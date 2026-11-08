using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Bold in RTF and HTML is one bit. Written only for exactly 700, an ExtraBold or Black run went out not bold at all and lost
// its weight entirely; 600 and up is bold, as on the WinUI control (2026-11-09).
public class BoldFromSemiboldTests
{
    private static FlowDocument One(FontWeight w)
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "x", FontWeight = w } } });
        return doc;
    }

    // \b on its own — not \bin, \brdrb, \box…
    private static bool RtfBold(string rtf) => Regex.IsMatch(rtf, @"\\b(?![a-z])");

    [Theory]
    [InlineData(600)]
    [InlineData(800)]
    [InlineData(900)]
    public void ARunIsWrittenBold_FromSemiboldUp(int weight)
    {
        var doc = One((FontWeight)weight);
        Assert.True(RtfBold(RtfDocumentFormatter.Write(doc)));
        Assert.Contains("<b>", HtmlDocumentFormatter.ToHtml(doc));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    public void ARunBelowSemibold_IsNotBold(int weight)
    {
        var doc = One((FontWeight)weight);
        Assert.False(RtfBold(RtfDocumentFormatter.Write(doc)));
        Assert.DoesNotContain("<b>", HtmlDocumentFormatter.ToHtml(doc));
    }
}
