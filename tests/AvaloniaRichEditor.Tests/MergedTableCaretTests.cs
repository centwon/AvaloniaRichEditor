using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Round 14. Inserting or deleting a row/column parks the caret on a fixed slot — column 0 of the new row,
// row 0 of the new column — and did so without asking whether that slot is COVERED by a merge. A covered
// cell is absent from LogicalCells(), so it is not rendered, not clickable, not walked by any formatter
// and not reachable by navigation: the caret sat in a paragraph the document cannot show, and typing went
// somewhere invisible until the next click moved the caret out.
//
// FocusCell has always done this redirect — its own comment describes it — and these four are the paths
// that assign _caretPosition directly instead of going through it.
//
// Found by the invariant fuzz once a merge axis was added to its generator: the merge grid was asserted
// from both directions, but nothing ever produced a merge, so a quarter of the assertions had never run.
public class MergedTableCaretTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static TextPointer Caret(RichEditor ed)
        => (TextPointer)typeof(RichEditor).GetField("_caretPosition", NP)!.GetValue(ed)!;

    private static void Invoke(RichEditor ed, string method, TableBlock tb, int at)
        => typeof(RichEditor).GetMethod(method, NP)!.Invoke(ed, new object[] { tb, at });

    // Every paragraph the document can actually reach, mirroring what render and navigation walk.
    private static bool Reachable(RichEditor ed, Paragraph p)
    {
        bool Walk(System.Collections.Generic.IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
            {
                if (b is Paragraph q)
                {
                    if (ReferenceEquals(q, p)) return true;
                    foreach (var inl in q.Inlines)
                        if (inl is InlineTable it)
                            foreach (var (_, _, cell) in it.Table.LogicalCells())
                                if (Walk(cell.Blocks)) return true;
                }
                else if (b is TableBlock tb)
                    foreach (var (_, _, cell) in tb.LogicalCells())
                        if (Walk(cell.Blocks)) return true;
            }
            return false;
        }
        return Walk(ed.Document!.Blocks);
    }

    private static (RichEditor ed, TableBlock tb) VerticallyMerged()
    {
        var ed = new RichEditor { Document = new FlowDocument(), PageSize = RichEditorPageSize.Continuous };
        ed.FocusDocumentEnd();
        ed.InsertTable(3, 2);
        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();
        // Column 0 of rows 0-1 becomes one cell, so (1,0) is covered: exactly the slot the row/column
        // operations aim the caret at.
        tb.MergeCells(0, 0, 1, 0);
        return (ed, tb);
    }

    [AvaloniaFact]
    public void InsertingARowIntoAMergedColumn_LeavesTheCaretReachable()
    {
        var (ed, tb) = VerticallyMerged();

        Invoke(ed, "TableInsertRow", tb, 1);

        // Inserting inside the merged span grows the rowspan, so the slot the operation aims at — (1,0) —
        // is still covered afterwards. That is precisely what makes this the failing case.
        Assert.True(tb.IsCovered(1, 0), "precondition: (1,0) must be covered for this to mean anything");
        Assert.True(Reachable(ed, Caret(ed).Paragraph!),
            "the caret landed in a cell the document cannot reach");
    }

    [AvaloniaFact]
    public void DeletingARowThatShiftsAMerge_LeavesTheCaretReachable()
    {
        var (ed, tb) = VerticallyMerged();

        Invoke(ed, "TableDeleteRow", tb, 2);

        Assert.True(Reachable(ed, Caret(ed).Paragraph!),
            "the caret landed in a cell the document cannot reach");
    }

    [AvaloniaFact]
    public void InsertingAColumnBesideAMergedRow_LeavesTheCaretReachable()
    {
        var ed = new RichEditor { Document = new FlowDocument(), PageSize = RichEditorPageSize.Continuous };
        ed.FocusDocumentEnd();
        ed.InsertTable(2, 3);
        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();
        tb.MergeCells(0, 0, 0, 1); // row 0 spans two columns, so (0,1) is covered

        // Inside the span, so the colspan grows and the slot the operation aims at stays covered.
        // Inserting outside it lands on a fresh column and proves nothing.
        Invoke(ed, "TableInsertColumn", tb, 1);

        Assert.True(tb.IsCovered(0, 1), "precondition: (0,1) must be covered for this to mean anything");
        Assert.True(Reachable(ed, Caret(ed).Paragraph!),
            "the caret landed in a cell the document cannot reach");
    }

    // The redirect must land on the anchor that actually covers the slot, not merely on something
    // reachable — the caret belongs in the cell the user sees at that position.
    [AvaloniaFact]
    public void TheCaretRedirectsToTheAnchorThatCoversTheSlot()
    {
        var (ed, tb) = VerticallyMerged();
        Invoke(ed, "TableInsertRow", tb, 3); // append a row; (0,0) still anchors rows 0-1

        // Aim at a slot that IS covered by driving the caret there through the same helper the
        // operations use, then confirm it resolved to the anchor rather than the covered cell.
        var target = typeof(RichEditor).GetMethod("CellCaretTarget",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var got = (Paragraph)target.Invoke(null, new object[] { tb, 1, 0 })!;

        Assert.True(tb.IsCovered(1, 0), "precondition: (1,0) must be covered for this to mean anything");
        Assert.Same(tb.Cells[0][0].Para, got);
    }

    // Typing after the operation has to reach the document, which is the behaviour the invariant protects.
    [AvaloniaFact]
    public void TypingAfterInsertingARowIntoAMergedColumn_ReachesTheDocument()
    {
        var (ed, tb) = VerticallyMerged();
        Invoke(ed, "TableInsertRow", tb, 1);

        ed.RaiseEvent(new Avalonia.Input.TextInputEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.TextInputEvent,
            Text = "typed",
        });

        Assert.Contains("typed", ed.GetPlainText());
    }
}
