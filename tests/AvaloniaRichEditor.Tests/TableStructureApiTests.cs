using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The row/column commands existed only behind the context menu, so a host building its own toolbar — or
// generating a document by script — had to edit TableBlock itself and skip the undo checkpoint, the parent
// wiring and the layout invalidation the commands do (backlog item, 2026-09-20).
//
// Two shapes over the same body: Table* names the table and the index, the caret pair acts where the caret
// is. What these tests hold is the part a host cannot see: the guards on a public entry point (an index
// from anywhere, a table from another document, a read-only editor), that one call is one undo step, and
// that "below/right" means past a merged area — the same rule the menu uses.
public class TableStructureApiTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void PlaceCaret(RichEditor ed, Paragraph p, int offset = 0)
    {
        foreach (var f in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(f, NP)!.SetValue(ed, new TextPointer(p, offset));
    }

    private static RichEditor Editor(string html)
    {
        var ed = new RichEditor();
        ed.LoadHtml(html);
        return ed;
    }

    private static TableBlock Table(RichEditor ed) => ed.Document!.Blocks.OfType<TableBlock>().Single();

    private const string TwoByTwo = "<table><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>";

    [AvaloniaFact]
    public void InsertingARow_AddsIt_AndIsOneUndoStep()
    {
        var ed = Editor(TwoByTwo);
        var tb = Table(ed);

        Assert.True(ed.InsertTableRow(tb, 1));
        Assert.Equal(3, tb.Rows);

        ed.Undo();
        Assert.Equal(2, Table(ed).Rows);
    }

    [AvaloniaFact]
    public void InsertingAtTheRowCount_Appends()
    {
        var ed = Editor(TwoByTwo);
        var tb = Table(ed);

        Assert.True(ed.InsertTableRow(tb, tb.Rows));

        Assert.Equal(3, tb.Rows);
    }

    [AvaloniaFact]
    public void DeletingARow_RemovesIt_AndIsOneUndoStep()
    {
        var ed = Editor(TwoByTwo);
        var tb = Table(ed);

        Assert.True(ed.DeleteTableRow(tb, 0));
        Assert.Equal(1, tb.Rows);

        ed.Undo();
        Assert.Equal(2, Table(ed).Rows);
    }

    [AvaloniaFact]
    public void ColumnsBehaveLikeRows()
    {
        var ed = Editor(TwoByTwo);
        var tb = Table(ed);

        Assert.True(ed.InsertTableColumn(tb, 0));
        Assert.Equal(3, tb.Columns);
        Assert.True(ed.DeleteTableColumn(tb, 0));
        Assert.Equal(2, tb.Columns);
    }

    // A table always keeps its last row and column — the model refuses, and the public entry has to report
    // that rather than push an undo step for an edit that did not happen.
    [AvaloniaFact]
    public void TheLastRowAndColumnAreKept()
    {
        var ed = Editor("<table><tr><td>only</td></tr></table>");
        var tb = Table(ed);

        Assert.False(ed.DeleteTableRow(tb, 0));
        Assert.False(ed.DeleteTableColumn(tb, 0));

        Assert.Equal(1, tb.Rows);
        Assert.Equal(1, tb.Columns);
        Assert.False(ed.IsModified);
    }

    // An index from a host is not trusted. Deletion takes 0..count-1, insertion one more (append).
    [AvaloniaTheory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void AnOutOfRangeIndex_ChangesNothing(int at)
    {
        var ed = Editor(TwoByTwo);
        var tb = Table(ed);

        Assert.False(ed.InsertTableRow(tb, at));   // 2 rows: 0..2 is in range, 3 is not
        Assert.False(ed.DeleteTableRow(tb, at));
        Assert.False(ed.InsertTableColumn(tb, at));
        Assert.False(ed.DeleteTableColumn(tb, at));

        Assert.Equal(2, tb.Rows);
        Assert.Equal(2, tb.Columns);
        Assert.False(ed.IsModified); // no undo checkpoint either
    }

    // The worst input: a live table that belongs to a DIFFERENT document. Editing it would push an undo
    // checkpoint for, and mutate, a tree this editor does not show — the snapshot would then restore the
    // editor's own document over an edit the host thinks it made.
    [AvaloniaFact]
    public void ATableFromAnotherDocument_IsRefused()
    {
        var ed = Editor(TwoByTwo);
        var other = Editor(TwoByTwo);
        var foreign = Table(other);

        Assert.False(ed.InsertTableRow(foreign, 0));
        Assert.False(ed.DeleteTableRow(foreign, 0));
        Assert.False(ed.InsertTableColumn(foreign, 0));
        Assert.False(ed.DeleteTableColumn(foreign, 0));

        Assert.Equal(2, foreign.Rows);
        Assert.Equal(2, foreign.Columns);
        Assert.False(ed.IsModified);
        Assert.False(other.IsModified);
    }

    [AvaloniaFact]
    public void AReadOnlyEditor_RefusesAll()
    {
        var ed = Editor(TwoByTwo);
        var tb = Table(ed);
        PlaceCaret(ed, tb.Cells[0][0].Para);
        ed.IsReadOnly = true;

        Assert.False(ed.InsertTableRow(tb, 0));
        Assert.False(ed.DeleteTableRow(tb, 0));
        Assert.False(ed.InsertRowBelow());
        Assert.False(ed.DeleteColumn());

        Assert.Equal(2, tb.Rows);
        Assert.Equal(2, tb.Columns);
    }

    // ---- the caret shape --------------------------------------------------------------------------

    [AvaloniaFact]
    public void TheCaretCommands_ActOnTheCaretsTable()
    {
        var ed = Editor(TwoByTwo);
        var tb = Table(ed);
        PlaceCaret(ed, tb.Cells[0][0].Para);

        Assert.True(ed.InsertRowAbove());
        Assert.Equal(3, tb.Rows);
        Assert.True(ed.InsertColumnLeft());
        Assert.Equal(3, tb.Columns);
        Assert.True(ed.DeleteRow());
        Assert.Equal(2, tb.Rows);
        Assert.True(ed.DeleteColumn());
        Assert.Equal(2, tb.Columns);
    }

    [AvaloniaFact]
    public void WithTheCaretOutsideATable_TheCaretCommandsDoNothing()
    {
        var ed = Editor("<p>plain</p>" + TwoByTwo);
        var tb = Table(ed);
        PlaceCaret(ed, ed.Document!.Blocks.OfType<Paragraph>().First());

        Assert.False(ed.InsertRowAbove());
        Assert.False(ed.InsertRowBelow());
        Assert.False(ed.DeleteRow());
        Assert.False(ed.InsertColumnLeft());
        Assert.False(ed.InsertColumnRight());
        Assert.False(ed.DeleteColumn());

        Assert.Equal(2, tb.Rows);
        Assert.Equal(2, tb.Columns);
    }

    // "Below" a cell that spans two rows is below the WHOLE merge: at r+1 the new row lands inside it, the
    // merge grows over it, and only the other columns gain a row. Same rule as the context menu, which is
    // why both call PastMerge.
    [AvaloniaFact]
    public void InsertRowBelow_GoesPastAVerticalMerge()
    {
        var ed = Editor("<table><tr><td rowspan='2'>tall</td><td>b</td></tr><tr><td>d</td></tr>"
                        + "<tr><td>e</td><td>f</td></tr></table>");
        var tb = Table(ed);
        Assert.Equal((1, 2), tb.SpanOf(0, 0)); // the fixture really is merged
        PlaceCaret(ed, tb.Cells[0][0].Para);

        Assert.True(ed.InsertRowBelow());

        // The new row is row 2 — after the merged area — so the merge still spans exactly rows 0..1.
        Assert.Equal(4, tb.Rows);
        Assert.Equal((1, 2), tb.SpanOf(0, 0));
        Assert.False(tb.IsCovered(2, 0));
    }

    [AvaloniaFact]
    public void InsertColumnRight_GoesPastAHorizontalMerge()
    {
        var ed = Editor("<table><tr><td colspan='2'>wide</td><td>c</td></tr>"
                        + "<tr><td>d</td><td>e</td><td>f</td></tr></table>");
        var tb = Table(ed);
        Assert.Equal((2, 1), tb.SpanOf(0, 0));
        PlaceCaret(ed, tb.Cells[0][0].Para);

        Assert.True(ed.InsertColumnRight());

        Assert.Equal(4, tb.Columns);
        Assert.Equal((2, 1), tb.SpanOf(0, 0));
        Assert.False(tb.IsCovered(0, 2));
    }

    // Cells hold block lists, so tables nest. FindCell resolves the INNERMOST table through the parent
    // chain — the caret commands must edit that one, not the table around it.
    [AvaloniaFact]
    public void InANestedTable_TheCaretCommandsEditTheInnerTable()
    {
        var ed = Editor("<table><tr><td><table><tr><td>in</td><td>ner</td></tr></table></td>"
                        + "<td>outer</td></tr></table>");
        var outer = Table(ed);
        var inner = outer.Cells[0][0].Blocks.OfType<TableBlock>().Single();
        PlaceCaret(ed, inner.Cells[0][0].Para);

        Assert.True(ed.InsertRowBelow());

        Assert.Equal(2, inner.Rows);
        Assert.Equal(1, outer.Rows);
    }

    // An inline table lives in a paragraph's inlines, not in any block list — the containment check walks
    // there too (ParagraphsInBlocks descends into inline tables), so its rows are editable like any other.
    [AvaloniaFact]
    public void AnInlineTable_IsEditableToo()
    {
        var ed = Editor("<p>x<table data-are-inline='1'><tr><td>a</td><td>b</td></tr></table>y</p>");
        var host = ed.Document!.Blocks.OfType<Paragraph>().First(p => p.Inlines.OfType<InlineTable>().Any());
        var it = host.Inlines.OfType<InlineTable>().Single();
        PlaceCaret(ed, it.Table.Cells[0][0].Para);

        Assert.True(ed.InsertTableRow(it.Table, 1));
        Assert.True(ed.InsertRowBelow());

        Assert.Equal(3, it.Table.Rows);
    }
}
