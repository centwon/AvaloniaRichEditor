using System.Linq;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// What a run's own strings can do to the formats that carry them (2026-11-08).
public class LinkAndFontNameEscapeTests
{
    private static FlowDocument OneRun(Run r)
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Inlines = { r } });
        return doc;
    }

    private static Run FirstRun(FlowDocument d) => d.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Run>().First();

    // A quote ended the field's address early: Word reads what follows as switches, and this reader cut the address there.
    [Fact]
    public void RtfLink_WithAQuote_KeepsItsWholeAddress()
    {
        string rtf = RtfDocumentFormatter.Write(OneRun(new Run { Text = "go", NavigateUri = "https://a.example/x\" \\o \"tip" }));
        Assert.Equal("https://a.example/x%22 \\o %22tip", FirstRun(RtfDocumentFormatter.Parse(rtf)).NavigateUri);
    }

    // The HTML writer never lets a script link out, whatever put it in the document; the RTF writer did.
    [Fact]
    public void RtfLink_ToAScript_GoesOutAsPlainText()
    {
        string rtf = RtfDocumentFormatter.Write(OneRun(new Run { Text = "go", NavigateUri = "javascript:alert(1)" }));
        Assert.DoesNotContain("HYPERLINK", rtf);
        Assert.DoesNotContain("javascript", rtf);
        Assert.Contains("go", rtf);
    }

    // The reader ends a family's value at its first ';' and read what followed as declarations of its own; and a colour
    // matched anywhere in the style made a `color:` inside the quoted name the text colour.
    [Theory]
    [InlineData("A;color:red")]
    [InlineData("A color:rgb(255,0,0)")]
    public void HtmlFontName_CannotSetAColour(string family)
    {
        var back = FirstRun(HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(OneRun(new Run { Text = "x", FontFamily = family }))));
        Assert.Null(back.Foreground);
        Assert.StartsWith("A", back.FontFamily);
    }
}
