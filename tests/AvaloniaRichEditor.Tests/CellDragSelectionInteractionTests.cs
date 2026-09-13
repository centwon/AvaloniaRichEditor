using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// CellBlockSelectionTests reproduces a cross-cell drag by writing the two endpoints straight into the
// fields, so it can only check what the commands do with a selection that is already correct. Round 3's
// defect was upstream of that: the drag itself produced a selection that disagreed with what was painted.
// These drag with the pointer and read what the drag left behind.
public class CellDragSelectionInteractionTests
{
    // The table holding the active cell block (the one the renderer fills and the commands act on), or null.
    private static TableBlock? BlockTable(RichEditor ed)
        => (TableBlock?)typeof(RichEditor).GetMethod("CellBlockTable", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(ed, null);

    private static List<TableCell>? SelectedCells(RichEditor ed)
        => (List<TableCell>?)typeof(RichEditor)
            .GetMethod("SelectedCellsBlock", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ed, null);

    // A grid whose every cell carries its own "r,c" text, shown in a window.
    private static (InteractionHost host, TableBlock tb) Grid(int rows, int cols)
    {
        var tb = new TableBlock(rows, cols);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                ((Run)tb.Cells[r][c].Para.Inlines[0]).Text = $"{r}{c}";
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        var host = InteractionHost.Create(ed);
        host.Render();
        return (host, tb);
    }

    // A point that hit-tests into cell (r,c). Found by sweeping rather than recomputing the table
    // geometry, so the test can't drift from the layout the renderer actually produced.
    private static Point InCell(InteractionHost host, TableBlock tb, int r, int c)
    {
        var target = tb.Cells[r][c].Para;
        var hit = typeof(RichEditor).GetMethod("GetPositionFromPoint", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (double y = 2; y < host.Editor.DesiredSize.Height; y += 3)
            for (double x = 2; x < host.Editor.Bounds.Width; x += 3)
            {
                var p = new Point(x, y);
                if (ReferenceEquals(((TextPointer)hit.Invoke(host.Editor, new object[] { p })!).Paragraph, target))
                    return p;
            }

        throw new Xunit.Sdk.XunitException($"no point hit-tests into cell ({r},{c})");
    }

    private static string TextOf(TableCell cell)
        => string.Concat(cell.Blocks.OfType<Paragraph>().Select(p => p.Text()));

    [AvaloniaFact]
    public void DraggingAcrossTwoCellsEntersCellSelection()
    {
        var (host, tb) = Grid(2, 2);

        host.Drag(InCell(host, tb, 0, 0), InCell(host, tb, 0, 1), InCell(host, tb, 1, 1));

        Assert.Same(tb, BlockTable(host.Editor));
    }

    // A right-click in another cell while text is selected keeps the caret with the selection — the menu acts on
    // the selection. The caret moved to the clicked cell while the selection stayed behind, so it was drawn in
    // another cell and Shift+arrow extended from there (measured 2026-09-14). Without a selection it does move.
    [AvaloniaFact]
    public void ARightClickInAnotherCell_KeepsTheCaretWithTheSelection()
    {
        var (host, tb) = Grid(2, 2);
        var p00 = tb.Cells[0][0].Para;
        var set = (string f, TextPointer v) => typeof(RichEditor).GetField(f, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(host.Editor, v);
        set("_selectionStart", new TextPointer(p00, 0));
        set("_selectionEnd", new TextPointer(p00, 2));
        set("_caretPosition", new TextPointer(p00, 2));

        host.Click(InCell(host, tb, 1, 1), Avalonia.Input.MouseButton.Right);

        var caret = Field<TextPointer>(host.Editor, "_caretPosition");
        Assert.Same(p00, caret.Paragraph);
        Assert.Equal(2, caret.Offset);
        Assert.Same(p00, Field<TextPointer>(host.Editor, "_selectionStart").Paragraph);
        Assert.Equal(2, Field<TextPointer>(host.Editor, "_selectionEnd").Offset);

        // Control: with no selection, a right-click places the caret where it lands.
        set("_selectionStart", new TextPointer(p00, 2));
        host.Click(InCell(host, tb, 1, 1), Avalonia.Input.MouseButton.Right);
        Assert.Same(tb.Cells[1][1].Para, Field<TextPointer>(host.Editor, "_caretPosition").Paragraph);
    }

    // …and the "Table" submenu then acts on the caret's table. Its row/column items take their cell from the caret,
    // so a submenu built for the table under the pointer would address another table by the caret table's indices.
    [AvaloniaFact]
    public void ARightClickInAnotherTable_WithASelection_TheTableSubmenuActsOnTheCaretsTable()
    {
        var a = new TableBlock(2, 2);
        var b = new TableBlock(2, 2);
        foreach (var t in new[] { a, b })
            foreach (var (r, c, cell) in t.LogicalCells()) ((Run)cell.Para.Inlines[0]).Text = $"{r}{c}";
        var doc = new FlowDocument();
        doc.Blocks.Add(a);
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "between" } } });
        doc.Blocks.Add(b);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        var host = InteractionHost.Create(ed);
        host.Render();

        var pa = a.Cells[0][0].Para;
        foreach (var f in new[] { "_selectionStart", "_selectionEnd", "_caretPosition" })
            typeof(RichEditor).GetField(f, BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(ed, new TextPointer(pa, f == "_selectionStart" ? 0 : 2));

        host.Click(InCell(host, b, 1, 1), Avalonia.Input.MouseButton.Right);

        var menu = (Avalonia.Controls.ContextMenu)typeof(RichEditor).GetField("_openContextMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ed)!;
        var tableOps = (menu.ItemsSource ?? menu.Items)!.OfType<Avalonia.Controls.MenuItem>()
            .Single(m => (m.Header as string) == RichEditorLocalization.GetString("TableOps"));
        var below = (tableOps.ItemsSource ?? tableOps.Items)!.OfType<Avalonia.Controls.MenuItem>()
            .Single(m => (m.Header as string) == RichEditorLocalization.GetString("InsertRowBelow"));
        below.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));

        Assert.Equal(3, a.Rows); // the caret's table got the row
        Assert.Equal(2, b.Rows); // the one under the pointer is untouched
    }

    private static T Field<T>(RichEditor ed, string name)
        => (T)typeof(RichEditor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ed)!;

    // A click after a cross-cell drag places a caret in the cell — no sticky mode making it select a cell (and
    // a double-click to edit); unified with the WinUI port, 2026-09-13.
    [AvaloniaFact]
    public void AClickAfterACrossCellDrag_PlacesACaret()
    {
        var (host, tb) = Grid(2, 2);
        host.Drag(InCell(host, tb, 0, 0), InCell(host, tb, 1, 1));
        Assert.Same(tb, BlockTable(host.Editor));

        host.Click(InCell(host, tb, 1, 0));

        Assert.Null(BlockTable(host.Editor));
        Assert.Null(SelectedCells(host.Editor));
    }

    // The selection the commands act on must be the rectangle the drag swept — whole cells, not the
    // linear text run between the endpoints.
    [AvaloniaFact]
    public void ACrossCellDragSelectsTheWholeSweptRectangle()
    {
        var (host, tb) = Grid(3, 3);

        host.Drag(InCell(host, tb, 0, 0), InCell(host, tb, 1, 1));

        var cells = SelectedCells(host.Editor);
        Assert.NotNull(cells);
        Assert.Equal(4, cells!.Count);
        Assert.Equal(new[] { "00", "01", "10", "11" }, cells.Select(TextOf).OrderBy(t => t).ToArray());
    }

    // ...and cells outside that rectangle stay out of it, even though they sit between the endpoints in
    // linear document order.
    [AvaloniaFact]
    public void CellsOutsideTheSweptRectangleAreNotSelected()
    {
        var (host, tb) = Grid(3, 3);

        host.Drag(InCell(host, tb, 0, 0), InCell(host, tb, 2, 0));

        var cells = SelectedCells(host.Editor);
        Assert.NotNull(cells);
        Assert.Equal(new[] { "00", "10", "20" }, cells!.Select(TextOf).OrderBy(t => t).ToArray());
    }

    // Delete then clears exactly those cells and keeps the grid shape.
    [AvaloniaFact]
    public void DeleteAfterACellDragClearsThoseCellsOnly()
    {
        var (host, tb) = Grid(2, 2);
        host.Drag(InCell(host, tb, 0, 0), InCell(host, tb, 0, 1));

        host.Key(Avalonia.Input.Key.Delete);

        Assert.Equal("", TextOf(tb.Cells[0][0]));
        Assert.Equal("", TextOf(tb.Cells[0][1]));
        Assert.Equal("10", TextOf(tb.Cells[1][0]));
        Assert.Equal(2, tb.Rows);
    }

    // A drag inside one cell is ordinary text selection: no cell block, so typing replaces the text
    // instead of clearing whole cells.
    [AvaloniaFact]
    public void DraggingInsideOneCellStaysATextSelection()
    {
        var (host, tb) = Grid(2, 2);
        var p = InCell(host, tb, 0, 0);

        host.Drag(p, p + new Point(20, 0));

        Assert.Null(BlockTable(host.Editor));
        Assert.Null(SelectedCells(host.Editor));
    }
}
