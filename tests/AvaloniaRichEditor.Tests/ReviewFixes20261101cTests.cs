using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Third review (2026-11-01, both repos): HTML picture sizes from other properties, relative font sizes, other -color
// properties taken as the text colour.
public class ReviewFixes20261101cTests
{
    private const string Png1x1 = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private static Run RunWith(FlowDocument doc, string text)
        => doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).First(r => r.Text == text);

    // ReadPx matched any property ENDING in the name: border-width / line-height / max-width sized the picture.
    [AvaloniaTheory]
    [InlineData("border-width:30px")]
    [InlineData("line-height:30px")]
    [InlineData("max-width:30px")]
    public void AnotherPropertyEndingInWidthOrHeight_DoesNotSizeAPicture(string style)
    {
        var doc = HtmlDocumentFormatter.ParseHtml($"<p>x<img src=\"{Png1x1}\" style=\"{style}\"></p>");
        var img = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<InlineImage>()).Single();
        Assert.Equal(1, img.Width, 3);
        Assert.Equal(1, img.Height, 3);
    }

    // em/rem/% were read as px: 1.2em came out at 0.9pt.
    [AvaloniaTheory]
    [InlineData("font-size:1.2em", 12)]
    [InlineData("font-size:150%", 15)]
    [InlineData("font-size:2rem", 20)]
    [InlineData("font-size:12pt", 12)]
    [InlineData("font-size:16px", 12)]
    public void RelativeFontSizes_AreRelative(string style, double pt)
    {
        var doc = HtmlDocumentFormatter.ParseHtml($"<p><span style=\"{style}\">x</span></p>");
        Assert.Equal(pt, RunWith(doc, "x").FontSize, 3);
    }

    // (?<!background-)color matched text-decoration-color / border-color too, and the first match won.
    [AvaloniaFact]
    public void AnotherColorProperty_IsNotTheTextColour()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<p><span style=\"text-decoration-color:#ff0000; color:#0000ff\">x</span></p>");
        Assert.Equal(Color.FromRgb(0, 0, 255), Assert.IsAssignableFrom<ISolidColorBrush>(RunWith(doc, "x").Foreground).Color);
    }
}
