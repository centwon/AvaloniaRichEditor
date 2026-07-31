using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Resizing was only ever tested by calling the model changes and the invalidation helper directly
// (InlineTableMenuAndResizeTests), which cannot see whether the pointer actually reaches those helpers.
// Round 3's inline-table resize bug lived exactly there. These drive the drag through the window.
public class ResizeDragInteractionTests
{
    private static InteractionHost Host(FlowDocument doc)
    {
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        var host = InteractionHost.Create(ed);
        host.Render(); // handles are recorded while painting
        return host;
    }

    private static FlowDocument TableDoc(int rows = 2, int cols = 2)
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(rows, cols);
        doc.Blocks.Add(tb);
        return doc;
    }

    // ---- table columns ------------------------------------------------------

    [AvaloniaFact]
    public void DraggingAColumnBoundaryWidensThatColumn()
    {
        var host = Host(TableDoc());
        var tb = host.Editor.Document!.Blocks.OfType<TableBlock>().Single();
        var handle = host.ColumnHandles.First(h => h.colIndex == 0);
        double before = tb.ColumnWidths[0];

        host.Drag(handle.rect.Center, handle.rect.Center + new Point(40, 0));

        Assert.True(tb.ColumnWidths[0] > before,
            $"the drag must reach the column resize path ({tb.ColumnWidths[0]} vs {before})");
    }

    // An interior boundary trades width with the next column instead of growing the table.
    [AvaloniaFact]
    public void DraggingAnInteriorColumnBoundaryKeepsTheTableWidth()
    {
        var host = Host(TableDoc(2, 3));
        var tb = host.Editor.Document!.Blocks.OfType<TableBlock>().Single();
        var handle = host.ColumnHandles.First(h => h.colIndex == 0);
        double totalBefore = tb.ColumnWidths.Sum();

        host.Drag(handle.rect.Center, handle.rect.Center + new Point(30, 0));

        Assert.Equal(totalBefore, tb.ColumnWidths.Sum(), 1);
        Assert.True(tb.ColumnWidths[1] < tb.ColumnWidths[0]);
    }

    // ---- table rows ---------------------------------------------------------

    [AvaloniaFact]
    public void DraggingARowBoundaryGrowsThatRowAndTheEditor()
    {
        var host = Host(TableDoc());
        var tb = host.Editor.Document!.Blocks.OfType<TableBlock>().Single();
        var handle = host.RowHandles.First(h => h.rowIndex == 0);
        double heightBefore = host.Editor.DesiredSize.Height;

        // Not the middle of the boundary: a row handle spans the table's width, so its centre sits on a
        // column boundary, and the press checks columns first. Aim near the left edge instead.
        var grab = new Point(handle.rect.Left + 10, handle.rect.Center.Y);
        host.Drag(grab, grab + new Point(0, 60));
        host.Editor.Measure(new Size(host.Editor.Bounds.Width, double.PositiveInfinity));

        Assert.True(tb.RowHeights[0] >= 60, $"row height {tb.RowHeights[0]}");
        Assert.True(host.Editor.DesiredSize.Height > heightBefore,
            $"the editor must grow with the row ({host.Editor.DesiredSize.Height} vs {heightBefore})");
    }

    // ---- inline table -------------------------------------------------------
    //
    // The round 3 defect: dragging an inline table's row height left the host paragraph at its old
    // height until the next edit, because the invalidation stopped at the table.

    [AvaloniaFact]
    public void DraggingAnInlineTablesRowReflowsTheHostParagraphOnTheSameFrame()
    {
        var doc = new FlowDocument();
        var host = new Paragraph();
        host.Inlines.Add(new Run { Text = "before " });
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        host.Inlines.Add(it);
        doc.Blocks.Add(host);
        var h = Host(doc);
        double heightBefore = h.Editor.DesiredSize.Height;

        var handle = h.RowHandles.First(r => ReferenceEquals(r.tb, it.Table));
        h.Drag(handle.rect.Center, handle.rect.Center + new Point(0, 80));
        h.Editor.Measure(new Size(h.Editor.Bounds.Width, double.PositiveInfinity));

        Assert.True(h.Editor.DesiredSize.Height > heightBefore,
            $"the host paragraph must grow with the inline table ({h.Editor.DesiredSize.Height} vs {heightBefore})");
    }

    // ---- read-only ----------------------------------------------------------

    [AvaloniaFact]
    public void ReadOnlyEditorIgnoresAResizeDrag()
    {
        var host = Host(TableDoc());
        var tb = host.Editor.Document!.Blocks.OfType<TableBlock>().Single();
        var handle = host.ColumnHandles.First(h => h.colIndex == 0);
        double before = tb.ColumnWidths[0];
        host.Editor.IsReadOnly = true;

        host.Drag(handle.rect.Center, handle.rect.Center + new Point(40, 0));

        Assert.Equal(before, tb.ColumnWidths[0], 1);
    }

    // ---- undo ---------------------------------------------------------------
    //
    // One drag is one undo step: the checkpoint is taken on the first move, not per move event. Note the
    // caret is never placed here — nothing places it until the first click, and an edit made in that
    // state still has to be undoable (UndoManager.PushState used to bail out on a null caret paragraph).

    [AvaloniaFact]
    public void OneResizeDragIsOneUndoStep()
    {
        var host = Host(TableDoc());
        var tb = host.Editor.Document!.Blocks.OfType<TableBlock>().Single();
        var handle = host.ColumnHandles.First(h => h.colIndex == 0);
        double before = tb.ColumnWidths[0];

        var start = handle.rect.Center;
        host.Drag(start, start + new Point(15, 0), start + new Point(30, 0), start + new Point(45, 0));
        host.Editor.Undo();

        Assert.Equal(before, host.Editor.Document!.Blocks.OfType<TableBlock>().Single().ColumnWidths[0], 1);
    }
}
