using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Table drags that must stay inside their bounds — backported from the WinUI port's table-resize audit
// (2026-09-15), where RichEditor.TableResize.cs had never been referenced by a test.
//   * A table inside a cell is capped to the cell's content width. The nested case was (EnclosingCellInnerWidth);
//     an inline table in a cell's paragraph was not, and grew through the neighbouring cells.
//   * The cap never goes below the width the drag started from: a nested table that already overflows (a file
//     says so) snapped its last column down to the floor on the first pixel of movement.
//   * An interior edge between two sub-minimum columns inverted Math.Clamp's bounds (ArgumentException).
//   * A lost pointer capture (no release ever arrives) ended nothing: the next plain hover kept resizing, or
//     kept extending the selection.
public class ResizeDragContainmentTests
{
    private static InteractionHost Host(FlowDocument doc)
    {
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        var host = InteractionHost.Create(ed);
        host.Render(); // handles are recorded while painting
        return host;
    }

    private static TableBlock Table(int rows, int cols, params double[] widths)
    {
        var tb = new TableBlock(rows, cols);
        for (int c = 0; c < cols; c++) tb.ColumnWidths[c] = widths.Length == 1 ? widths[0] : widths[c];
        return tb;
    }

    private static FlowDocument Doc(Block b)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(b);
        doc.Blocks.Add(new Paragraph());
        return doc;
    }

    private static Rect Handle(InteractionHost host, TableBlock tb, int col)
        => host.ColumnHandles.First(h => ReferenceEquals(h.tb, tb) && h.colIndex == col).rect;

    // The 1×2 outer table of 200px columns: cell (0,0) has a 190px content box (EnclosingCellInnerWidth).
    private const double CellContent = 190;

    private static (InteractionHost host, TableBlock inner) Nested(params double[] innerWidths)
    {
        var outer = Table(1, 2, 200);
        var inner = Table(1, 2, innerWidths);
        outer.Cells[0][0].Blocks.Add(inner);
        outer.Cells[0][0].Blocks.Add(new Paragraph());
        return (Host(Doc(outer)), inner);
    }

    // ---- a table inside a cell stays inside it ------------------------------------------------------

    [AvaloniaFact]
    public void ANestedTable_GrowsOnlyToItsCellsContentWidth()
    {
        var (host, inner) = Nested(90, 90);
        var h = Handle(host, inner, 1).Center;
        host.Drag(h, h + new Point(300, 0));
        Assert.Equal(CellContent, inner.ColumnWidths.Sum(), 1);
    }

    [AvaloniaFact]
    public void AnInlineTableInACell_GrowsOnlyToItsCellsContentWidth()
    {
        var outer = Table(1, 2, 200);
        var para = new Paragraph();
        para.Inlines.Add(new Run { Text = "x" });
        var it = new InlineTable { Table = Table(1, 2, 60) };
        para.Inlines.Add(it);
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(para);
        var host = Host(Doc(outer));

        var h = Handle(host, it.Table, 1).Center;
        host.Drag(h, h + new Point(300, 0));

        Assert.True(it.Table.ColumnWidths.Sum() <= CellContent + 0.01,
            $"the inline table grew to {it.Table.ColumnWidths.Sum()} in a {CellContent}px cell");
    }

    [AvaloniaFact]
    public void AnOverflowingNestedTable_DoesNotSnapWhenGrabbed_ButShrinks()
    {
        var (host, inner) = Nested(150, 150);
        var h = Handle(host, inner, 1).Center;
        host.Press(h);
        host.Move(h + new Point(1, 0), RawInputModifiers.LeftMouseButton);
        Assert.Equal(150, inner.ColumnWidths[1], 1);
        host.Move(h + new Point(100, 0), RawInputModifiers.LeftMouseButton);
        Assert.Equal(150, inner.ColumnWidths[1], 1);
        host.Move(h + new Point(-30, 0), RawInputModifiers.LeftMouseButton);
        Assert.Equal(120, inner.ColumnWidths[1], 1);
        host.Release(h + new Point(-30, 0));
    }

    // ---- sub-minimum columns ------------------------------------------------------------------------

    // Both columns under the 20px floor: the naive bounds invert (min > max) and Math.Clamp throws. Widths
    // like these are ordinary — HTML keeps any positive <td width>, a nested InsertTable floors at 15.
    [AvaloniaFact]
    public void AnInteriorEdgeBetweenSubMinimumColumns_DragsWithoutThrowing()
    {
        var tb = Table(1, 3, 15, 15, 100);
        var host = Host(Doc(tb));
        var h = Handle(host, tb, 0).Center;

        host.Drag(h, h + new Point(10, 0));

        Assert.Equal(30, tb.ColumnWidths[0] + tb.ColumnWidths[1], 1); // the pair keeps its total
        Assert.Equal(25, tb.ColumnWidths[0], 1);
    }

    // ---- a lost capture ends the drag ---------------------------------------------------------------

    // The pointer as the editor saw it on press, so a test can take its capture away mid-drag — what a window
    // deactivating (or another element capturing) does, with no release ever reaching the editor.
    private static System.Func<IPointer?> TrackPointer(InteractionHost host)
    {
        IPointer? pointer = null;
        host.Editor.AddHandler(InputElement.PointerPressedEvent,
            (object? _, PointerPressedEventArgs e) => pointer = e.Pointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        return () => pointer;
    }

    private static void LoseCapture(InteractionHost host, System.Func<IPointer?> pointer)
    {
        Assert.NotNull(pointer());
        pointer()!.Capture(null);
        host.Pump();
    }

    [AvaloniaFact]
    public void ALostCapture_EndsAColumnDrag()
    {
        var tb = Table(2, 2, 100);
        var host = Host(Doc(tb));
        var pointer = TrackPointer(host);
        var h = Handle(host, tb, 0).Center;

        host.Press(h);
        host.Move(h + new Point(20, 0), RawInputModifiers.LeftMouseButton);
        Assert.Equal(120, tb.ColumnWidths[0], 1); // the drag is live
        LoseCapture(host, pointer);
        host.Move(h + new Point(60, 0)); // a plain hover: no button

        Assert.Equal(120, tb.ColumnWidths[0], 1);
    }

    [AvaloniaFact]
    public void ALostCapture_EndsARowDrag()
    {
        var tb = Table(2, 2, 100);
        var host = Host(Doc(tb));
        var pointer = TrackPointer(host);
        var row = host.RowHandles.First(r => ReferenceEquals(r.tb, tb) && r.rowIndex == 0).rect;
        var grab = new Point(row.Left + 10, row.Center.Y); // off the column boundary (see ResizeDragInteractionTests)

        host.Press(grab);
        host.Move(grab + new Point(0, 30), RawInputModifiers.LeftMouseButton);
        double live = tb.RowHeights[0];
        LoseCapture(host, pointer);
        host.Move(grab + new Point(0, 90));

        Assert.Equal(live, tb.RowHeights[0], 1);
    }

    [AvaloniaFact]
    public void ALostCapture_EndsADragSelection()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "a line of text long enough to select across several words" } } });
        var host = Host(doc);
        var pointer = TrackPointer(host);

        host.Press(new Point(20, 8));
        host.Move(new Point(80, 8), RawInputModifiers.LeftMouseButton);
        string live = host.SelectedText;
        Assert.NotEqual("", live); // the drag really selected something
        LoseCapture(host, pointer);
        host.Move(new Point(250, 8));

        Assert.Equal(live, host.SelectedText);
    }

    // Control: a normal release still ends the drag, and the hover after it changes nothing.
    [AvaloniaFact]
    public void AReleasedDrag_StaysPut_OnTheHoverAfter()
    {
        var tb = Table(2, 2, 100);
        var host = Host(Doc(tb));
        var h = Handle(host, tb, 0).Center;
        host.Drag(h, h + new Point(20, 0));
        host.Move(h + new Point(60, 0));
        Assert.Equal(120, tb.ColumnWidths[0], 1);
    }
}
