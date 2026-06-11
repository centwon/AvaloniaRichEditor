using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// HWP-style "글자처럼 취급" toggle: a block image can be demoted to an inline (1-character) image
// anchored in the neighbouring paragraph, and an inline image promoted back to a sibling block.
// Bytes/mime/size must survive both directions.
public class ImageInlineToggleTests
{
    private static byte[] FakeJpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03, 0x04 };

    private static (RichEditor ed, ImageBlock ib) EditorWithBlockImage()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "before" });
        doc.Blocks.Add(p);
        var ib = new ImageBlock { Width = 100, Height = 50 };
        ib.SetImageData(FakeJpeg(), "image/jpeg");
        doc.Blocks.Add(ib);
        ed.Document = doc;
        return (ed, ib);
    }

    [AvaloniaFact]
    public void BlockToInline_AnchorsAtEndOfPreviousParagraph()
    {
        var (ed, ib) = EditorWithBlockImage();
        ed.ConvertImageBlockToInline(ib);

        Assert.DoesNotContain(ed.Document!.Blocks, b => b is ImageBlock);
        var p = Assert.IsType<Paragraph>(ed.Document.Blocks[0]);
        var im = Assert.Single(p.Inlines.OfType<InlineImage>());
        Assert.Same(p.Inlines[^1], im); // appended after "before"
        Assert.Equal(FakeJpeg(), im.RawBytes);
        Assert.Equal("image/jpeg", im.MimeType);
        Assert.Equal(100, im.Width);
        Assert.Equal(50, im.Height);
    }

    [AvaloniaFact]
    public void InlineToBlock_InsertsSiblingAfterParagraph()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "text" });
        var im = new InlineImage { Width = 40, Height = 40 };
        im.SetImageData(FakeJpeg(), "image/jpeg");
        p.Inlines.Add(im);
        doc.Blocks.Add(p);
        ed.Document = doc;

        ed.ConvertInlineImageToBlock(p, im);

        Assert.Empty(p.Inlines.OfType<InlineImage>());
        int pIdx = ed.Document!.Blocks.IndexOf(p);
        var ib = Assert.IsType<ImageBlock>(ed.Document.Blocks[pIdx + 1]);
        Assert.Equal(FakeJpeg(), ib.RawBytes);
        Assert.Equal("image/jpeg", ib.MimeType);
        Assert.Equal(40, ib.Width);
    }

    [AvaloniaFact]
    public void Toggle_RoundTrips_BytesAndSize()
    {
        var (ed, ib) = EditorWithBlockImage();
        ed.ConvertImageBlockToInline(ib);
        var p = Assert.IsType<Paragraph>(ed.Document!.Blocks[0]);
        var im = Assert.Single(p.Inlines.OfType<InlineImage>());

        ed.ConvertInlineImageToBlock(p, im);
        var ib2 = Assert.Single(ed.Document.Blocks.OfType<ImageBlock>());
        Assert.Equal(FakeJpeg(), ib2.RawBytes);
        Assert.Equal(100, ib2.Width);
        Assert.Equal(50, ib2.Height);
    }

    [AvaloniaFact]
    public void BlockToInline_IsUndoable()
    {
        var (ed, ib) = EditorWithBlockImage();
        ed.FocusDocumentEnd(); // undo snapshots require a placed caret
        ed.ConvertImageBlockToInline(ib);
        Assert.DoesNotContain(ed.Document!.Blocks, b => b is ImageBlock);

        ed.Undo();
        Assert.Single(ed.Document!.Blocks.OfType<ImageBlock>());
        var p = Assert.IsType<Paragraph>(ed.Document.Blocks[0]);
        Assert.Empty(p.Inlines.OfType<InlineImage>());
    }
}
