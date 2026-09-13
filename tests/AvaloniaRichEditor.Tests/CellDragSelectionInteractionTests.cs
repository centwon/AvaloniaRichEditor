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
