using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Formatters;

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FlowDocumentDto))]
internal partial class DocumentJsonContext : JsonSerializerContext { }

// Full document persistence: paragraphs, runs (bold/italic/underline/strikethrough/size/color/link),
// inline images, image blocks and tables (column widths, row heights, cell contents). Images are
// stored as base64 of their original encoded bytes (RawBytes + MimeType, no re-encoding); bitmaps
// set without bytes fall back to base64 PNG. A flat BlockDto/InlineDto with a Type discriminator
// keeps the schema AOT/source-generator friendly (no polymorphic serialization).
public static class DocumentSerializer
{
    /// <summary>Current JSON schema version written by <see cref="Serialize"/>. Bump when the on-disk
    /// shape changes; older documents (no "version" field) are read back as version 1.</summary>
    public const int CurrentSchemaVersion = 1;

    public static string Serialize(FlowDocument document)
    {
        var dto = new FlowDocumentDto { Version = CurrentSchemaVersion };
        foreach (var block in document.Blocks) dto.Blocks.Add(BlockToDto(block));
        return JsonSerializer.Serialize(dto, DocumentJsonContext.Default.FlowDocumentDto);
    }

    public static FlowDocument Deserialize(string json)
    {
        var doc = new FlowDocument();
        var dto = JsonSerializer.Deserialize(json, DocumentJsonContext.Default.FlowDocumentDto);
        if (dto?.Blocks != null)
            foreach (var bd in dto.Blocks)
            {
                var b = DtoToBlock(bd);
                if (b != null) doc.Blocks.Add(b);
            }
        return doc;
    }

    // ---- model -> dto ----

    private static BlockDto BlockToDto(Block block)
    {
        switch (block)
        {
            case Paragraph p:
                return ParagraphToDto(p);
            case DividerBlock:
                return new BlockDto { Type = "Divider" };
            case ImageBlock img:
                // RawBytes go straight to base64 (no re-encoding); a bitmap set without bytes
                // (legacy/consumer path) falls back to PNG encoding as before.
                return new BlockDto
                {
                    Type = "Image",
                    ImageBase64 = img.RawBytes != null ? Convert.ToBase64String(img.RawBytes) : BitmapToBase64(img.Image),
                    MimeType = img.RawBytes != null ? img.MimeType : null,
                    Width = NanToNull(img.Width),
                    Height = NanToNull(img.Height),
                    Indent = img.Indent
                };
            case TableBlock tb:
                var td = new BlockDto
                {
                    Type = "Table",
                    Rows = tb.Rows,
                    Columns = tb.Columns,
                    Indent = tb.Indent,
                    ColumnWidths = new List<double>(tb.ColumnWidths),
                    RowHeights = new List<double>(tb.RowHeights),
                    Cells = new List<List<BlockDto>>()
                };
                foreach (var row in tb.Cells)
                {
                    var rd = new List<BlockDto>();
                    foreach (var cell in row) rd.Add(ParagraphToDto(cell));
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

    private static BlockDto ParagraphToDto(Paragraph p)
    {
        var d = new BlockDto
        {
            Type = "Paragraph",
            Inlines = new List<InlineDto>(),
            TextAlignment = p.TextAlignment.ToString(),
            LineHeight = NanToNull(p.LineHeight),
            MarginTop = p.MarginTop,
            MarginBottom = p.MarginBottom,
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
                    ImageBase64 = im.RawBytes != null ? Convert.ToBase64String(im.RawBytes) : BitmapToBase64(im.Image),
                    MimeType = im.RawBytes != null ? im.MimeType : null,
                    Width = im.Width,
                    Height = im.Height
                });
        }
        return d;
    }

    // ---- dto -> model ----

    private static Block? DtoToBlock(BlockDto d)
    {
        switch (d.Type)
        {
            case "Divider":
                return new DividerBlock();
            case "Image":
            {
                // Bytes are kept encoded; the Bitmap is decoded lazily on first render.
                // Legacy documents (no MimeType field) were always PNG-encoded.
                var ib = new ImageBlock
                {
                    Width = d.Width ?? double.NaN,
                    Height = d.Height ?? double.NaN,
                    Indent = d.Indent
                };
                var bytes = TryFromBase64(d.ImageBase64);
                if (bytes != null) ib.SetImageData(bytes, d.MimeType ?? "image/png");
                return ib;
            }
            case "Table":
                var tb = new TableBlock(Math.Max(1, d.Rows), Math.Max(1, d.Columns));
                tb.Indent = d.Indent;
                tb.Cells.Clear();
                tb.ColumnWidths.Clear();
                tb.RowHeights.Clear();
                if (d.ColumnWidths != null) tb.ColumnWidths.AddRange(d.ColumnWidths);
                if (d.RowHeights != null) tb.RowHeights.AddRange(d.RowHeights);
                if (d.Cells != null)
                    foreach (var rowD in d.Cells)
                    {
                        var row = new List<Paragraph>();
                        foreach (var cd in rowD) row.Add(DtoToParagraph(cd));
                        tb.Cells.Add(row);
                    }
                tb.Rows = tb.Cells.Count;
                tb.Columns = tb.Cells.Count > 0 ? tb.Cells[0].Count : d.Columns;
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
                return DtoToParagraph(d);
        }
    }

    private static Paragraph DtoToParagraph(BlockDto d)
    {
        var p = new Paragraph
        {
            LineHeight = d.LineHeight ?? double.NaN,
            MarginTop = d.MarginTop,
            MarginBottom = d.MarginBottom,
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
                    var bytes = TryFromBase64(id.ImageBase64);
                    if (bytes != null) im.SetImageData(bytes, id.MimeType ?? "image/png");
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
        try { return new SolidColorBrush(Color.Parse(value)); } catch { return null; }
    }

    private static string? BitmapToBase64(Bitmap? bmp)
    {
        if (bmp == null) return null;
        try
        {
            using var ms = new MemoryStream();
            bmp.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
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
}

internal class BlockDto
{
    public string Type { get; set; } = "Paragraph";

    // Paragraph
    public List<InlineDto>? Inlines { get; set; }
    public string? TextAlignment { get; set; }
    public double? LineHeight { get; set; }
    public double MarginTop { get; set; }
    public double MarginBottom { get; set; }
    public bool IsListItem { get; set; } // legacy (read fallback); replaced by ListType
    public string? ListType { get; set; }
    public int HeadingLevel { get; set; }
    public string? Background { get; set; }
    public double Indent { get; set; }
    public bool IsQuote { get; set; }
    public int ListLevel { get; set; }

    // Image block
    public string? ImageBase64 { get; set; }
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
    public string? ImageBase64 { get; set; }
    public string? MimeType { get; set; } // of ImageBase64 bytes; absent in legacy docs => image/png
    public double? Width { get; set; }
    public double? Height { get; set; }
}
