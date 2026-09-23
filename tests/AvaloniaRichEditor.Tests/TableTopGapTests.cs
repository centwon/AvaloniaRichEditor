using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Reported from the demo (2026-09-21): a table sat flush against the paragraph above it, with none of the
// air the line spacing gives between two lines of text. Both ends were at zero — paragraphs carry no
// bottom margin (HWP-style, round 24) and a table carried no top one.
//
// A table's MarginTop now defaults to NaN, "let the editor choose", which every block walker resolves to
// one line gap of body text. NaN rather than a number because 0 has to keep meaning "no gap at all" —
// three pagination tests set exactly that to get round numbers, and would have silently gained 8 px.
public class TableTopGapTests
{
    private static FlowDocument DocWith(Block table)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(TestHelpers.Para(new Run { Text = "text above" }));
        doc.Blocks.Add(table);
        return doc;
    }

    // Every block kind that is an OBJECT on the page, not text: a picture and a divider butt against the
    // paragraph above exactly as a table does.
    [AvaloniaFact]
    public void ANewObjectBlockAsksTheEditorForItsTopGap()
    {
        Assert.True(double.IsNaN(new TableBlock().MarginTop));
        Assert.True(double.IsNaN(new TableBlock(3, 2).MarginTop));
        Assert.True(double.IsNaN(new ImageBlock().MarginTop));
        Assert.True(double.IsNaN(new DividerBlock().MarginTop));
        Assert.Equal(0, new Paragraph().MarginTop); // text is not an object: it keeps its plain 0
    }

    [AvaloniaTheory]
    [InlineData("image")]
    [InlineData("divider")]
    public void APictureAndADividerGetTheSameGap(string kind)
    {
        Block block = kind == "image" ? new ImageBlock { Width = 60, Height = 40 } : new DividerBlock();
        var ed = new RichEditor { Document = DocWith(block) };

        Assert.Equal(ed.AutoBlockTopGap, ed.TopGapOf(block), 3);
    }

    // One line gap = the body line box (font size x line spacing) less the text itself.
    [AvaloniaFact]
    public void TheGapIsTheLineSpacingOfBodyText()
    {
        var ed = new RichEditor { Document = DocWith(new TableBlock(2, 2)) };
        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();

        double expected = ed.DefaultFontSize * 4 / 3 * (RichEditor.DefaultLineSpacing - 1);
        Assert.Equal(expected, ed.TopGapOf(tb), 3);
        Assert.True(expected > 4, $"the gap should be visible, got {expected}");
    }

    // An explicit margin — including zero — is used as given, or a document could not ask for a table
    // tight against the text.
    [AvaloniaTheory]
    [InlineData(0.0)]
    [InlineData(24.0)]
    public void AnExplicitMarginIsUsedAsGiven(double margin)
    {
        var table = new TableBlock(2, 2) { MarginTop = margin };
        var ed = new RichEditor { Document = DocWith(table) };

        Assert.Equal(margin, ed.TopGapOf(table), 3);
    }

    // The gap is real layout, not just a number: the document is taller by it, which is what pushes the
    // table clear of the paragraph.
    [AvaloniaFact]
    public void TheGapMakesTheDocumentTallerThanATightTable()
    {
        var auto = new RichEditor { Document = DocWith(new TableBlock(2, 2)) };
        var tight = new RichEditor { Document = DocWith(new TableBlock(2, 2) { MarginTop = 0 }) };

        auto.Measure(new Size(600, double.PositiveInfinity));
        tight.Measure(new Size(600, double.PositiveInfinity));

        double grew = auto.DesiredSize.Height - tight.DesiredSize.Height;
        Assert.Equal(auto.TopGapOf(auto.Document!.Blocks.OfType<TableBlock>().Single()), grew, 3);
        // Spelt out because the comparison above also holds when both are zero — which is what an "auto
        // resolves to nothing" defect looks like, and it passed that way until this line was added.
        Assert.True(grew > 4, $"the auto gap should add real height, added {grew}");
    }

    // JSON has no NaN: "auto" goes out as no field at all and has to come back as auto, or every save
    // would quietly pin the gap to whatever number was written instead.
    [AvaloniaFact]
    public void AutoSurvivesAJsonRoundTrip_AndAnExplicitZeroStaysZero()
    {
        var doc = DocWith(new TableBlock(2, 2));
        string json = DocumentSerializer.Serialize(doc);
        Assert.True(double.IsNaN(DocumentSerializer.Deserialize(json).Blocks.OfType<TableBlock>().Single().MarginTop));

        var pinned = DocWith(new TableBlock(2, 2) { MarginTop = 0 });
        var back = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(pinned));
        Assert.Equal(0, back.Blocks.OfType<TableBlock>().Single().MarginTop);
    }
}
