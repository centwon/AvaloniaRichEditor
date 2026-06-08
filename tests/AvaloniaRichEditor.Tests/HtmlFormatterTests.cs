using System.Linq;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

public class HtmlFormatterTests
{
    [Fact]
    public void ParseHtml_SplitsInlineFormatting()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<p>Hello <b>world</b></p>");
        var p = Assert.IsType<Paragraph>(doc.Blocks[0]);

        Assert.Equal("Hello world", p.Text());
        var bold = p.Inlines.OfType<Run>().Single(r => r.Text == "world");
        Assert.Equal(FontWeight.Bold, bold.FontWeight);
    }

    [Fact]
    public void ToHtml_EmitsBoldTag()
    {
        var html = HtmlDocumentFormatter.ToHtml(
            HtmlDocumentFormatter.ParseHtml("<p><b>hi</b></p>"));
        Assert.Contains("<b>", html);
    }

    [Fact]
    public void RoundTrip_PreservesBold()
    {
        var once = HtmlDocumentFormatter.ParseHtml("<p>a <b>b</b></p>");
        var twice = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(once));
        var p = Assert.IsType<Paragraph>(twice.Blocks[0]);
        var bold = p.Inlines.OfType<Run>().Single(r => r.Text == "b");
        Assert.Equal(FontWeight.Bold, bold.FontWeight);
    }

    [Fact]
    public void ParseHtml_BuildsTable()
    {
        var doc = HtmlDocumentFormatter.ParseHtml(
            "<table><tr><td>A</td><td>B</td></tr></table>");
        var tb = doc.Blocks.OfType<TableBlock>().Single();
        Assert.Equal(2, tb.Columns);
        Assert.Equal("A", tb.Cells[0][0].Text());
    }

    [Fact]
    public void RoundTrip_PreservesList()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<ul><li>one</li><li>two</li></ul>");
        Assert.Contains(doc.Blocks.OfType<Paragraph>(), p => p.ListType == ListKind.Bullet);
        Assert.Contains("<ul>", HtmlDocumentFormatter.ToHtml(doc));
    }
}
