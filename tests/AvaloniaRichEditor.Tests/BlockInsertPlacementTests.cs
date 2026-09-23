using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Where a block (table, picture, divider) goes when the caret is in a paragraph (user decision, 2026-09-23): the
// rule the object drag already had. In the middle the paragraph splits and the block goes between; at its start
// the block goes before it; at its end (or in an empty one) after it. Every insert used to go AFTER the caret's
// paragraph, so a picture pasted over a word in the middle of a line landed below the whole paragraph.
public class BlockInsertPlacementTests
{
    private static string Text(Block b) => b is Paragraph p ? string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text)) : b.GetType().Name;

    private static string[] Shape(RichEditor ed) => ed.Document!.Blocks.Select(Text).ToArray();

    private static InteractionHost Host(params Block[] blocks)
    {
        var doc = new FlowDocument();
        foreach (var b in blocks) doc.Blocks.Add(b);
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        return host;
    }

    private static void CaretAt(InteractionHost host, Paragraph p, int off)
    {
        var f = typeof(RichEditor).GetField("_caretPosition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        f.SetValue(host.Editor, new TextPointer(p, off));
        typeof(RichEditor).GetMethod("CollapseSelectionToCaret", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(host.Editor, null);
    }

    [AvaloniaTheory]
    [InlineData(4, new[] { "abcd", "DividerBlock", "efgh" })]  // middle: split, between
    [InlineData(0, new[] { "DividerBlock", "abcdefgh" })]      // start: before (NormalizeBlocks adds one ahead)
    [InlineData(8, new[] { "abcdefgh", "DividerBlock", "" })] // end: after, as before
    public void ADividerGoesWhereTheCaretIs(int offset, string[] expected)
    {
        var p = new Paragraph { Inlines = { new Run { Text = "abcdefgh" } } };
        var host = Host(p);
        CaretAt(host, p, offset);

        host.Editor.InsertDivider();

        var shape = Shape(host.Editor);
        // At the start, the document's first block may be a paragraph NormalizeBlocks put ahead of the divider.
        if (offset == 0) shape = shape.SkipWhile(s => s == "").ToArray();
        Assert.Equal(expected, shape);
    }

    // The same in a table cell: the cell's block list splits.
    [AvaloniaFact]
    public void InACell_TheCellParagraphSplits()
    {
        var tb = new TableBlock(1, 1);
        var cp = tb.Cells[0][0].Para;
        ((Run)cp.Inlines[0]).Text = "abcdefgh";
        var host = Host(new Paragraph { Inlines = { new Run { Text = "top" } } }, tb);
        CaretAt(host, cp, 4);

        host.Editor.InsertDivider();

        Assert.Equal(new[] { "abcd", "DividerBlock", "efgh" }, tb.Cells[0][0].Blocks.Select(Text).ToArray());
    }

    // A split heading stays a heading on both sides — Enter's "the next line is body text" is a typing rule.
    [AvaloniaFact]
    public void SplittingAHeading_KeepsBothHalvesHeadings()
    {
        var p = new Paragraph { HeadingLevel = 2, Inlines = { new Run { Text = "abcdefgh" } } };
        var host = Host(p);
        CaretAt(host, p, 4);

        host.Editor.InsertDivider();

        Assert.All(host.Editor.Document!.Blocks.OfType<Paragraph>().Where(x => Text(x).Length > 0), x => Assert.Equal(2, x.HeadingLevel));
    }

    // One undo step brings the paragraph back whole.
    [AvaloniaFact]
    public void Undo_RejoinsTheParagraph()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "abcdefgh" } } };
        var host = Host(p);
        CaretAt(host, p, 4);

        host.Editor.InsertDivider();
        host.Editor.Undo();

        Assert.Equal(new[] { "abcdefgh" }, Shape(host.Editor));
    }
}
