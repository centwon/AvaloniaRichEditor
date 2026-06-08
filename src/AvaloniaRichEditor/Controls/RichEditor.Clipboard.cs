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
public partial class RichEditor
{
    public async Task PasteFromClipboardAsync()
    {
        if (Document == null) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        string? text = await clipboard.TryGetTextAsync();

        // 1. Internal rich clipboard: if the system text still matches what we last copied
        //    in-app, paste the formatted version (blocks/tables when available, else runs).
        if (_internalClipboardText != null && text == _internalClipboardText &&
            (_internalClipboardBlocks != null || _internalClipboard != null))
        {
            PushUndo();
            if (_internalClipboardBlocks != null)
            {
                InsertBlocks(_internalClipboardBlocks);
                InvalidateVisual();
            }
            else
            {
                InsertRuns(_internalClipboard!);
            }
            return;
        }

        // 2. External HTML (from browsers, Word, etc.): parse and insert with formatting.
        string? html = await TryGetHtmlAsync(clipboard);
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
                    InvalidateVisual();
                    return;
                }
            }
            catch { /* fall back to plain text */ }
        }

        // 3. Bitmap image on the clipboard (e.g. a screenshot or copied picture).
        var clipImage = await TryGetImageAsync(clipboard);
        if (clipImage != null)
        {
            InsertImage(Downscale(clipImage));
            return;
        }

        // 4. Tab-separated text (e.g. Excel/HWP cells copied without HTML) -> rebuild as a table.
        if (!string.IsNullOrEmpty(text) && LooksTabular(text))
        {
            PushUndo();
            InsertTableFromTsv(text);
            InvalidateVisual();
            return;
        }

        // 5. Plain text fallback.
        if (!string.IsNullOrEmpty(text))
        {
            PushUndo();
            InsertText(text);
        }
    }

    // Heuristic: a tab plus at least one row that splits into 2+ columns => tabular paste.
    private static bool LooksTabular(string text)
    {
        if (!text.Contains('\t')) return false;
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            if (line.Split('\t').Length >= 2) return true;
        return false;
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
        e.DragEffects = (!IsReadOnly && e.DataTransfer.Contains(DataFormat.File)) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (IsReadOnly) return;
        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;
        foreach (var f in files)
        {
            var path = f.Path?.LocalPath;
            if (string.IsNullOrEmpty(path)) continue;
            try
            {
                using var st = System.IO.File.OpenRead(path);
                var bmp = new Avalonia.Media.Imaging.Bitmap(st);
                InsertImage(Downscale(bmp));
            }
            catch { }
        }
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

    private static async Task<Avalonia.Media.Imaging.Bitmap?> TryGetImageAsync(IClipboard clipboard)
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
                    bool looksImage = id.IndexOf("png", StringComparison.OrdinalIgnoreCase) >= 0
                        || id.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0
                        || id.IndexOf("bitmap", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!looksImage) continue;

                    object? raw;
                    try { raw = await item.TryGetRawAsync(fmt); }
                    catch { continue; }

                    if (raw is Avalonia.Media.Imaging.Bitmap bm) return bm;
                    byte[]? bytes = raw as byte[];
                    if (bytes == null && raw is System.IO.Stream s)
                    {
                        using var ms = new System.IO.MemoryStream();
                        s.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                    if (bytes is { Length: > 0 })
                    {
                        try { using var ms = new System.IO.MemoryStream(bytes); return new Avalonia.Media.Imaging.Bitmap(ms); }
                        catch { }
                    }
                }
            }
        }
        finally { (dt as IDisposable)?.Dispose(); }
        return null;
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
    private static string ExtractHtmlFragment(string raw)
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
