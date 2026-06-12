using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;

namespace AvaloniaRichEditor.Controls;

// Clipboard paste + external-content ingestion (HTML/CF_HTML, image, Excel/TSV->table) and drag-drop.
// Part of RichEditor (split out of the main file for readability).
public partial class RichEditor  // doc comment lives on the primary declaration in RichEditor.cs
{
    /// <summary>Pastes from the system clipboard. Priority: internal rich → external HTML → plain text.</summary>
    public async Task PasteFromClipboardAsync()
    {
        if (Document == null || IsReadOnly) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        string? text = await clipboard.TryGetTextAsync();

        // 1. Internal rich clipboard: if the system text still matches what we last copied
        //    in-app, paste the formatted version (blocks/tables when available, else runs).
        //    Skipped when rich paste is disabled (Basic mode) — falls through to plain text below.
        if (AllowRichPaste && _internalClipboardText != null && text == _internalClipboardText &&
            (_internalClipboardBlocks != null || _internalClipboard != null))
        {
            PushUndo();
            if (_internalClipboardBlocks != null)
                InsertBlocks(_internalClipboardBlocks);
            else
                InsertRuns(_internalClipboard!);
            ResetCaretBlink(); // caret sits at the end of the pasted content — scroll it into view
            return;
        }

        // 2. External HTML (from browsers, Word, etc.): parse and insert with formatting.
        string? html = AllowRichPaste ? await TryGetHtmlAsync(clipboard) : null;
        if (!string.IsNullOrEmpty(html))
        {
            // Malformed/exotic HTML can make the parser throw; fall through to the plain-text
            // fallback below instead of letting the exception escape this fire-and-forget task.
            try
            {
                string fragment = ExtractHtmlFragment(html!);
                var parsed = Formatters.HtmlDocumentFormatter.ParseHtml(fragment);
                if (parsed.Blocks.Count > 0)
                {
                    PushUndo();
                    InsertParsedDocument(parsed);
                    ResetCaretBlink(); // caret sits at the end of the pasted content — scroll it into view
                    return;
                }
            }
            catch { /* fall back to plain text */ }
        }

        // 3. Bitmap image on the clipboard (e.g. a screenshot or copied picture). Images copied
        //    in-app carry a meta string restoring inline-ness and display size.
        var (clipImage, clipBytes, clipMeta) = AllowImages
            ? await TryGetImageAsync(clipboard)
            : ((Avalonia.Media.Imaging.Bitmap?)null, (byte[]?)null, (string?)null);
        if (clipImage != null)
        {
            var meta = ParseImageMeta(clipMeta);
            if (meta is { Inline: true } im && clipBytes != null)
            {
                PushUndo();
                InsertInlineImageAtCaret(clipBytes, im.W, im.H);
            }
            else if (clipBytes != null) InsertImageBytes(clipBytes, meta?.W ?? 0, meta?.H ?? 0); // keep the original encoding
            else InsertImage(Downscale(clipImage));                  // raw Bitmap object: no bytes to keep
            ResetCaretBlink(); // image lands just after the caret block — scroll there
            return;
        }

        // 4. Tab-separated text (e.g. Excel/HWP cells copied without HTML) -> rebuild as a table.
        if (AllowTables && !string.IsNullOrEmpty(text) && LooksTabular(text))
        {
            PushUndo();
            InsertTableFromTsv(text);
            ResetCaretBlink(); // table lands just after the caret block — scroll there
            return;
        }

        // 5. Plain text fallback.
        if (!string.IsNullOrEmpty(text))
        {
            PushUndo();
            InsertText(text);
            ResetCaretBlink(); // caret sits at the end of the pasted text — scroll it into view
        }
    }

    // Heuristic for "this plain text is a copied spreadsheet grid". Every non-empty line must
    // contain a tab (a grid has uniform columns) and at least one line must have 2+ non-empty
    // cells — otherwise tab-indented prose/code ("\tfoo" splits into ["", "foo"]) would paste
    // as a bogus table. (internal for test coverage.)
    internal static bool LooksTabular(string text)
    {
        bool anyMultiCell = false;
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (line.Length == 0) continue;
            if (!line.Contains('\t')) return false;
            int nonEmpty = 0;
            foreach (var f in line.Split('\t')) if (!string.IsNullOrWhiteSpace(f)) nonEmpty++;
            if (nonEmpty >= 2) anyMultiCell = true;
        }
        return anyMultiCell;
    }

    private void InsertTableFromTsv(string text)
    {
        if (Document == null) return;
        var lines = new List<string>(text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        while (lines.Count > 1 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        int cols = 1;
        foreach (var l in lines) cols = Math.Max(cols, l.Split('\t').Length);

        var tb = new TableBlock(lines.Count, cols);
        tb.Cells.Clear();
        tb.ColumnWidths.Clear();
        for (int c = 0; c < cols; c++) tb.ColumnWidths.Add(100);
        foreach (var l in lines)
        {
            var parts = l.Split('\t');
            var row = new List<Paragraph>();
            for (int c = 0; c < cols; c++)
            {
                var pp = new Paragraph();
                pp.Inlines.Add(new Run { Text = c < parts.Length ? parts[c] : "" });
                row.Add(pp);
            }
            tb.Cells.Add(row);
        }
        tb.Rows = lines.Count;
        tb.Columns = cols;
        InsertBlockAtCaret(tb);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = (!IsReadOnly && AllowImages && e.DataTransfer.Contains(DataFormat.File)) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (IsReadOnly || !AllowImages) return;
        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;
        foreach (var f in files)
        {
            var path = f.Path?.LocalPath;
            if (string.IsNullOrEmpty(path)) continue;
            try
            {
                InsertImageBytes(System.IO.File.ReadAllBytes(path));
            }
            catch { }
        }
    }

    /// <summary>Inserts an image from its original encoded bytes (JPEG/PNG/...), keeping them as
    /// the document's data (<see cref="ImageBlock.RawBytes"/>) so saving never re-encodes. Prefer
    /// this over <see cref="InsertImage"/> when the encoded bytes are available. Oversized images
    /// are downscaled once here (PNG); later drag-handle resizes only change Width/Height.</summary>
    public void InsertImageBytes(byte[] bytes) => InsertImageBytes(bytes, 0, 0);

    // Core insert with an optional display size (used by paste to restore the copied image's size;
    // 0 = natural size).
    private void InsertImageBytes(byte[] bytes, double displayW, double displayH)
    {
        if (Document == null || IsReadOnly || !AllowImages) return;
        Avalonia.Media.Imaging.Bitmap bmp;
        try
        {
            using var ms = new System.IO.MemoryStream(bytes);
            bmp = new Avalonia.Media.Imaging.Bitmap(ms);
        }
        catch { return; }

        var scaled = Downscale(bmp);
        var ib = new ImageBlock
        {
            Width = displayW > 0 ? displayW : scaled.Size.Width,
            Height = displayH > 0 ? displayH : scaled.Size.Height
        };
        if (!ReferenceEquals(scaled, bmp))
        {
            using var ms = new System.IO.MemoryStream();
            scaled.Save(ms);
            ib.SetImageData(ms.ToArray(), "image/png", scaled);
        }
        else
        {
            ib.SetImageData(bytes, ImageMime.Detect(bytes), bmp);
        }

        PushUndo();
        InsertBlockAtCaret(ib);
        InvalidateVisual();
    }

    // Scales an image down to fit within maxW x maxH (keeping aspect ratio).
    private static Avalonia.Media.Imaging.Bitmap Downscale(Avalonia.Media.Imaging.Bitmap bmp, int maxW = 1920, int maxH = 1080)
    {
        var ps = bmp.PixelSize;
        if (ps.Width <= maxW && ps.Height <= maxH) return bmp;
        double ratio = Math.Min((double)maxW / ps.Width, (double)maxH / ps.Height);
        var size = new PixelSize(Math.Max(1, (int)(ps.Width * ratio)), Math.Max(1, (int)(ps.Height * ratio)));
        try { return bmp.CreateScaledBitmap(size, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality); }
        catch { return bmp; }
    }

    // Application-private clipboard formats: the image's original encoded bytes (so an in-editor
    // copy/paste round-trips without re-encoding) plus a small meta string "inline;width;height"
    // (so an inline image pastes back as inline, keeping its display size). A decoded Bitmap is
    // written alongside them for other applications.
    private static readonly DataFormat<byte[]> AreImageFormat =
        DataFormat.CreateBytesApplicationFormat("AvaloniaRichEditor.Image");
    private static readonly DataFormat<string> AreImageMetaFormat =
        DataFormat.CreateStringApplicationFormat("AvaloniaRichEditor.ImageMeta");

    // Copies an image (block or inline) to the OS clipboard. Paste re-enters through the image
    // branch of PasteFromClipboardAsync, which prefers the original-bytes format.
    private async Task CopyImageToClipboardAsync(byte[]? rawBytes, Bitmap? bmp, bool inline, double width, double height)
    {
        byte[]? bytes = rawBytes;
        if (bytes == null && bmp != null)
        {
            try
            {
                using var ms = new System.IO.MemoryStream();
                bmp.Save(ms);
                bytes = ms.ToArray();
            }
            catch { return; }
        }
        if (bytes == null) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;

        // The text-based internal slots are stale now; they must not hijack the next paste.
        _internalClipboard = null;
        _internalClipboardText = null;
        _internalClipboardBlocks = null;

        try
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var item = DataTransferItem.Create(AreImageFormat, bytes);
            item.Set(AreImageMetaFormat, $"{(inline ? 1 : 0)};{width.ToString(inv)};{height.ToString(inv)}");
            // The Bitmap representation is best-effort (for other apps); never let it break the copy.
            try
            {
                if (bmp == null) { using var ms = new System.IO.MemoryStream(bytes); bmp = new Bitmap(ms); }
                item.Set(DataFormat.Bitmap, bmp);
            }
            catch { }
            var dt = new DataTransfer();
            dt.Add(item);
            await clipboard.SetDataAsync(dt);
        }
        catch { }
    }

    // Parses the meta string written by CopyImageToClipboardAsync. Null on any mismatch.
    private static (bool Inline, double W, double H)? ParseImageMeta(string? meta)
    {
        if (string.IsNullOrEmpty(meta)) return null;
        var parts = meta.Split(';');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (parts.Length != 3
            || !int.TryParse(parts[0], out int inl)
            || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out double w)
            || !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out double h))
            return null;
        return (inl == 1, w, h);
    }

    // Returns the decoded bitmap plus, when the clipboard handed us encoded bytes, those original
    // bytes (so the document can keep them without re-encoding), plus the editor's own meta string
    // when the image was copied in-app. Bytes are null when the clipboard produced a Bitmap object.
    private static async Task<(Avalonia.Media.Imaging.Bitmap?, byte[]?, string?)> TryGetImageAsync(IClipboard clipboard)
    {
        Avalonia.Input.IAsyncDataTransfer? dt;
        try { dt = await clipboard.TryGetDataAsync(); }
        catch { return (null, null, null); }
        if (dt == null) return (null, null, null);

        try
        {
            // First pass: our own format — original encoded bytes, no generation loss.
            foreach (var item in dt.Items)
            {
                byte[]? own = null;
                string? meta = null;
                foreach (var fmt in item.Formats)
                {
                    object? raw;
                    if (fmt.Identifier == AreImageFormat.Identifier)
                    {
                        try { raw = await item.TryGetRawAsync(fmt); } catch { continue; }
                        if (raw is byte[] { Length: > 0 } ob) own = ob;
                    }
                    else if (fmt.Identifier == AreImageMetaFormat.Identifier)
                    {
                        try { raw = await item.TryGetRawAsync(fmt); } catch { continue; }
                        if (raw is string ms) meta = ms;
                        else if (raw is byte[] mb) meta = System.Text.Encoding.UTF8.GetString(mb);
                    }
                }
                if (own != null)
                {
                    try { using var ms = new System.IO.MemoryStream(own); return (new Bitmap(ms), own, meta); }
                    catch { }
                }
            }

            foreach (var item in dt.Items)
            {
                foreach (var fmt in item.Formats)
                {
                    var id = fmt.Identifier ?? fmt.ToString() ?? "";
                    bool looksImage = id.IndexOf("png", StringComparison.OrdinalIgnoreCase) >= 0
                        || id.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0
                        || id.IndexOf("bitmap", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!looksImage) continue;

                    object? raw;
                    try { raw = await item.TryGetRawAsync(fmt); }
                    catch { continue; }

                    if (raw is Avalonia.Media.Imaging.Bitmap bm) return (bm, null, null);
                    byte[]? bytes = raw as byte[];
                    if (bytes == null && raw is System.IO.Stream s)
                    {
                        using var ms = new System.IO.MemoryStream();
                        s.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                    if (bytes is { Length: > 0 })
                    {
                        try { using var ms = new System.IO.MemoryStream(bytes); return (new Avalonia.Media.Imaging.Bitmap(ms), bytes, null); }
                        catch { }
                    }
                }
            }
        }
        finally { (dt as IDisposable)?.Dispose(); }
        return (null, null, null);
    }

    private static async Task<string?> TryGetHtmlAsync(IClipboard clipboard)
    {
        Avalonia.Input.IAsyncDataTransfer? dt;
        try { dt = await clipboard.TryGetDataAsync(); }
        catch { return null; }
        if (dt == null) return null;

        try
        {
            foreach (var item in dt.Items)
            {
                foreach (var fmt in item.Formats)
                {
                    var id = fmt.Identifier ?? fmt.ToString() ?? "";
                    if (id.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        object? raw;
                        try { raw = await item.TryGetRawAsync(fmt); }
                        catch { continue; }
                        if (raw is string s && !string.IsNullOrWhiteSpace(s)) return s;
                        if (raw is byte[] b) return System.Text.Encoding.UTF8.GetString(b);
                    }
                }
            }
        }
        finally
        {
            (dt as IDisposable)?.Dispose();
        }
        return null;
    }

    // Strips the Windows CF_HTML ("HTML Format") header/fragment markers down to the markup.
    // (internal for test coverage — CF_HTML stripping is a documented Windows-specific pitfall.)
    internal static string ExtractHtmlFragment(string raw)
    {
        const string startTag = "<!--StartFragment-->";
        const string endTag = "<!--EndFragment-->";
        int sf = raw.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        int ef = raw.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);
        if (sf >= 0 && ef > sf)
            return raw.Substring(sf + startTag.Length, ef - (sf + startTag.Length));

        if (raw.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
        {
            int lt = raw.IndexOf('<');
            if (lt >= 0) return raw.Substring(lt);
        }
        return raw;
    }
}
