using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Image commands (context menu / public insert): resize presets, replace/save, and the HWP-style
// block<->inline conversion. Part of RichEditor (split out of the main file for readability).
public partial class RichEditor
{
    // Caps an inserted image's display width to the editor's content width (keeping aspect ratio),
    // so an image larger than the document doesn't overflow. Display size only — the bytes are intact,
    // and the user can still resize it larger by hand.
    private (double w, double h) CapToContentWidth(double w, double h)
    {
        double max = Bounds.Width > 60 ? ContentLayoutWidth - 20 : 0;
        if (max > 0 && w > max) { h = w > 0 ? h * (max / w) : h; w = max; }
        return (w, h);
    }

    // ---- drawing pictures at the size they are drawn -------------------------------------------------
    // The renderer used to draw the model's Image, whose getter decodes at SOURCE size and keeps the bitmap on
    // the element (and, through Clone, on undo snapshots). Measured on the WinUI port (2026-09-16): six
    // 4000x3000 photos shown 240 px wide held ~280 MB; decoded to the drawn size, the cost followed the display.
    // So nothing the editor does on its own touches Image any more when bytes exist — drawing goes through
    // ImageDisplayCache, sizes come from the header, and save/copy decode transiently. A host reading Image
    // still gets the full bitmap, as documented.

    private readonly ImageDisplayCache _displayImages = new();

    // Device pixels per DIP for the pictures drawn in the current pass. Render sets it from the screen;
    // printing raises it to print resolution for the duration.
    private double _imagePixelScale = 1;

    // Pictures print at up to this resolution (never above the source's own).
    internal const double PrintImageDpi = 300;

    private double ScreenPixelScale()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return 1;
        double s = top.RenderScaling;
        // Zoom is a LayoutTransform the HOST puts around the editor (RichEditorView does); whatever transforms
        // sit between here and the window multiply what a DIP covers on screen.
        if (this.TransformToVisual(top) is { } m)
            s *= Math.Max(Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12), Math.Sqrt(m.M21 * m.M21 + m.M22 * m.M22));
        return s > 0 && double.IsFinite(s) ? s : 1;
    }

    // Every picture draw. The display bitmap COVERS its rect with the source's aspect (ImageDisplayCache), so a
    // picture squashed out of its proportions still shrinks its short axis several times when drawn — and
    // sampling that skips source rows turns fine lines into noise (measured: 1px stripes shrunk ~17:1 came out
    // with rows anywhere from green 0 to 239). HighQuality filters the downscale. From the WinUI port
    // (2026-09-19), where the same defect drew "SHARP" as "SHAKI'".
    private static void DrawPicture(Avalonia.Media.DrawingContext context, Avalonia.Media.Imaging.Bitmap bmp, Rect rect)
    {
        using (context.PushRenderOptions(new Avalonia.Media.RenderOptions { BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality }))
            context.DrawImage(bmp, rect);
    }

    internal Avalonia.Media.Imaging.Bitmap? PictureToDraw(ImageBlock img, double w, double h)
        => PictureToDraw(img.RawBytes, img.CachedBitmap, () => img.Image, w, h);

    internal Avalonia.Media.Imaging.Bitmap? PictureToDraw(InlineImage img, double w, double h)
        => PictureToDraw(img.RawBytes, img.CachedBitmap, () => img.Image, w, h);

    private Avalonia.Media.Imaging.Bitmap? PictureToDraw(byte[]? rawBytes, Avalonia.Media.Imaging.Bitmap? cached,
        Func<Avalonia.Media.Imaging.Bitmap?> image, double w, double h)
    {
        if (rawBytes == null) return image(); // a bitmap set directly: there is nothing to decode
        return _displayImages.Get(rawBytes, cached,
            (int)Math.Ceiling(Math.Max(1, w) * _imagePixelScale), (int)Math.Ceiling(Math.Max(1, h) * _imagePixelScale));
    }

    // The picture's natural size in DIPs: from the header when the format is one ImageInfo reads, else from a
    // decode — the element's own bitmap if it has one, or a transient decode that isn't kept.
    private static Size? NaturalSize(byte[]? rawBytes, Avalonia.Media.Imaging.Bitmap? cached, Func<Avalonia.Media.Imaging.Bitmap?> image)
    {
        if (rawBytes != null)
        {
            var (w, h) = ImageInfo.GetPixelSize(rawBytes);
            if (w > 0 && h > 0) return new Size(w, h);
        }
        if (cached != null) return cached.Size;
        if (rawBytes == null) return image()?.Size;
        try
        {
            using var ms = new System.IO.MemoryStream(rawBytes);
            using var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
            return bmp.Size;
        }
        catch (Exception ex) { RichEditorDiagnostics.Report(ex); return null; }
    }

    private static Size? NaturalSize(ImageBlock img) => NaturalSize(img.RawBytes, img.CachedBitmap, () => img.Image);
    private static Size? NaturalSize(InlineImage img) => NaturalSize(img.RawBytes, img.CachedBitmap, () => img.Image);

    // Whether the element has a picture at all — what the image menu items used to ask by decoding it.
    private static bool HasPicture(ImageBlock img) => img.RawBytes != null || img.Image != null;
    private static bool HasPicture(InlineImage img) => img.RawBytes != null || img.Image != null;

    private void ResetImageSize(ImageBlock img)
    {
        if (Document == null || NaturalSize(img) is not { } natural) return;
        if (img.Width == natural.Width && img.Height == natural.Height) return; // already: no undo step that undoes nothing
        PushUndo();
        img.Width = natural.Width;
        img.Height = natural.Height;
        // A size preset changes the block's height, so the document's total height changed. The presets
        // reach neither ResetCaretBlink nor any other re-measuring path (the resize DRAG does — see
        // OnPointerReleased), so the ScrollViewer kept the old extent: a picture reset to natural size
        // could grow past the bottom of the scrollable range.
        InvalidateMeasure();
        InvalidateVisual();
    }

    // Scales the display size by a factor relative to the CURRENT size (so presets compound),
    // falling back to natural size when no explicit size is set. Display size only — bytes untouched.
    private void ScaleImageSize(ImageBlock img, double factor)
    {
        if (Document == null || !HasPicture(img)) return;
        Size natural = img.Width > 0 && img.Height > 0 ? default : NaturalSize(img) ?? default;
        double baseW = img.Width > 0 ? img.Width : natural.Width;
        double baseH = img.Height > 0 ? img.Height : natural.Height;
        if (baseW <= 0 || baseH <= 0) return; // no size set and none readable: nothing to scale from
        PushUndo();
        img.Width = Math.Max(1, baseW * factor);
        img.Height = Math.Max(1, baseH * factor);
        InvalidateMeasure(); // see ResetImageSize
        InvalidateVisual();
    }

    // Edits a picture's accessibility description (HTML `alt`). Backported from the WinUI peer, where
    // it was the only half of image accessibility that existed — the description round-trips through
    // JSON/.flow and HTML, so a document that carries it keeps carrying it.
    //
    // Exactly one of the two is non-null: the caller is a menu built for a block image or for an inline
    // one. AltText affects neither layout nor rendering, so nothing is invalidated here.
    private async Task EditImageAltTextAsync(ImageBlock? block, InlineImage? inline)
    {
        if (IsReadOnly || (block == null && inline == null)) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        string current = block?.AltText ?? inline?.AltText ?? "";
        // Null means "cancelled", and an empty string means "decorative" — the model spells that null
        // too, which is why the two are distinguished here rather than in the dialog.
        string? entered = await InputDialog.ShowAsync(owner, Loc("AltText").TrimEnd('…', '.'), current);
        if (entered == null) return;

        string? alt = string.IsNullOrWhiteSpace(entered) ? null : entered.Trim();
        PushUndo();
        if (block != null) block.AltText = alt;
        else inline!.AltText = alt;
    }

    private async Task ReplaceImageAsync(ImageBlock img)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = Loc("SelectImage"),
            AllowMultiple = false,
            FileTypeFilter = new[] { Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll }
        });
        if (files.Count == 0) return;
        try
        {
            await using var s = await files[0].OpenReadAsync();
            using var ms = new System.IO.MemoryStream();
            await s.CopyToAsync(ms);
            var bytes = ms.ToArray();
            using var ms2 = new System.IO.MemoryStream(bytes);
            using (new Avalonia.Media.Imaging.Bitmap(ms2)) { } // validate before committing — not kept (drawn from ImageDisplayCache)
            if (Document != null) PushUndo();
            img.SetImageData(bytes, ImageMime.Detect(bytes));
            InvalidateVisual();
        }
        catch (Exception ex) { RichEditorDiagnostics.Report(ex); }
    }

    private Task SaveImageAsync(ImageBlock img) => SaveBitmapAsync(img.RawBytes, img.RawBytes == null ? img.Image : null);

    // Saves as PNG. With bytes, the full-size bitmap is decoded for the save and dropped after it — reading the
    // model's Image instead would decode it at source size and keep it on the element.
    private async Task SaveBitmapAsync(byte[]? rawBytes, Avalonia.Media.Imaging.Bitmap? bmp)
    {
        if (rawBytes == null && bmp == null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = Loc("SaveImage"),
            DefaultExtension = "png",
            SuggestedFileName = "image.png"
        });
        if (file == null) return;
        try
        {
            await using var s = await file.OpenWriteAsync();
            if (bmp != null) { bmp.Save(s, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); return; }
            using var ms = new System.IO.MemoryStream(rawBytes!);
            using var decoded = new Avalonia.Media.Imaging.Bitmap(ms);
            decoded.Save(s, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        catch (Exception ex) { RichEditorDiagnostics.Report(ex); }
    }

    // HWP-style "글자처럼 취급" (treat as character): demote a block image into an InlineImage,
    // anchored at the end of the preceding paragraph (NormalizeBlocks guarantees paragraphs around
    // every block) so it flows with the text as one character.
    internal void ConvertImageBlockToInline(ImageBlock ib)
    {
        if (Document == null) return;
        int idx = Document.Blocks.IndexOf(ib);
        if (idx < 0) return;
        Paragraph? anchor = null;
        bool atEnd = true;
        if (idx > 0 && Document.Blocks[idx - 1] is Paragraph prev) anchor = prev;
        else
            for (int i = idx + 1; i < Document.Blocks.Count && anchor == null; i++)
                if (Document.Blocks[i] is Paragraph next) { anchor = next; atEnd = false; }
        if (anchor == null) return;

        PushUndo();
        var im = new InlineImage
        {
            Width = double.IsNaN(ib.Width) ? (NaturalSize(ib)?.Width ?? 16) : ib.Width,
            Height = double.IsNaN(ib.Height) ? (NaturalSize(ib)?.Height ?? 16) : ib.Height
        };
        if (ib.RawBytes != null) im.SetImageData(ib.RawBytes, ib.MimeType, ib.CachedBitmap);
        else im.Image = ib.Image;
        Document.Blocks.Remove(ib);
        if (atEnd) anchor.Inlines.Add(im);
        else anchor.Inlines.Insert(0, im);
        if (_selectedBlock == ib) _selectedBlock = null;
        _selectedInline = (anchor, im); // show the inline selection chrome right away
        UpdateParents(Document);

        // Caret right after the image character so typing continues next to it.
        int off = 0;
        foreach (var inl in anchor.Inlines) { off += InlineLen(inl); if (ReferenceEquals(inl, im)) break; }
        _caretPosition = new TextPointer(anchor, off);
        CollapseSelectionToCaret();
        ResetCaretBlink();
        InvalidateVisual();
    }

    // Reverse of ConvertImageBlockToInline: promote an inline image to a sibling ImageBlock after
    // its paragraph. Top-level paragraphs only — table cells cannot host block siblings, so the
    // context menu disables this inside cells.
    internal void ConvertInlineImageToBlock(Paragraph p, InlineImage im)
    {
        if (Document == null) return;
        int idx = Document.Blocks.IndexOf(p);
        if (idx < 0) return;

        PushUndo();
        var ib = new ImageBlock { Width = im.Width, Height = im.Height };
        if (im.RawBytes != null) ib.SetImageData(im.RawBytes, im.MimeType, im.CachedBitmap);
        else ib.Image = im.Image;
        RemoveInlineImageCharacter(p, im);
        Document.Blocks.Insert(idx + 1, ib);
        UpdateParents(Document);
        _selectedInline = null;
        _selectedBlock = ib;
        ResetCaretBlink();
        InvalidateVisual();
    }

    private void DeleteInlineImage(Paragraph p, InlineImage img)
    {
        if (Document == null) return;
        PushUndo();
        RemoveInlineImageCharacter(p, img);
        if (_selectedInline is { } s && ReferenceEquals(s.img, img)) _selectedInline = null;
        InvalidateMeasure(); // the line box loses the picture's height
        InvalidateVisual();
    }

    // Takes a picture's one character out of its paragraph, pulling back the caret and selection ends that were
    // past it. They kept their offsets, so the caret after a trailing picture sat one past the paragraph's end and
    // the next keys went nowhere.
    private void RemoveInlineImageCharacter(Paragraph p, InlineImage img)
    {
        int off = OffsetOfInline(p, img);
        p.Inlines.Remove(img);
        TextPointer Back(TextPointer t) => ReferenceEquals(t.Paragraph, p) && t.Offset > off ? new TextPointer(p, t.Offset - 1) : t;
        _caretPosition = Back(_caretPosition);
        _selectionStart = Back(_selectionStart);
        _selectionEnd = Back(_selectionEnd);
    }

    private void ResetInlineImageSize(InlineImage img)
    {
        if (Document == null || NaturalSize(img) is not { } natural) return;
        if (img.Width == natural.Width && img.Height == natural.Height) return; // see ResetImageSize
        PushUndo();
        img.Width = natural.Width;
        img.Height = natural.Height;
        InvalidateMeasure(); // see ResetImageSize — a taller inline image grows its line box
        InvalidateVisual();
    }

    // Scales the display size by a factor relative to the CURRENT size (so presets compound),
    // mirroring the block-image presets. Display size only — the encoded bytes are untouched.
    private void ScaleInlineImageSize(InlineImage img, double factor)
    {
        if (Document == null || !HasPicture(img)) return;
        Size natural = img.Width > 0 && img.Height > 0 ? default : NaturalSize(img) ?? default;
        double baseW = img.Width > 0 ? img.Width : natural.Width;
        double baseH = img.Height > 0 ? img.Height : natural.Height;
        if (baseW <= 0 || baseH <= 0) return; // no size set and none readable: nothing to scale from
        PushUndo();
        img.Width = Math.Max(1, baseW * factor);
        img.Height = Math.Max(1, baseH * factor);
        InvalidateMeasure(); // see ResetImageSize
        InvalidateVisual();
    }

    private async Task ReplaceInlineImageAsync(InlineImage img)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = Loc("SelectImage"),
            AllowMultiple = false,
            FileTypeFilter = new[] { Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll }
        });
        if (files.Count == 0) return;
        try
        {
            await using var s = await files[0].OpenReadAsync();
            using var ms = new System.IO.MemoryStream();
            await s.CopyToAsync(ms);
            var bytes = ms.ToArray();
            using var ms2 = new System.IO.MemoryStream(bytes);
            using (new Avalonia.Media.Imaging.Bitmap(ms2)) { } // validate before committing — not kept (drawn from ImageDisplayCache)
            if (Document != null) PushUndo();
            img.SetImageData(bytes, ImageMime.Detect(bytes));
            InvalidateVisual();
        }
        catch (Exception ex) { RichEditorDiagnostics.Report(ex); }
    }

    /// <summary>Opens a file picker and inserts the chosen image at the caret.</summary>
    public async Task InsertImageFromFileAsync()
    {
        if (IsReadOnly || !AllowImages) return;
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = Loc("SelectImage"),
            AllowMultiple = false,
            FileTypeFilter = new[] { Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll }
        });
        if (files.Count == 0) return;
        try
        {
            await using var s = await files[0].OpenReadAsync();
            using var ms = new System.IO.MemoryStream();
            await s.CopyToAsync(ms);
            InsertImageBytes(ms.ToArray()); // keep the file's original encoding (no PNG re-encode on save)
        }
        catch (Exception ex) { RichEditorDiagnostics.Report(ex); }
    }
}
