using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Second audit round: defects only the format layer and the public model API can reach — a soft break
// dropped on HTML export, an image-only cell lost on merge, and TextRange.Delete() leaving a paragraph
// with no inlines at all. The rest of the round is filed where its contract already lives: the oversized
// RTF parameter in DamagedRtfTests, and the two cases needing a REAL decoded bitmap (RTF export of a
// bitmap-only image, HTML import with one axis declared) in AvaloniaRichEditor.Tests.Render.
public class AuditFix2Tests
{
    // Shift+Enter is a `\n` INSIDE a run. HTML collapses a bare newline to a space, so the break was
    // lost on export while import turned `<br>` back into `\n` — the round trip only lost lines.
    [AvaloniaFact]
    public void ExportHtml_SoftBreakInRun_BecomesBr()
    {
        var doc = TestHelpers.Doc(TestHelpers.Para(new Run { Text = "one\ntwo" }));

        string html = HtmlDocumentFormatter.ToHtml(doc);

        Assert.Contains("<br/>", html);
        Assert.DoesNotContain("one\ntwo", html);
    }

    [AvaloniaFact]
    public void HtmlRoundTrip_SoftBreak_Survives()
    {
        var doc = TestHelpers.Doc(TestHelpers.Para(new Run { Text = "one\ntwo" }));

        var back = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));

        Assert.Equal("one\ntwo", back.Blocks.OfType<Paragraph>().Single().Text());
    }

    // The space in front of a soft break is the case PreserveDroppableSpaces exists for: it has to
    // become &nbsp; BEFORE the newline turns into a tag, or HTML throws it away.
    [AvaloniaFact]
    public void HtmlRoundTrip_SpaceBeforeSoftBreak_Survives()
    {
        var doc = TestHelpers.Doc(TestHelpers.Para(new Run { Text = "one \ntwo" }));

        var back = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));

        Assert.Equal("one \ntwo", back.Blocks.OfType<Paragraph>().Single().Text());
    }

    // A cell holding only an InlineImage has no non-empty Run, and the merge tested for TEXT: the
    // image stayed in the covered cell, which LogicalCells() skips — gone from render, navigation and
    // extraction, and destroyed outright by the next unmerge.
    [AvaloniaFact]
    public void MergeCells_KeepsInlineOnlyContentOfCoveredCell()
    {
        var tb = new TableBlock(1, 2);
        tb.Cells[0][0].Para.Inlines.Add(new Run { Text = "A" });
        var covered = tb.Cells[0][1].Para;
        covered.Inlines.Clear();
        covered.Inlines.Add(new InlineImage());

        tb.MergeCells(0, 0, 0, 1);

        int visibleImages = tb.LogicalCells()
            .SelectMany(x => x.cell.Blocks.OfType<Paragraph>())
            .SelectMany(p => p.Inlines.OfType<InlineImage>())
            .Count();
        Assert.Equal(1, visibleImages);
        // ...and it was re-parented onto the anchor, not merely left reachable.
        Assert.Same(tb.Cells[0][0].Para, tb.Cells[0][0].Para.Inlines.OfType<InlineImage>().Single().Parent);
    }

    [AvaloniaFact]
    public void MergeCells_KeepsInlineTableOfCoveredCell()
    {
        var tb = new TableBlock(1, 2);
        tb.Cells[0][0].Para.Inlines.Add(new Run { Text = "A" });
        var covered = tb.Cells[0][1].Para;
        covered.Inlines.Clear();
        covered.Inlines.Add(TestHelpers.Tbl("inner"));

        tb.MergeCells(0, 0, 0, 1);

        Assert.Single(tb.Cells[0][0].Para.Inlines.OfType<InlineTable>());
    }

    // The oversized-RTF-parameter case lives in DamagedRtfTests, which owns the "what counts as a
    // damaged file" contract that tolerating it changes.

    // TextRange.Delete() is public API. Called on a range covering a paragraph's entire content it
    // removed every inline, breaking the "a paragraph always holds at least one Run" invariant the
    // offset model is built on. The editor's own delete paths restored it; a library caller's did not.
    [AvaloniaFact]
    public void TextRangeDelete_WholeParagraph_KeepsARun()
    {
        var p = TestHelpers.Para(new Run { Text = "hello" });
        TestHelpers.Doc(p);

        new TextRange(new TextPointer(p, 0), new TextPointer(p, 5)).Delete();

        Assert.Single(p.Inlines);
        Assert.Equal("", p.Text());
    }

    [AvaloniaFact]
    public void TextRangeDelete_ParagraphOfOnlyAnImage_KeepsARun()
    {
        var p = TestHelpers.Para(TestHelpers.Img());
        TestHelpers.Doc(p);

        new TextRange(new TextPointer(p, 0), new TextPointer(p, 1)).Delete();

        Assert.Single(p.Inlines);
        Assert.IsType<Run>(p.Inlines[0]);
    }
}
