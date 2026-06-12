using System.Linq;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Regression coverage for the two riskiest, previously-untested TextRange paths (roadmap N4/N6-2
// safety net): the "inline image = one logical character" offset model, and multi-paragraph
// selection (text/delete/style) including selections that span a non-paragraph block.
public class TextRangeOffsetTests
{
    // Layout: "AB" [img] "CD"  → logical offsets A=0 B=1 IMG=2 C=3 D=4, length 5.
    private static Paragraph ImageParagraph() =>
        TestHelpers.Para(new Run { Text = "AB" }, TestHelpers.Img(), new Run { Text = "CD" });

    // ---- offset model: inline image counts as one position ----

    [Fact]
    public void GetText_AcrossInlineImage_DropsPlaceholder_ButOffsetsCountIt()
    {
        var p = ImageParagraph();
        Assert.Equal("ABCD", new TextRange(new TextPointer(p, 0), new TextPointer(p, 5)).GetText());
        // 1..4 spans B, the image, and C; the image is dropped from the text but still consumed an offset.
        Assert.Equal("BC", new TextRange(new TextPointer(p, 1), new TextPointer(p, 4)).GetText());
    }

    [Fact]
    public void Delete_RangeCoveringInlineImage_RemovesImage()
    {
        var p = ImageParagraph();
        new TextRange(new TextPointer(p, 1), new TextPointer(p, 4)).Delete(); // B, img, C
        Assert.Equal("AD", p.Text());
        Assert.DoesNotContain(p.Inlines, i => i is InlineImage);
    }

    [Fact]
    public void Delete_RangeBeforeInlineImage_KeepsImage()
    {
        var p = ImageParagraph();
        new TextRange(new TextPointer(p, 0), new TextPointer(p, 1)).Delete(); // just "A"
        Assert.Equal("BCD", p.Text());
        Assert.Contains(p.Inlines, i => i is InlineImage);
    }

    [Fact]
    public void ApplyPropertyValue_AcrossInlineImage_StylesRunsOnBothSides()
    {
        var p = ImageParagraph();
        new TextRange(new TextPointer(p, 0), new TextPointer(p, 5))
            .ApplyPropertyValue(r => r.FontWeight = FontWeight.Bold);
        Assert.All(p.Inlines.OfType<Run>(), r => Assert.Equal(FontWeight.Bold, r.FontWeight));
    }

    [Fact]
    public void GetRichRuns_SkipsInlineImage_KeepsSurroundingText()
    {
        var p = ImageParagraph();
        var runs = new TextRange(new TextPointer(p, 0), new TextPointer(p, 5)).GetRichRuns();
        Assert.Equal("ABCD", string.Concat(runs.Select(r => r.Text)));
    }

    // ---- partial formatting splits runs at the selection boundary ----

    [Fact]
    public void ApplyPropertyValue_SubRange_SplitsRun_StylesOnlySelectedPart()
    {
        var p = TestHelpers.Para(new Run { Text = "abcdef" });
        new TextRange(new TextPointer(p, 2), new TextPointer(p, 4))
            .ApplyPropertyValue(r => r.FontWeight = FontWeight.Bold); // "cd"

        var runs = p.Inlines.OfType<Run>().ToList();
        Assert.Equal(new[] { "ab", "cd", "ef" }, runs.Select(r => r.Text));
        Assert.Equal(
            new[] { FontWeight.Normal, FontWeight.Bold, FontWeight.Normal },
            runs.Select(r => r.FontWeight));
    }

    [Fact]
    public void ApplyPropertyValue_SubRange_SplitKeepsFontFamilyAndBackground()
    {
        // Regression: the boundary split once hand-copied run fields and dropped
        // FontFamily/Background, so styling part of a styled run lost them on the tail.
        var p = TestHelpers.Para(new Run
        {
            Text = "abcdef",
            FontFamily = "Consolas",
            Background = Brushes.Yellow
        });
        new TextRange(new TextPointer(p, 2), new TextPointer(p, 4))
            .ApplyPropertyValue(r => r.FontWeight = FontWeight.Bold); // "cd"

        var runs = p.Inlines.OfType<Run>().ToList();
        Assert.Equal(new[] { "ab", "cd", "ef" }, runs.Select(r => r.Text));
        Assert.All(runs, r => Assert.Equal("Consolas", r.FontFamily));
        Assert.All(runs, r => Assert.Equal(Brushes.Yellow, r.Background));
    }

    // ---- multi-paragraph operations ----

    [Fact]
    public void GetText_MultiParagraph_JoinsWithNewline()
    {
        var p1 = TestHelpers.Para(new Run { Text = "Hello" });
        var p2 = TestHelpers.Para(new Run { Text = "World" });
        TestHelpers.Doc(p1, p2);
        Assert.Equal("llo\nWor", new TextRange(new TextPointer(p1, 2), new TextPointer(p2, 3)).GetText());
    }

    [Fact]
    public void Delete_MultiParagraph_RemovesMiddle_AndMergesEndpoints()
    {
        var p1 = TestHelpers.Para(new Run { Text = "Hello" });
        var pm = TestHelpers.Para(new Run { Text = "MID" });
        var p2 = TestHelpers.Para(new Run { Text = "World" });
        var doc = TestHelpers.Doc(p1, pm, p2);

        new TextRange(new TextPointer(p1, 2), new TextPointer(p2, 2)).Delete();

        Assert.Single(doc.Blocks);          // middle removed, end merged into start
        Assert.Equal("Herld", p1.Text());   // "He" + "rld"
    }

    [Fact]
    public void Delete_MultiParagraph_SpanningTable_RemovesTableBlock()
    {
        var p1 = TestHelpers.Para(new Run { Text = "A" });
        var table = new TableBlock(2, 2);
        var p2 = TestHelpers.Para(new Run { Text = "B" });
        var doc = TestHelpers.Doc(p1, table, p2);

        new TextRange(new TextPointer(p1, 1), new TextPointer(p2, 0)).Delete();

        Assert.DoesNotContain(doc.Blocks, b => b is TableBlock);
        Assert.Equal("AB", ((Paragraph)doc.Blocks[0]).Text());
    }

    [Fact]
    public void Delete_AcrossTableCells_KeepsCellStructure()
    {
        // Regression: the end cell's remainder used to be merged into the start cell
        // (and the end cell emptied), corrupting the grid.
        var table = new TableBlock(1, 2);
        var c0 = table.Cells[0][0];
        var c1 = table.Cells[0][1];
        c0.Inlines.Add(new Run { Text = "abc", Parent = c0 });
        c1.Inlines.Add(new Run { Text = "def", Parent = c1 });
        var doc = TestHelpers.Doc(table);
        c0.Parent = table; c1.Parent = table; // cell parents (Doc() wires top-level blocks only)

        // "bc" in cell0 through "de" in cell1.
        new TextRange(new TextPointer(c0, 1), new TextPointer(c1, 2)).Delete();

        Assert.Equal("a", c0.Text());  // no text moved across the grid
        Assert.Equal("f", c1.Text());
        Assert.Equal(2, table.Columns);
    }

    [Fact]
    public void ApplyPropertyValue_MultiParagraph_StylesMiddleParagraph()
    {
        var p1 = TestHelpers.Para(new Run { Text = "aaa" });
        var p2 = TestHelpers.Para(new Run { Text = "bbb" });
        var p3 = TestHelpers.Para(new Run { Text = "ccc" });
        TestHelpers.Doc(p1, p2, p3);

        new TextRange(new TextPointer(p1, 0), new TextPointer(p3, 3))
            .ApplyPropertyValue(r => r.FontWeight = FontWeight.Bold);

        foreach (var p in new[] { p1, p2, p3 })
            Assert.All(p.Inlines.OfType<Run>(), r => Assert.Equal(FontWeight.Bold, r.FontWeight));
    }
}
