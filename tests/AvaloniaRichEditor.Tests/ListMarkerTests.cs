using System.Linq;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// item 5: list marker styles. The bullet glyph / number format is a paragraph property (ListMarker)
// rendered by ListMarkerText; the number formats reuse ToAlpha/ToRoman.
public class ListMarkerTests
{
    [Theory]
    [InlineData(ListMarkerStyle.Default, "•")]
    [InlineData(ListMarkerStyle.Disc, "•")]
    [InlineData(ListMarkerStyle.Circle, "◦")]
    [InlineData(ListMarkerStyle.Square, "▪")]
    [InlineData(ListMarkerStyle.Dash, "–")]
    public void BulletGlyphs(ListMarkerStyle style, string expected)
        => Assert.Equal(expected, RichEditor.ListMarkerText(ListKind.Bullet, style, 1));

    [Theory]
    [InlineData(ListMarkerStyle.Default, 3, "3.")]
    [InlineData(ListMarkerStyle.Decimal, 3, "3.")]
    [InlineData(ListMarkerStyle.DecimalParen, 3, "3)")]
    [InlineData(ListMarkerStyle.LowerAlpha, 3, "c)")]
    [InlineData(ListMarkerStyle.UpperAlpha, 3, "C)")]
    [InlineData(ListMarkerStyle.LowerRoman, 4, "iv)")]
    public void NumberFormats(ListMarkerStyle style, int num, string expected)
        => Assert.Equal(expected, RichEditor.ListMarkerText(ListKind.Ordered, style, num));

    [Theory]
    [InlineData(1, "a")]
    [InlineData(26, "z")]
    [InlineData(27, "aa")]
    public void Alpha(int n, string expected) => Assert.Equal(expected, RichEditor.ToAlpha(n, false));

    [Theory]
    [InlineData(1, "i")]
    [InlineData(9, "ix")]
    [InlineData(14, "xiv")]
    public void Roman(int n, string expected) => Assert.Equal(expected, RichEditor.ToRoman(n));

    [Fact]
    public void RoundTrip_PreservesListMarker()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { ListType = ListKind.Ordered, ListMarker = ListMarkerStyle.LowerRoman, Inlines = { new Run { Text = "x" } } });
        doc.Blocks.Add(new Paragraph { ListType = ListKind.Bullet, Inlines = { new Run { Text = "y" } } }); // Default

        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));
        Assert.Equal(ListMarkerStyle.LowerRoman, Assert.IsType<Paragraph>(doc2.Blocks[0]).ListMarker);
        Assert.Equal(ListMarkerStyle.Default, Assert.IsType<Paragraph>(doc2.Blocks[1]).ListMarker);
    }

    [Fact]
    public void Html_RoundTripsBulletAndNumberStyle()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { ListType = ListKind.Bullet, ListMarker = ListMarkerStyle.Square, Inlines = { new Run { Text = "b" } } });
        var ordered = new FlowDocument();
        ordered.Blocks.Add(new Paragraph { ListType = ListKind.Ordered, ListMarker = ListMarkerStyle.LowerAlpha, Inlines = { new Run { Text = "n" } } });

        var bulletBack = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));
        var numberBack = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(ordered));

        Assert.Equal(ListMarkerStyle.Square, bulletBack.Blocks.OfType<Paragraph>().First().ListMarker);
        Assert.Equal(ListMarkerStyle.LowerAlpha, numberBack.Blocks.OfType<Paragraph>().First().ListMarker);
    }
}
