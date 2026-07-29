using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// An inline table living in a paragraph INSIDE a table cell — what you get by pasting a paragraph that
// contains one into a cell. The renderer drew it (DrawCellBlockList flushes the inline-table draws), but
// HitTestBlockList had no inline-table descent, so a click stopped at the host paragraph's ObjChar: the
// table's cells could not be entered with the mouse or drag-selected (rule #1 — every walk must agree).
public class InlineTableInCellHitTests
{
    private static void Realize(RichEditor ed, double width = 800)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    private static TextPointer HitTest(RichEditor ed, Point p)
        => (TextPointer)typeof(RichEditor)
            .GetMethod("GetPositionFromPoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(ed, new object[] { p })!;

    // Every paragraph a click anywhere in the editor can land on. Sweeping is the honest way to ask
    // "is this reachable with the mouse at all?" without hard-coding the inline table's geometry.
    private static HashSet<Paragraph> ReachableParagraphs(RichEditor ed, double w, double h)
    {
        var seen = new HashSet<Paragraph>();
        for (double y = 1; y < h; y += 2)
            for (double x = 1; x < w; x += 2)
                if (HitTest(ed, new Point(x, y)).Paragraph is { } p) seen.Add(p);
        return seen;
    }

    // Cell -> paragraph("host") -> inline table -> its own cell paragraph.
    private static (RichEditor ed, Paragraph inlineCellPara, Paragraph hostPara) InlineTableInsideACell()
    {
        var outer = new TableBlock(1, 1);
        outer.ColumnWidths[0] = 400;

        var host = new Paragraph();
        host.Inlines.Add(new Run { Text = "before " });
        var it = new InlineTable { Table = new TableBlock(1, 2) };
        ((Run)it.Table.Cells[0][0].Para.Inlines[0]).Text = "A";
        ((Run)it.Table.Cells[0][1].Para.Inlines[0]).Text = "B";
        host.Inlines.Add(it);
        host.Inlines.Add(new Run { Text = " after" });

        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(host);

        var doc = new FlowDocument();
        doc.Blocks.Add(outer);
        var ed = new RichEditor { Document = doc };
        Realize(ed);
        return (ed, it.Table.Cells[0][0].Para, host);
    }

    [AvaloniaFact]
    public void ClickingIntoAnInlineTableInsideACell_ReachesItsCellParagraph()
    {
        var (ed, inlineCellPara, host) = InlineTableInsideACell();
        var reachable = ReachableParagraphs(ed, 800, ed.DesiredSize.Height);

        Assert.Contains(host, reachable);            // sanity: the host paragraph is clickable
        Assert.Contains(inlineCellPara, reachable);  // the inline table's own cell must be too
    }

    [AvaloniaFact]
    public void BothCellsOfAnInlineTableInsideACell_AreReachable()
    {
        var outer = new TableBlock(1, 1);
        outer.ColumnWidths[0] = 400;
        var host = new Paragraph();
        host.Inlines.Add(new Run { Text = "x " });
        var it = new InlineTable { Table = new TableBlock(1, 2) };
        ((Run)it.Table.Cells[0][0].Para.Inlines[0]).Text = "A";
        ((Run)it.Table.Cells[0][1].Para.Inlines[0]).Text = "B";
        host.Inlines.Add(it);
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(host);
        var doc = new FlowDocument();
        doc.Blocks.Add(outer);
        var ed = new RichEditor { Document = doc };
        Realize(ed);

        var reachable = ReachableParagraphs(ed, 800, ed.DesiredSize.Height);

        // Both cells reachable = a drag from one to the other can form a cell block.
        Assert.Contains(it.Table.Cells[0][0].Para, reachable);
        Assert.Contains(it.Table.Cells[0][1].Para, reachable);
    }

    // Guard: the same content at the top level already worked and must keep working.
    [AvaloniaFact]
    public void ATopLevelInlineTable_StaysReachable()
    {
        var host = new Paragraph();
        host.Inlines.Add(new Run { Text = "before " });
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        ((Run)it.Table.Cells[0][0].Para.Inlines[0]).Text = "A";
        host.Inlines.Add(it);
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };
        Realize(ed);

        Assert.Contains(it.Table.Cells[0][0].Para, ReachableParagraphs(ed, 800, ed.DesiredSize.Height));
    }
}
