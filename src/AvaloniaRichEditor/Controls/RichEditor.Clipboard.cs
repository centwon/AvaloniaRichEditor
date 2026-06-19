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
                InsertInlines(_internalClipboard!);
            ResetCaretBlink(); // caret sits at the end of the pasted content — scroll it into view
            return;
        }

        // 2. External RTF (Word/HWP): tried before CF_HTML because RTF embeds image bytes, whereas
        //    Word's CF_HTML only references temp files that may already be gone. Browsers don't put
        //    RTF on the clipboard, so their HTML path (below) is unaffected.
        string? rtf = AllowRichPaste ? await TryGetRtfAsync(clipboard) : null;
        if (!string.IsNullOrEmpty(rtf) && Formatters.RtfDocumentFormatter.LooksLikeRtf(rtf))
        {
            try
            {
                var parsedRtf = Formatters.RtfDocumentFormatter.Parse(rtf!);
                bool empty = parsedRtf.Blocks.Count == 0
                    || (parsedRtf.Blocks.Count == 1 && parsedRtf.Blocks[0] is Paragraph ep && ep.Inlines.Count == 0);
                if (!empty)
                {
                    PushUndo();
                    InsertParsedDocument(parsedRtf);
                    ResetCaretBlink();
                    return;
                }
            }
            catch { /* fall through to HTML/plain */ }
        }

        // 3. External HTML (from browsers, Word, etc.): parse and insert with formatting.
        string? html = AllowRichPaste ? await TryGetHtmlAsync(clipboard) : null;
        if (!string.IsNullOrEmpty(html))
        {
            // Malformed/exotic HTML can make the parser throw; fall through to the plain-text
            // fallback below instead of letting the exception escape this fire-and-forget task.
            try
            {
                string fragment = ExtractHtmlFragment(html!);
                // Async: remote images download off the UI thread so a slow network can't freeze
                // the paste (model build stays on the UI thread inside ParseHtmlAsync).
                var parsed = await Formatters.HtmlDocumentFormatter.ParseHtmlAsync(fragment, AllowLocalFileImages);
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

        // 4. Bitmap image on the clipboard (e.g. a screenshot or copied picture). Images copied
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

        // 5. Tab-separated text (e.g. Excel/HWP cells copied without HTML) -> rebuild as a table.
        if (AllowTables && !string.IsNullOrEmpty(text) && LooksTabular(text))
        {
            PushUndo();
            InsertTableFromTsv(text);
            ResetCaretBlink(); // table lands just after the caret block — scroll there
            return;
        }

        // 6. Plain text fallback.
        if (!string.IsNullOrEmpty(text))
        {
            PushUndo();
            InsertText(text);
            ResetCaretBlink(); // caret sits at the end of the pasted text — scroll it into view
        }
    }

    // Ctrl+Shift+V: pastes the clipboard's plain text only, ignoring rich/HTML/image formats
    // (and the TSV-to-table heuristic — "paste as plain text" must never build structure).
    private async Task PastePlainTextAsync()
    {
        if (Document == null || IsReadOnly) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        string? text = null;
        try { text = await clipboard.TryGetTextAsync(); } catch { }
        if (string.IsNullOrEmpty(text)) return;
        PushUndo();
        InsertText(text);
        ResetCaretBlink();
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

        // The constructor builds Cells/ColumnWidths/span grids consistently; just fill the cells'
        // text in place (rebuilding Cells alone would desync the span grids).
        var tb = new TableBlock(lines.Count, cols);
        for (int r = 0; r < lines.Count; r++)
        {
            var parts = lines[r].Split('\t');
            for (int c = 0; c < cols; c++)
                ((Run)tb.Cells[r][c].Para.Inlines[0]).Text = c < parts.Length ? parts[c] : "";
        }
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
        var (w, h) = CapToContentWidth(
            displayW > 0 ? displayW : scaled.Size.Width,
            displayH > 0 ? displayH : scaled.Size.Height);
        var ib = new ImageBlock { Width = w, Height = h };
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

    // The Windows native "HTML Format" (CF_HTML) clipboard format: a platform (system-named) format, so
    // other apps (Word, browsers) receive the copied selection as rich text, not just plain text. A
    // bytes format is used so we control the exact UTF-8 payload (the CF_HTML byte offsets must match).
    private static readonly DataFormat<byte[]> CfHtmlFormat = DataFormat.CreateBytesPlatformFormat("HTML Format");

    // Puts the selection on the system clipboard as plain text and (on Windows) CF_HTML rich text, in one
    // DataTransfer item. Either may be empty (e.g. an inline-image-only selection has HTML but no text);
    // the item carries whichever formats are present. Mirrors the read side, which understands HTML.
    private static async Task SetClipboardTextAndHtmlAsync(IClipboard clipboard, string text, string? html)
    {
        var item = new DataTransferItem();
        if (!string.IsNullOrEmpty(text)) item.SetText(text);
        if (!string.IsNullOrEmpty(html) && OperatingSystem.IsWindows())
            item.Set(CfHtmlFormat, System.Text.Encoding.UTF8.GetBytes(BuildCfHtml(html!)));
        var dt = new DataTransfer();
        dt.Add(item);
        await clipboard.SetDataAsync(dt);
    }

    // Renders a trimmed selection sub-document (paragraph properties + inline images preserved — see
    // BuildSelectionDocument) to HTML, wrapped so the editor's base font/size is inherited: ToHtml omits
    // the default 10pt and an empty font-family, which would otherwise fall back to the consumer's own
    // defaults (Word → Calibri 11 pt) and look like the font/size was lost. Runs with explicit
    // font/size still emit overriding spans. Null when there is nothing to emit.
    private string? BuildSelectionHtml(FlowDocument? doc)
    {
        if (doc == null || doc.Blocks.Count == 0) return null;
        string inner = HtmlDocumentFormatter.ToHtml(doc);
        if (string.IsNullOrEmpty(inner)) return null;
        // Double-quoted attribute + single-quoted (and thus valid for multi-word/CJK names) font-family,
        // matching ToHtml — a single-quoted style attribute or an unquoted family name is dropped by
        // Word/HWP on paste.
        string family = (DefaultFontFamily.Name ?? "").Replace("'", "").Replace("\"", "");
        // pt, not px (Word/HWP ignore px font-size on paste); the 10pt body default matches ToHtml's
        // skipped default so unstyled runs inherit the right base size.
        return $"<div style=\"font-family:'{family}';font-size:10pt\">{inner}</div>";
    }

    // A trimmed FlowDocument for the current selection that, unlike GetRichRuns (runs only), preserves
    // each paragraph's list/heading/alignment/indent/background AND inline images — so HTML export keeps
    // bullets/numbers, headings, and pasted-back pictures. Table cells / block images flatten to
    // paragraphs (same as the plain-text path). Null when the selection is empty.
    private FlowDocument? BuildSelectionDocument()
    {
        if (_selectionStart.Paragraph == null || _selectionEnd.Paragraph == null) return null;
        int cmp = _selectionStart.CompareTo(_selectionEnd);
        if (cmp == 0) return null;
        var s = cmp < 0 ? _selectionStart : _selectionEnd;
        var e = cmp < 0 ? _selectionEnd : _selectionStart;

        var all = GetAllParagraphsInOrder();
        int si = all.IndexOf(s.Paragraph!), ei = all.IndexOf(e.Paragraph!);
        if (si < 0 || ei < 0 || si > ei) return null;

        var doc = new FlowDocument();
        for (int i = si; i <= ei; i++)
        {
            var p = all[i];
            int from = i == si ? s.Offset : 0;
            int to = i == ei ? e.Offset : GetParagraphLength(p);
            doc.Blocks.Add(CloneParagraphRange(p, from, to));
        }
        return doc;
    }

    // Clones paragraph-level formatting plus the inlines (text runs trimmed to [from,to); inline images
    // whose single position falls inside the range) of one paragraph.
    private static Paragraph CloneParagraphRange(Paragraph p, int from, int to)
    {
        var np = new Paragraph
        {
            ListType = p.ListType, ListMarker = p.ListMarker, ListLevel = p.ListLevel, HeadingLevel = p.HeadingLevel,
            TextAlignment = p.TextAlignment, Indent = p.Indent, MarginRight = p.MarginRight,
            IsQuote = p.IsQuote, Background = p.Background, LineHeight = p.LineHeight, LineSpacing = p.LineSpacing
        };
        int idx = 0;
        foreach (var inl in p.Inlines)
        {
            int len = InlineLen(inl);
            int segStart = idx, segEnd = idx + len;
            idx = segEnd;
            if (len == 0 || segEnd <= from || segStart >= to) continue;
            if (inl is Run r && r.Text != null)
            {
                int a = Math.Max(from, segStart) - segStart;
                int b = Math.Min(to, segEnd) - segStart;
                if (b > a) { var c = (Run)r.Clone(); c.Text = r.Text.Substring(a, b - a); c.Parent = np; np.Inlines.Add(c); }
            }
            else if (inl is InlineImage img && segStart >= from && segEnd <= to)
            {
                var c = (InlineImage)img.Clone(); c.Parent = np; np.Inlines.Add(c);
            }
        }
        if (np.Inlines.Count == 0) np.Inlines.Add(new Run { Text = "" });
        return np;
    }

    // Wraps an HTML fragment in the Windows CF_HTML envelope: a header whose StartHTML/EndHTML/
    // StartFragment/EndFragment are byte offsets (UTF-8) into the payload, plus the fragment markers.
    // The offsets are fixed-width (10 digits), so the header length is the same with placeholder zeros
    // and with the real values — one measuring pass suffices. (internal for unit-test coverage.)
    internal static string BuildCfHtml(string fragmentHtml)
    {
        const string headerFmt =
            "Version:0.9\r\nStartHTML:{0:0000000000}\r\nEndHTML:{1:0000000000}\r\n" +
            "StartFragment:{2:0000000000}\r\nEndFragment:{3:0000000000}\r\n";
        const string pre = "<html><body>\r\n<!--StartFragment-->";
        const string post = "<!--EndFragment-->\r\n</body></html>";
        var enc = System.Text.Encoding.UTF8;
        int headerLen = enc.GetByteCount(string.Format(System.Globalization.CultureInfo.InvariantCulture, headerFmt, 0, 0, 0, 0));
        int startHtml = headerLen;
        int startFragment = startHtml + enc.GetByteCount(pre);
        int endFragment = startFragment + enc.GetByteCount(fragmentHtml);
        int endHtml = endFragment + enc.GetByteCount(post);
        string header = string.Format(System.Globalization.CultureInfo.InvariantCulture, headerFmt,
            startHtml, endHtml, startFragment, endFragment);
        return header + pre + fragmentHtml + post;
    }

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
        => await TryGetTextFormatAsync(clipboard, "html", System.Text.Encoding.UTF8);

    // Word/HWP put "Rich Text Format" (RTF) on the clipboard alongside CF_HTML; unlike CF_HTML, RTF
    // embeds image bytes, so it survives Word's temp-file image references. RTF is ASCII (escapes for
    // the rest), so decode the raw bytes as Latin-1.
    private static async Task<string?> TryGetRtfAsync(IClipboard clipboard)
        => await TryGetTextFormatAsync(clipboard, "rtf", "rich text", System.Text.Encoding.Latin1);

    private static Task<string?> TryGetTextFormatAsync(IClipboard clipboard, string idSubstring, System.Text.Encoding enc)
        => TryGetTextFormatAsync(clipboard, idSubstring, idSubstring, enc);

    private static async Task<string?> TryGetTextFormatAsync(IClipboard clipboard, string idA, string idB, System.Text.Encoding enc)
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
                    if (id.IndexOf(idA, StringComparison.OrdinalIgnoreCase) < 0
                        && id.IndexOf(idB, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    object? raw;
                    try { raw = await item.TryGetRawAsync(fmt); }
                    catch { continue; }
                    if (raw is string s && !string.IsNullOrWhiteSpace(s)) return s;
                    if (raw is byte[] b) return enc.GetString(b);
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
