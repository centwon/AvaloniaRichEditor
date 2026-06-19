using System.Linq;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// RTF export (RtfDocumentFormatter.Write) verified by round-tripping through the existing parser:
// Write → Parse must preserve what the parser understands (text, bold/italic/underline/strike, size,
// colour, tables, images, Unicode). Paragraph-level properties the parser doesn't read (alignment,
// lists, headings, font family) are exported for external apps but intentionally not asserted here.
public class RtfRoundTripTests
{
    private static FlowDocument RoundTrip(FlowDocument doc)
        => RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(doc));

    private static string Text(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));

    [Fact]
    public void Write_ProducesValidRtf()
    {
        var doc = TestHelpers.Doc(TestHelpers.Para(new Run { Text = "hi" }));
        string rtf = RtfDocumentFormatter.Write(doc);
        Assert.StartsWith(@"{\rtf", rtf);
        Assert.True(RtfDocumentFormatter.LooksLikeRtf(rtf));
    }

    // Regression: an astral character (emoji) is written as two \u surrogate halves; the reader must
    // recombine them. ConvertFromUtf32 threw on each lone surrogate and dropped the character before.
    [Fact]
    public void RoundTrip_PreservesAstralUnicode()
    {
        string emoji = "A\U0001F600B"; // grinning face (surrogate pair)
        var doc = TestHelpers.Doc(TestHelpers.Para(new Run { Text = emoji }));
        var p = RoundTrip(doc).Blocks.OfType<Paragraph>().First();
        Assert.Equal(emoji, Text(p));
    }

    [Fact]
    public void RoundTrip_PreservesTextAndCharacterFormatting()
    {
        var doc = TestHelpers.Doc(TestHelpers.Para(
            new Run { Text = "plain " },
            new Run { Text = "bold", FontWeight = FontWeight.Bold },
            new Run { Text = "ital", FontStyle = FontStyle.Italic },
            new Run { Text = "big", FontSize = 24 }));

        var p = RoundTrip(doc).Blocks.OfType<Paragraph>().First();
        var runs = p.Inlines.OfType<Run>().ToList();

        Assert.Equal("plain bolditalbig", Text(p));
        Assert.Contains(runs, r => r.Text == "bold" && r.FontWeight == FontWeight.Bold);
        Assert.Contains(runs, r => r.Text == "ital" && r.FontStyle == FontStyle.Italic);
        Assert.Contains(runs, r => r.Text == "big" && r.FontSize == 24);
    }

    [Fact]
    public void RoundTrip_PreservesForegroundColour()
    {
        var doc = TestHelpers.Doc(TestHelpers.Para(
            new Run { Text = "red", Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00)) }));

        var p = RoundTrip(doc).Blocks.OfType<Paragraph>().First();
        var run = p.Inlines.OfType<Run>().Single(r => r.Text == "red");
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x00), (run.Foreground as ISolidColorBrush)?.Color);
    }

    [Fact]
    public void RoundTrip_PreservesKoreanText()
    {
        var doc = TestHelpers.Doc(TestHelpers.Para(new Run { Text = "한글 텍스트 round-trip" }));
        var p = RoundTrip(doc).Blocks.OfType<Paragraph>().First();
        Assert.Equal("한글 텍스트 round-trip", Text(p));
    }

    [Fact]
    public void RoundTrip_PreservesTableGrid()
    {
        var tb = new TableBlock(2, 2);
        ((Run)tb.Cells[0][0].Para.Inlines[0]).Text = "A1";
        ((Run)tb.Cells[0][1].Para.Inlines[0]).Text = "B1";
        ((Run)tb.Cells[1][0].Para.Inlines[0]).Text = "A2";
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);

        var t = RoundTrip(doc).Blocks.OfType<TableBlock>().Single();
        Assert.Equal(2, t.Rows);
        Assert.Equal(2, t.Columns);
        Assert.Equal("A1", Text(t.Cells[0][0].Para));
        Assert.Equal("B1", Text(t.Cells[0][1].Para));
        Assert.Equal("A2", Text(t.Cells[1][0].Para));
    }

    [Fact]
    public void RoundTrip_PreservesImageBytes()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 }; // PNG magic + filler
        var ib = new ImageBlock { Width = 100, Height = 50 }; // >64 so it round-trips as a block image
        ib.SetImageData(bytes, "image/png");
        var doc = new FlowDocument();
        doc.Blocks.Add(ib);

        var img = RoundTrip(doc).Blocks.OfType<ImageBlock>().Single();
        Assert.Equal(bytes, img.RawBytes);
    }
}
