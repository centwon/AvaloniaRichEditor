using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Two inline-table defects reported from the demo: the right-click menu offered no table operations
// inside one, and dragging its row height didn't reflow the host paragraph until the next edit.
public class InlineTableMenuAndResizeTests
{
    private static void Realize(RichEditor ed, double width = 400)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    private static T Invoke<T>(RichEditor ed, string method, params object[] args)
        => (T)typeof(RichEditor).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(ed, args)!;

    private static TextPointer Hit(RichEditor ed, double x, double y)
        => Invoke<TextPointer>(ed, "GetPositionFromPoint", new Point(x, y));

    // A top-level paragraph holding an inline table, plus the editor showing it.
    private static (RichEditor ed, Paragraph host, InlineTable it) InlineTableDoc(string cellText = "A")
    {
        var host = new Paragraph();
        host.Inlines.Add(new Run { Text = "before " });
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        ((Run)it.Table.Cells[0][0].Para.Inlines[0]).Text = cellText;
        host.Inlines.Add(it);
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Realize(ed);
        return (ed, host, it);
    }

    // ---- 1. right-click inside an inline table ------------------------------

    [AvaloniaFact]
    public void RightClickInsideAnInlineTable_TargetsThatTablesOperations()
    {
        var (ed, _, it) = InlineTableDoc();

        // Sweep for a point that resolves into the inline table's cell, then ask what the menu would
        // target there. GetBlockAtPoint can't see an inline table, so this used to come back null and
        // the menu was built with no table operations at all.
        TableBlock? target = null;
        for (double y = 2; y < ed.DesiredSize.Height && target == null; y += 2)
            for (double x = 2; x < 400 && target == null; x += 2)
                if (ReferenceEquals(Hit(ed, x, y).Paragraph, it.Table.Cells[0][0].Para))
                    target = Invoke<TableBlock?>(ed, "ContextMenuTargetTable", new Point(x, y));

        Assert.Same(it.Table, target);
    }

    // Clicking the host paragraph's own text is not "inside a table" — no table menu there.
    [AvaloniaFact]
    public void RightClickOnTheHostParagraphsText_TargetsNoTable()
    {
        var (ed, host, _) = InlineTableDoc();

        TableBlock? target = null;
        bool found = false;
        for (double y = 2; y < ed.DesiredSize.Height && !found; y += 2)
            if (ReferenceEquals(Hit(ed, 12, y).Paragraph, host))
            {
                found = true;
                target = Invoke<TableBlock?>(ed, "ContextMenuTargetTable", new Point(12, y));
            }

        Assert.True(found, "the host paragraph's text must be clickable");
        Assert.Null(target);
    }

    // ---- 2. resizing an inline table reflows its host paragraph -------------

    [AvaloniaFact]
    public void ResizingAnInlineTablesRow_ReflowsTheHostParagraphImmediately()
    {
        var (ed, _, it) = InlineTableDoc();
        double before = ed.DesiredSize.Height;

        // What the row-resize drag does to the model, then the invalidation it performs.
        it.Table.RowHeights.Clear();
        it.Table.RowHeights.Add(150);
        Invoke<object?>(ed, "InvalidateTableChain", it.Table);
        ed.InvalidateMeasure();
        ed.Measure(new Size(400, double.PositiveInfinity));

        Assert.True(ed.DesiredSize.Height > before,
            $"the host paragraph must grow with the table on the same frame ({ed.DesiredSize.Height} vs {before})");
    }

    // The same for a table nested inside an inline table's cell: the growth has to travel all the way
    // out to the host paragraph, not stop at the table that was dragged.
    [AvaloniaFact]
    public void ResizingATableNestedInsideAnInlineTable_ReflowsTheHostParagraph()
    {
        var host = new Paragraph();
        host.Inlines.Add(new Run { Text = "before " });
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        var inner = new TableBlock(1, 1);
        it.Table.Cells[0][0].Blocks.Clear();
        it.Table.Cells[0][0].Blocks.Add(inner);
        host.Inlines.Add(it);
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Realize(ed);
        double before = ed.DesiredSize.Height;

        inner.RowHeights.Clear();
        inner.RowHeights.Add(150);
        Invoke<object?>(ed, "InvalidateTableChain", inner);
        ed.InvalidateMeasure();
        ed.Measure(new Size(400, double.PositiveInfinity));

        Assert.True(ed.DesiredSize.Height > before,
            $"the growth must reach the host paragraph ({ed.DesiredSize.Height} vs {before})");
    }

    // A plain block table has no host paragraph; the chain walk must still work for it.
    [AvaloniaFact]
    public void ResizingATopLevelTablesRow_StillReflows()
    {
        var tb = new TableBlock(1, 1);
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Realize(ed);
        double before = ed.DesiredSize.Height;

        tb.RowHeights.Clear();
        tb.RowHeights.Add(150);
        Invoke<object?>(ed, "InvalidateTableChain", tb);
        ed.InvalidateMeasure();
        ed.Measure(new Size(400, double.PositiveInfinity));

        Assert.True(ed.DesiredSize.Height > before,
            $"({ed.DesiredSize.Height} vs {before})");
    }
}
