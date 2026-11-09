using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

// Drawing a table with the mouse: the preview showed one plain rectangle, where the WinUI control draws the picked grid's
// cell lines inside it (and a "rows × cols" label), so the user sees the table that will go in (2026-11-09).
public class TableDrawPreviewRenderTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int W = 600, H = 400;

    [AvaloniaFact]
    public void ThePreview_DrawsTheCellLines()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>a</p>");
        ed.Measure(new Size(W, H));
        ed.Arrange(new Rect(0, 0, W, H));
        typeof(RichEditor).GetMethod("BeginTableDraw", NP)!.Invoke(ed, new object[] { 2, 3 });
        // A 300 × 200 band from (20, 40): columns every 100, the row line at 140.
        typeof(RichEditor).GetField("_tableDrawStart", NP)!.SetValue(ed, new Point(20, 40));
        typeof(RichEditor).GetField("_tableDrawCurrent", NP)!.SetValue(ed, new Point(320, 240));

        using var rtb = new RenderTargetBitmap(new PixelSize(W, H));
        rtb.Render(ed);
        var px = new byte[W * H * 4];
        var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
        try { rtb.CopyPixels(new PixelRect(0, 0, W, H), handle.AddrOfPinnedObject(), px.Length, W * 4); }
        finally { handle.Free(); }

        // Alpha (BGRA; the bitmap has no background): the translucent fill is faint, a grid line on it clearly more opaque.
        int Alpha(int x, int y) => px[(y * W + x) * 4 + 3];
        int Strongest(int x0, int x1, int y0, int y1)
        {
            int m = 0;
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) m = Math.Max(m, Alpha(x, y));
            return m;
        }
        int fill = Alpha(70, 190);                     // inside a cell, away from every line
        Assert.True(fill > 0, "no preview band at all — vacuous");
        Assert.True(Strongest(118, 122, 180, 200) > fill + 40, "no column line at x = 120");
        Assert.True(Strongest(218, 222, 180, 200) > fill + 40, "no column line at x = 220");
        Assert.True(Strongest(60, 80, 138, 142) > fill + 40, "no row line at y = 140");
    }
}
