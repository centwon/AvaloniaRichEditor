using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Formatters;

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FlowDocumentDto))]
internal partial class DocumentJsonContext : JsonSerializerContext { }

/// <summary>Serializes and deserializes a <see cref="FlowDocument"/> to/from the library's JSON format.
/// Images are stored as base64 of their original encoded bytes (no re-encoding); bitmaps
/// set without raw bytes fall back to base64 PNG. Uses AOT-safe source-generated JSON.</summary>
public static class DocumentSerializer
{
    /// <summary>Current JSON schema version written by <see cref="Serialize"/>. Bump when the on-disk
    /// shape changes; older documents (no "version" field) are read back as version 1.
    /// History: 1 = inline base64 per image; 2 = document-level image pool keyed by SHA-256,
    /// images reference the pool (identical bytes are stored once).</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Serializes <paramref name="document"/> to a JSON string.</summary>
    public static string Serialize(FlowDocument document)
    {
        var (dto, images) = BuildDto(document);
        return SerializeDto(dto, images);
    }

    // Builds the DTO + image pool from the model. Reads thread-affine brush colors (mutable
    // SolidColorBrush.Color is a StyledProperty), so this MUST run on the UI thread. Async save
    // paths call this synchronously on the caller's thread, then offload only SerializeDto/WriteDto.
    internal static (FlowDocumentDto Dto, Dictionary<string, (byte[] Bytes, string Mime)> Images) BuildDto(FlowDocument document)
    {
        var images = new Dictionary<string, (byte[] Bytes, string Mime)>();
        var dto = ToDto(document, images);
        return (dto, images);
    }

    // Pure-data half of Serialize: base64-encodes the image pool into the DTO and writes JSON. No
    // model/brush reads, so it is safe to run off the UI thread.
    internal static string SerializeDto(FlowDocumentDto dto, Dictionary<string, (byte[] Bytes, string Mime)> images)
    {
        if (images.Count > 0)
        {
            dto.Images = new Dictionary<string, ImagePoolDto>();
            foreach (var (key, img) in images)
                dto.Images[key] = new ImagePoolDto { Data = Convert.ToBase64String(img.Bytes), MimeType = img.Mime };
        }
        return JsonSerializer.Serialize(dto, DocumentJsonContext.Default.FlowDocumentDto);
    }

    // Builds the wire DTO; image bytes land in `images` keyed by content hash (the dto references
    // them via ImageRef). The caller picks the byte representation: base64 in the JSON pool
    // (Serialize) or zip entries (DocumentPackage).
    internal static FlowDocumentDto ToDto(FlowDocument document, Dictionary<string, (byte[] Bytes, string Mime)> images)
    {
        var dto = new FlowDocumentDto { Version = CurrentSchemaVersion };
        foreach (var block in document.Blocks) dto.Blocks.Add(BlockToDto(block, images));
        return dto;
    }

    /// <summary>Deserializes a <see cref="FlowDocument"/> from a JSON string produced by <see cref="Serialize"/>.
    /// Returns an empty document on parse errors. Image decoding is deferred to first render.</summary>
    public static FlowDocument Deserialize(string json)
    {
        var (dto, pool) = ParseJson(json);
        return FromDto(dto, pool);
    }

    // Thread-free half of Deserialize: JSON parsing + base64 decode only, no model objects. Async
    // loaders run this on a background thread and then build the model with FromDto on the UI
    // thread — model brushes/decorations are Avalonia thread-affine objects and must be created on
    // the thread that will render them.
    internal static (FlowDocumentDto? Dto, Dictionary<string, (byte[] Bytes, string Mime)> Pool) ParseJson(string json)
    {
        var dto = JsonSerializer.Deserialize(json, DocumentJsonContext.Default.FlowDocumentDto);
        // Decode each pool entry once; blocks referencing the same hash share one byte[] instance.
        var pool = new Dictionary<string, (byte[] Bytes, string Mime)>();
        if (dto?.Images != null)
            foreach (var (key, entry) in dto.Images)
            {
                var bytes = TryFromBase64(entry.Data);
                if (bytes != null) pool[key] = (bytes, entry.MimeType ?? "image/png");
            }
        return (dto, pool);
    }

    // Rebuilds the document from a DTO plus the resolved image pool (bytes already decoded from
    // base64 or read from package entries).
    internal static FlowDocument FromDto(FlowDocumentDto? dto, Dictionary<string, (byte[] Bytes, string Mime)> pool)
    {
        var doc = new FlowDocument();
        if (dto?.Blocks != null)
            foreach (var bd in dto.Blocks)
            {
                var b = DtoToBlock(bd, pool);
                if (b != null) doc.Blocks.Add(b);
            }
        return doc;
    }

    // ---- model -> dto ----

    private static BlockDto BlockToDto(Block block, Dictionary<string, (byte[] Bytes, string Mime)> pool)
    {
        switch (block)
        {
            case Paragraph p:
                return ParagraphToDto(p, pool);
            case DividerBlock dv:
                return new BlockDto { Type = "Divider", MarginTop = dv.MarginTop, MarginBottom = dv.MarginBottom };
            case ImageBlock img:
                // Bytes go to the document image pool (deduplicated by hash); the block stores only
                // the pool key. A bitmap set without bytes (legacy/consumer path) is PNG-encoded once.
                return new BlockDto
                {
                    Type = "Image",
                    // Only touch .Image (lazy decode) when there are no raw bytes — keeps "save"
                    // decode-free for byte-backed images (N6-2).
                    ImageRef = PoolImage(pool, img.RawBytes, img.MimeType, img.RawBytes == null ? img.Image : null),
                    Width = NanToNull(img.Width),
                    Height = NanToNull(img.Height),
                    Indent = img.Indent,
                    MarginTop = img.MarginTop,
                    MarginBottom = img.MarginBottom
                };
            case TableBlock tb:
                var td = new BlockDto
                {
                    Type = "Table",
                    Rows = tb.Rows,
                    Columns = tb.Columns,
                    Indent = tb.Indent,
                    MarginTop = tb.MarginTop,
                    MarginBottom = tb.MarginBottom,
                    ColumnWidths = new List<double>(tb.ColumnWidths),
                    RowHeights = new List<double>(tb.RowHeights),
                    Cells = new List<List<BlockDto>>()
                };
                foreach (var row in tb.Cells)
                {
                    var rd = new List<BlockDto>();
                    foreach (var cell in row) rd.Add(ParagraphToDto(cell, pool));
                    td.Cells.Add(rd);
                }
                td.ColSpans = new List<List<int>>();
                td.RowSpans = new List<List<int>>();
                foreach (var row in tb.ColSpans) td.ColSpans.Add(new List<int>(row));
                foreach (var row in tb.RowSpans) td.RowSpans.Add(new List<int>(row));
                return td;
            default:
                return new BlockDto { Type = "Paragraph", Inlines = new List<InlineDto>() };
        }
    }

    private static BlockDto ParagraphToDto(Paragraph p, Dictionary<string, (byte[] Bytes, string Mime)> pool)
    {
        var d = new BlockDto
        {
            Type = "Paragraph",
            Inlines = new List<InlineDto>(),
            TextAlignment = p.TextAlignment.ToString(),
            LineHeight = NanToNull(p.LineHeight),
            MarginTop = p.MarginTop,
            MarginBottom = p.MarginBottom,
            MarginRight = p.MarginRight,
            ListType = p.ListType.ToString(),
            HeadingLevel = p.HeadingLevel,
            Background = BrushToString(p.Background),
            Indent = p.Indent,
            IsQuote = p.IsQuote,
            ListLevel = p.ListLevel
        };
        foreach (var inline in p.Inlines)
        {
            if (inline is Run r)
                d.Inlines.Add(new InlineDto
                {
                    Type = "Run",
                    Text = r.Text,
                    Bold = r.FontWeight == FontWeight.Bold,
                    Italic = r.FontStyle == FontStyle.Italic,
                    FontSize = r.FontSize,
                    Foreground = BrushToString(r.Foreground),
                    Background = BrushToString(r.Background),
                    FontFamily = r.FontFamily,
                    NavigateUri = r.NavigateUri,
                    Underline = HasDecoration(r.TextDecorations, TextDecorationLocation.Underline),
                    Strikethrough = HasDecoration(r.TextDecorations, TextDecorationLocation.Strikethrough)
                });
            else if (inline is InlineImage im)
                d.Inlines.Add(new InlineDto
                {
                    Type = "Image",
                    ImageRef = PoolImage(pool, im.RawBytes, im.MimeType, im.RawBytes == null ? im.Image : null),
                    Width = im.Width,
                    Height = im.Height
                });
        }
        return d;
    }

    // ---- dto -> model ----

    private static Block? DtoToBlock(BlockDto d, Dictionary<string, (byte[] Bytes, string Mime)> pool)
    {
        switch (d.Type)
        {
            case "Divider":
                return new DividerBlock { MarginTop = d.MarginTop ?? 0, MarginBottom = d.MarginBottom ?? 0 };
            case "Image":
                {
                    // Bytes are kept encoded; the Bitmap is decoded lazily on first render.
                    var ib = new ImageBlock
                    {
                        Width = d.Width ?? double.NaN,
                        Height = d.Height ?? double.NaN,
                        Indent = d.Indent,
                        MarginTop = d.MarginTop ?? 0,
                        MarginBottom = d.MarginBottom ?? 10
                    };
                    if (ResolveImage(d.ImageRef, d.ImageBase64, d.MimeType, pool) is { } img)
                        ib.SetImageData(img.Bytes, img.Mime);
                    return ib;
                }
            case "Table":
                var tb = new TableBlock(Math.Max(1, d.Rows), Math.Max(1, d.Columns));
                tb.Indent = d.Indent;
                tb.MarginTop = d.MarginTop ?? 0;
                tb.MarginBottom = d.MarginBottom ?? 10;
                tb.Cells.Clear();
                tb.ColumnWidths.Clear();
                tb.RowHeights.Clear();
                if (d.ColumnWidths != null) tb.ColumnWidths.AddRange(d.ColumnWidths);
                if (d.RowHeights != null) tb.RowHeights.AddRange(d.RowHeights);
                if (d.Cells != null)
                    foreach (var rowD in d.Cells)
                    {
                        var row = new List<Paragraph>();
                        foreach (var cd in rowD) row.Add(DtoToParagraph(cd, pool));
                        tb.Cells.Add(row);
                    }
                tb.Rows = tb.Cells.Count;
                int maxCols = Math.Max(1, d.Columns);
                foreach (var row in tb.Cells) if (row.Count > maxCols) maxCols = row.Count;
                tb.Columns = maxCols;
                // Pad jagged/short rows so the grid stays rectangular — corrupt or hand-edited JSON
                // could otherwise make Cells[r][c] throw in render/layout/hit-testing.
                foreach (var row in tb.Cells)
                    while (row.Count < tb.Columns) row.Add(new Paragraph { Inlines = { new Run { Text = "" } } });
                // Rebuild span grids to match the loaded cell grid. Missing/legacy docs default to 1×1.
                tb.ColSpans.Clear();
                tb.RowSpans.Clear();
                for (int r = 0; r < tb.Rows; r++)
                {
                    var cs = new List<int>();
                    var rs = new List<int>();
                    for (int c = 0; c < tb.Columns; c++)
                    {
                        cs.Add(d.ColSpans != null && r < d.ColSpans.Count && c < d.ColSpans[r].Count ? d.ColSpans[r][c] : 1);
                        rs.Add(d.RowSpans != null && r < d.RowSpans.Count && c < d.RowSpans[r].Count ? d.RowSpans[r][c] : 1);
                    }
                    tb.ColSpans.Add(cs);
                    tb.RowSpans.Add(rs);
                }
                return tb;
            default:
                return DtoToParagraph(d, pool);
        }
    }

    private static Paragraph DtoToParagraph(BlockDto d, Dictionary<string, (byte[] Bytes, string Mime)> pool)
    {
        var p = new Paragraph
        {
            LineHeight = d.LineHeight ?? double.NaN,
            MarginTop = d.MarginTop ?? 0,
            MarginBottom = d.MarginBottom ?? 10,
            MarginRight = d.MarginRight ?? 0,
            HeadingLevel = d.HeadingLevel,
            Background = StringToBrush(d.Background),
            Indent = d.Indent,
            IsQuote = d.IsQuote,
            ListLevel = d.ListLevel,
            ListType = Enum.TryParse<ListKind>(d.ListType, out var lk) ? lk : (d.IsListItem ? ListKind.Bullet : ListKind.None)
        };
        if (Enum.TryParse<TextAlignment>(d.TextAlignment, out var ta)) p.TextAlignment = ta;
        if (d.Inlines != null)
            foreach (var id in d.Inlines)
            {
                if (id.Type == "Image")
                {
                    var im = new InlineImage
                    {
                        Width = id.Width ?? 16,
                        Height = id.Height ?? 16
                    };
                    if (ResolveImage(id.ImageRef, id.ImageBase64, id.MimeType, pool) is { } img)
                        im.SetImageData(img.Bytes, img.Mime);
                    p.Inlines.Add(im);
                }
                else
                    p.Inlines.Add(new Run
                    {
                        Text = id.Text,
                        FontWeight = id.Bold ? FontWeight.Bold : FontWeight.Normal,
                        FontStyle = id.Italic ? FontStyle.Italic : FontStyle.Normal,
                        FontSize = id.FontSize <= 0 ? 14 : id.FontSize,
                        Foreground = StringToBrush(id.Foreground),
                        Background = StringToBrush(id.Background),
                        FontFamily = id.FontFamily,
                        NavigateUri = id.NavigateUri,
                        TextDecorations = BuildDecorations(id.Underline, id.Strikethrough)
                    });
            }
        return p;
    }

    // ---- helpers ----

    private static double? NanToNull(double v) => double.IsNaN(v) ? (double?)null : v;

    private static bool HasDecoration(TextDecorationCollection? decos, TextDecorationLocation loc)
    {
        if (decos == null) return false;
        foreach (var d in decos) if (d.Location == loc) return true;
        return false;
    }

    private static TextDecorationCollection? BuildDecorations(bool underline, bool strikethrough)
    {
        if (!underline && !strikethrough) return null;
        var c = new TextDecorationCollection();
        if (underline) c.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        if (strikethrough) c.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        return c;
    }

    private static string? BrushToString(IBrush? brush) =>
        brush is ISolidColorBrush s ? s.Color.ToString() : null;

    private static IBrush? StringToBrush(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        // Immutable: no dispatcher thread affinity (mutable SolidColorBrush created off the UI
        // thread crashes the compositor on first render) and no per-brush change tracking.
        try { return new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse(value)); }
        catch { return null; }
    }

    // Adds the image bytes to the document pool (deduplicated by content hash) and returns the
    // pool key, or null when there is no image data. RawBytes are stored as-is (no re-encoding);
    // a bitmap without bytes falls back to PNG encoding.
    private static string? PoolImage(Dictionary<string, (byte[] Bytes, string Mime)> pool, byte[]? rawBytes, string? mimeType, Bitmap? bmp)
    {
        byte[]? bytes = rawBytes ?? BitmapToBytes(bmp);
        if (bytes == null) return null;
        string mime = rawBytes != null ? (mimeType ?? "image/png") : "image/png";
        string key = Convert.ToHexString(SHA256.HashData(bytes));
        if (!pool.ContainsKey(key)) pool[key] = (bytes, mime);
        return key;
    }

    // Resolves an image dto to its bytes: pool reference (v2) first, inline base64 (v1 legacy)
    // as fallback. Legacy documents without a MimeType were always PNG-encoded.
    private static (byte[] Bytes, string Mime)? ResolveImage(string? imageRef, string? inlineBase64, string? mimeType,
        Dictionary<string, (byte[] Bytes, string Mime)> pool)
    {
        if (imageRef != null && pool.TryGetValue(imageRef, out var pooled)) return pooled;
        var bytes = TryFromBase64(inlineBase64);
        return bytes != null ? (bytes, mimeType ?? "image/png") : null;
    }

    private static byte[]? BitmapToBytes(Bitmap? bmp)
    {
        if (bmp == null) return null;
        try
        {
            using var ms = new MemoryStream();
            bmp.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    private static byte[]? TryFromBase64(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try { return Convert.FromBase64String(value); }
        catch { return null; }
    }
}

internal class FlowDocumentDto
{
    // Schema version. Absent in pre-versioning documents → deserializes to the initializer (1).
    public int Version { get; set; } = 1;
    public List<BlockDto> Blocks { get; set; } = new();
    // v2: image pool keyed by SHA-256 hex of the encoded bytes; identical images stored once.
    public Dictionary<string, ImagePoolDto>? Images { get; set; }
}

internal class ImagePoolDto
{
    public string? Data { get; set; } // base64 of the original encoded bytes
    public string? MimeType { get; set; }
}

internal class BlockDto
{
    public string Type { get; set; } = "Paragraph";

    // Paragraph
    public List<InlineDto>? Inlines { get; set; }
    public string? TextAlignment { get; set; }
    public double? LineHeight { get; set; }
    // Nullable so pre-margin documents (field absent) fall back to the per-block-type defaults
    // instead of 0 — images/tables historically rendered with a fixed 10px bottom gap.
    public double? MarginTop { get; set; }
    public double? MarginBottom { get; set; }
    public double? MarginRight { get; set; } // paragraphs only (narrows the wrap width)
    public bool IsListItem { get; set; } // legacy (read fallback); replaced by ListType
    public string? ListType { get; set; }
    public int HeadingLevel { get; set; }
    public string? Background { get; set; }
    public double Indent { get; set; }
    public bool IsQuote { get; set; }
    public int ListLevel { get; set; }

    // Image block
    public string? ImageRef { get; set; } // v2: key into FlowDocumentDto.Images
    public string? ImageBase64 { get; set; } // v1 legacy: inline base64 (read fallback)
    public string? MimeType { get; set; } // of ImageBase64 bytes; absent in legacy docs => image/png
    public double? Width { get; set; }
    public double? Height { get; set; }

    // Table block
    public int Rows { get; set; }
    public int Columns { get; set; }
    public List<double>? ColumnWidths { get; set; }
    public List<double>? RowHeights { get; set; }
    public List<List<BlockDto>>? Cells { get; set; }
    public List<List<int>>? ColSpans { get; set; }
    public List<List<int>>? RowSpans { get; set; }
}

internal class InlineDto
{
    public string Type { get; set; } = "Run";

    // Run
    public string? Text { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public double FontSize { get; set; } = 14;
    public string? Foreground { get; set; }
    public string? Background { get; set; }
    public string? FontFamily { get; set; }
    public string? NavigateUri { get; set; }
    public bool Underline { get; set; }
    public bool Strikethrough { get; set; }

    // Inline image
    public string? ImageRef { get; set; } // v2: key into FlowDocumentDto.Images
    public string? ImageBase64 { get; set; } // v1 legacy: inline base64 (read fallback)
    public string? MimeType { get; set; } // of ImageBase64 bytes; absent in legacy docs => image/png
    public double? Width { get; set; }
    public double? Height { get; set; }
}
