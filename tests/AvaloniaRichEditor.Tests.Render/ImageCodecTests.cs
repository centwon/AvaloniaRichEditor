using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

// Second-audit cases that need REAL image codecs: the main test project draws with the no-op backend,
// where Bitmap.Save writes nothing and a decode reports a stub size — so an assertion about encoded
// bytes or about a decoded image's aspect ratio would prove nothing there. Skia is live here.
public class ImageCodecTests
{
    // A 4x2 (2:1) red PNG, so a single declared axis has a ratio to be wrong about.
    private const string Png4x2 =
        "iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAIAAADwyuo0AAAAEklEQVR4nGP4z8AARwjWfwYGAG+qB/lC/d2tAAAAAElFTkSuQmCC";

    private static Bitmap MakeBitmap()
    {
        var rtb = new RenderTargetBitmap(new PixelSize(8, 4));
        return rtb; // a Bitmap with no encoded bytes behind it — exactly the case the writer dropped
    }

    // ImageBlock.Image / InlineImage.Image is public API and its setter CLEARS RawBytes. The RTF writer
    // gated every picture on RawBytes != null, so an image assigned that way vanished from the export
    // without a word — no error, no placeholder.
    [AvaloniaFact]
    public void Write_BitmapOnlyImageBlock_IsWritten()
    {
        var doc = new FlowDocument();
        var ib = new ImageBlock { Image = MakeBitmap(), Width = 8, Height = 4 };
        doc.Blocks.Add(ib);

        string rtf = RtfDocumentFormatter.Write(doc);

        Assert.Null(ib.RawBytes);            // the case under test, not an image that happens to have bytes
        Assert.Contains(@"\pict", rtf, StringComparison.Ordinal);
        Assert.Contains(@"\pngblip", rtf, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Write_BitmapOnlyInlineImage_IsWritten()
    {
        var doc = new FlowDocument();
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "icon " });
        p.Inlines.Add(new InlineImage { Image = MakeBitmap(), Width = 8, Height = 4 });
        doc.Blocks.Add(p);

        string rtf = RtfDocumentFormatter.Write(doc);

        Assert.Contains(@"\pict", rtf, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Write_BitmapOnlyImageInTableCell_IsWritten()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 1);
        tb.Cells[0][0].Blocks.Add(new ImageBlock { Image = MakeBitmap(), Width = 8, Height = 4 });
        doc.Blocks.Add(tb);

        string rtf = RtfDocumentFormatter.Write(doc);

        Assert.Contains(@"\pict", rtf, StringComparison.Ordinal);
    }

    // An image that has neither bytes nor a bitmap is still dropped — the fallback must not invent one.
    [AvaloniaFact]
    public void Write_EmptyImageBlock_WritesNoPicture()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new ImageBlock());

        Assert.DoesNotContain(@"\pict", RtfDocumentFormatter.Write(doc), StringComparison.Ordinal);
    }

    // `<img width="200">` means "scale to 200 wide"; the height follows. Taking the natural height for
    // the undeclared axis stretched a 2:1 picture to 200x2 — the shape foreign HTML most often has.
    [AvaloniaFact]
    public void ParseHtml_WidthOnly_KeepsAspectRatio()
    {
        var doc = HtmlDocumentFormatter.ParseHtml($"<p><img src=\"data:image/png;base64,{Png4x2}\" width=\"200\"></p>");

        var img = doc.Blocks.OfType<ImageBlock>().Single();
        Assert.Equal(200, img.Width);
        Assert.Equal(100, img.Height, 3);
    }

    [AvaloniaFact]
    public void ParseHtml_HeightOnly_KeepsAspectRatio()
    {
        var doc = HtmlDocumentFormatter.ParseHtml($"<p><img src=\"data:image/png;base64,{Png4x2}\" height=\"100\"></p>");

        var img = doc.Blocks.OfType<ImageBlock>().Single();
        Assert.Equal(200, img.Width, 3);
        Assert.Equal(100, img.Height);
    }

    // Both axes declared still win outright — an explicit non-uniform size is the author's choice.
    [AvaloniaFact]
    public void ParseHtml_BothAxes_AreHonouredVerbatim()
    {
        var doc = HtmlDocumentFormatter.ParseHtml(
            $"<p><img src=\"data:image/png;base64,{Png4x2}\" width=\"200\" height=\"300\"></p>");

        var img = doc.Blocks.OfType<ImageBlock>().Single();
        Assert.Equal(200, img.Width);
        Assert.Equal(300, img.Height);
    }

    // Neither declared: the natural size, unchanged.
    [AvaloniaFact]
    public void ParseHtml_NoDeclaredSize_UsesNaturalSize()
    {
        var doc = HtmlDocumentFormatter.ParseHtml($"<p><img src=\"data:image/png;base64,{Png4x2}\"></p>");

        var img = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<InlineImage>()).Single();
        Assert.Equal(4, img.Width, 3);   // small enough to land inline rather than as a block
        Assert.Equal(2, img.Height, 3);
    }
}
