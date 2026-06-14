using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Text must flow on the same line after an inline image (shared baseline) while the line has
// horizontal room; a too-wide image forces a wrap. Compared via measured editor heights, which
// include constant chrome (margins + bottom padding) that cancels out between variants.
public class InlineImageFlowTests
{
    private static byte[] FakeJpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03, 0x04 };

    private static double MeasureDoc(params Inline[] inlines)
    {
        // These tests assert inline-image line wrapping at a known width, so use the continuous (Free)
        // layout that measures to the given width (the default A4 paper would fix it to 698 instead).
        var ed = new RichEditor { MinHeight = 0, PageSize = RichEditorPageSize.Continuous };
        var doc = new FlowDocument();
        var p = new Paragraph();
        foreach (var i in inlines) p.Inlines.Add(i);
        doc.Blocks.Add(p);
        ed.Document = doc;
        ed.Measure(new Size(700, double.PositiveInfinity));
        return ed.DesiredSize.Height;
    }

    private static InlineImage Img(double w, double h)
    {
        var im = new InlineImage { Width = w, Height = h };
        im.SetImageData(FakeJpeg(), "image/jpeg");
        return im;
    }

    [AvaloniaFact]
    public void TextAfterSmallInlineImage_StaysOnTheSameLine()
    {
        double textOnly = MeasureDoc(new Run { Text = "abcdef" });
        double withImage = MeasureDoc(new Run { Text = "abc" }, Img(20, 20), new Run { Text = "def" });
        // Same single line: the image only raises the line by its ascent over the text's (~9px here);
        // an actual wrap would add a whole extra text line (>= ~24px).
        Assert.True(withImage - textOnly < 16, $"expected one shared line: text={textOnly}, withImage={withImage}");
    }

    [AvaloniaFact]
    public void TextAfterWideInlineImage_WrapsBelow()
    {
        double small = MeasureDoc(new Run { Text = "abc" }, Img(20, 20), new Run { Text = "def" });
        double wide = MeasureDoc(new Run { Text = "abc" }, Img(650, 100), new Run { Text = "def" });
        // 650px image + text can't share a 700px-wide editor's content line -> extra wrapped lines
        // plus the 100px-tall image line.
        Assert.True(wide > small + 90, $"expected wrap below the wide image: small={small}, wide={wide}");
    }

    [AvaloniaFact]
    public void TypingAfterInlineImage_AppendsAfterTheImageInTheModel()
    {
        var ed = new RichEditor { MinHeight = 0 };
        var doc = new FlowDocument();
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "abc" });
        p.Inlines.Add(Img(20, 20));
        doc.Blocks.Add(p);
        ed.Document = doc;

        ed.FocusDocumentEnd(); // caret at the very end: right after the image
        ed.InsertText("x");
        Assert.True(p.Inlines[1] is InlineImage);
        var last = Assert.IsType<Run>(p.Inlines[^1]);
        Assert.EndsWith("x", last.Text);
    }
}
