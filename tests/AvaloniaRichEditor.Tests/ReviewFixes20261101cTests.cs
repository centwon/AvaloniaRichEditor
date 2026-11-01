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

    // An inline table on a list item's line came back flattened to its cells' text: the list item reader was
    // the inline one, which knows no tables (the port's fuzz, seed 1488).
    [AvaloniaFact]
    public void AnInlineTableInAListItem_SurvivesHtml()
    {
        var table = new TableBlock(1, 2);
        table.Cells[0][1].Blocks.Clear();
        table.Cells[0][1].Blocks.Add(new Paragraph { Inlines = { new Run { Text = "cell", NavigateUri = "https://example.com/2" } } });
        var item = new Paragraph { ListType = ListKind.Bullet, Inlines = { new InlineTable { Table = table }, new Run { Text = "after" } } };
        var doc = new FlowDocument();
        doc.Blocks.Add(item);

        var back = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));
        var p = Assert.Single(back.Blocks.OfType<Paragraph>());
        Assert.Equal(ListKind.Bullet, p.ListType);
        var it = Assert.Single(p.Inlines.OfType<InlineTable>());
        Assert.Equal(2, it.Table.Columns);
        var link = it.Table.Cells[0][1].Blocks.OfType<Paragraph>().SelectMany(q => q.Inlines.OfType<Run>()).Single(r => r.Text == "cell");
        Assert.Equal("https://example.com/2", link.NavigateUri);
        Assert.Contains(p.Inlines.OfType<Run>(), r => r.Text == "after");
    }

    // A list item's paragraph formatting (fill, indent, alignment) went out and came back plain; the port read it since 1.1.
    [AvaloniaFact]
    public void AListItemsParagraphFormat_SurvivesHtml()
    {
        var item = new Paragraph
        {
            ListType = ListKind.Ordered, TextAlignment = TextAlignment.Center, Indent = 40,
            Background = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xDD, 0xD2, 0x20)),
            Inlines = { new Run { Text = "item" } },
        };
        var doc = new FlowDocument();
        doc.Blocks.Add(item);

        var p = Assert.Single(HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc)).Blocks.OfType<Paragraph>());
        Assert.Equal(TextAlignment.Center, p.TextAlignment);
        Assert.Equal(40, p.Indent, 3);
        Assert.Equal(Color.FromRgb(0xDD, 0xD2, 0x20), Assert.IsAssignableFrom<ISolidColorBrush>(p.Background).Color);
    }
}
