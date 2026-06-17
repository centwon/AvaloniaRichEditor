using System;
using System.Threading.Tasks;
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

    private void ResetImageSize(ImageBlock img)
    {
        if (Document == null || img.Image == null) return;
        PushUndo();
        img.Width = img.Image.Size.Width;
        img.Height = img.Image.Size.Height;
        InvalidateVisual();
    }

    // Scales the display size by a factor relative to the CURRENT size (so presets compound),
    // falling back to natural size when no explicit size is set. Display size only — bytes untouched.
    private void ScaleImageSize(ImageBlock img, double factor)
    {
        if (Document == null || img.Image == null) return;
        PushUndo();
        double baseW = img.Width > 0 ? img.Width : img.Image.Size.Width;
        double baseH = img.Height > 0 ? img.Height : img.Image.Size.Height;
        img.Width = Math.Max(1, baseW * factor);
        img.Height = Math.Max(1, baseH * factor);
        InvalidateVisual();
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
            var bmp = new Avalonia.Media.Imaging.Bitmap(ms2); // validate before committing
            if (Document != null) PushUndo();
            img.SetImageData(bytes, ImageMime.Detect(bytes), bmp);
            InvalidateVisual();
        }
        catch { }
    }

    private Task SaveImageAsync(ImageBlock img) => SaveBitmapAsync(img.Image);

    private async Task SaveBitmapAsync(Avalonia.Media.Imaging.Bitmap? bmp)
    {
        if (bmp == null) return;
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
            bmp.Save(s);
        }
        catch { }
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
            Width = double.IsNaN(ib.Width) ? (ib.Image?.Size.Width ?? 16) : ib.Width,
            Height = double.IsNaN(ib.Height) ? (ib.Image?.Size.Height ?? 16) : ib.Height
        };
        if (ib.RawBytes != null) im.SetImageData(ib.RawBytes, ib.MimeType, ib.Image);
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
        if (im.RawBytes != null) ib.SetImageData(im.RawBytes, im.MimeType, im.Image);
        else ib.Image = im.Image;
        p.Inlines.Remove(im);
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
        p.Inlines.Remove(img);
        if (_selectedInline is { } s && ReferenceEquals(s.img, img)) _selectedInline = null;
        InvalidateVisual();
    }

    private void ResetInlineImageSize(InlineImage img)
    {
        if (Document == null || img.Image == null) return;
        PushUndo();
        img.Width = img.Image.Size.Width;
        img.Height = img.Image.Size.Height;
        InvalidateVisual();
    }

    // Scales the display size by a factor relative to the CURRENT size (so presets compound),
    // mirroring the block-image presets. Display size only — the encoded bytes are untouched.
    private void ScaleInlineImageSize(InlineImage img, double factor)
    {
        if (Document == null || img.Image == null) return;
        PushUndo();
        double baseW = img.Width > 0 ? img.Width : img.Image.Size.Width;
        double baseH = img.Height > 0 ? img.Height : img.Image.Size.Height;
        img.Width = Math.Max(1, baseW * factor);
        img.Height = Math.Max(1, baseH * factor);
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
            var bmp = new Avalonia.Media.Imaging.Bitmap(ms2); // validate before committing
            if (Document != null) PushUndo();
            img.SetImageData(bytes, ImageMime.Detect(bytes), bmp);
            InvalidateVisual();
        }
        catch { }
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
        catch { }
    }
}
