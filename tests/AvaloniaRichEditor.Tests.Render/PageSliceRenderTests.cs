using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

// A table that does not fit at the bottom of a page moves to the next one and leaves room on the first. Selected, it was
// still drawn on the first page — its frame across the room and its top edge as a 1-px line at the slice's end
// (2026-11-08): a selected block was drawn on every page, and one starting exactly at the slice's end counted as on it.
public class PageSliceRenderTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int W = 900, H = 1300;

    [AvaloniaFact]
    public void ASelectedTableThatMoved_LeavesTheRoomBlank()
    {
        var ed = new RichEditor { ViewMode = RichEditorViewMode.Page, PageSize = RichEditorPageSize.A4 };
        var filler = new TableBlock(1, 1);
        filler.RowHeights.Clear(); filler.RowHeights.Add(ed.PaperContentHeight - 200);
        var moved = new TableBlock(1, 2);
        moved.RowHeights.Clear(); moved.RowHeights.Add(300);
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(filler);
        doc.Blocks.Add(moved);
        ed.Document = doc;
        ed.Measure(new Size(W, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, W, Math.Max(H, ed.DesiredSize.Height)));
        typeof(RichEditor).GetField("_selectedBlock", NP)!.SetValue(ed, moved);

        var breaks = (List<double>)typeof(RichEditor).GetMethod("EnsurePageBreaks", NP)!.Invoke(ed, null)!;
        Assert.True(breaks.Count >= 2, "the second table should start a second page");
        var sliceEnd = ed.MapDocToView(new Point(10, breaks[1] - 0.01));            // page 1, view = control at 100 %
        double contentTop = (double)typeof(RichEditor).GetMethod("ContentTopView", NP)!.Invoke(ed, [0])!;
        double roomBottom = contentTop + ed.PaperContentHeight;
        Assert.True(roomBottom - sliceEnd.Y > 40, $"page 1's room {roomBottom - sliceEnd.Y} is too small to look at");

        using var rtb = new RenderTargetBitmap(new PixelSize(W, H));
        rtb.Render(ed);
        int stride = W * 4;
        var px = new byte[stride * H];
        var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
        try { rtb.CopyPixels(new PixelRect(0, 0, W, H), handle.AddrOfPinnedObject(), px.Length, stride); }
        finally { handle.Free(); }

        int Ink(double y0, double y1)
        {
            int n = 0;
            for (int y = (int)y0; y < (int)y1; y++)
                for (int x = (int)sliceEnd.X + 2; x < (int)(sliceEnd.X + ed.PaperContentWidth) - 12; x++)
                {
                    int o = (y * W + x) * 4;
                    if (px[o] < 240 || px[o + 1] < 240 || px[o + 2] < 240) n++;
                }
            return n;
        }
        Assert.True(Ink(contentTop, contentTop + 100) > 100, "page 1 drew nothing — vacuous");
        Assert.Equal(0, Ink(sliceEnd.Y - 3, roomBottom - 2)); // from just above the slice's end to the content box's bottom
    }
}
