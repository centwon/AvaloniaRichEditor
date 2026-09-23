using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

// Reported from the demo (2026-09-23): "the picture is cut by a pixel or two", seen at a high zoom.
//
// Nothing clipped it. A picture carries a faint always-on outline marking it as an object, and a pen is
// centred on the rect it strokes — so half of that line lay ON the picture, painting over its outermost
// half-pen on every side (measured before the fix: the picture kept 118.5 of its 120 rows of colour). At 1x
// it reads as a soft edge; at the zoom the report came from, as a band of border colour where the picture's
// own pixels should be. The bold selection border ate twice as much.
//
// Both outlines now sit half a pen OUTSIDE the picture's rect. A table's borders go the other way
// (InsetTableEdges): there the line IS the table's ink and must stay inside the box pagination knows about.
//
// Needs real rasterisation, hence this project: the main suite's backend draws nothing.
public class PictureOutlineRenderTests
{
    private const int W = 400, H = 300;
    private const int PicW = 320, PicH = 120;

    // A solid red 24-bit BMP, bottom-up.
    private static byte[] SolidBmp(int w, int h)
    {
        int stride = (w * 3 + 3) & ~3, size = 54 + stride * h;
        var bytes = new byte[size];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BitConverter.GetBytes(size).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(w).CopyTo(bytes, 18);
        BitConverter.GetBytes(h).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        BitConverter.GetBytes(stride * h).CopyTo(bytes, 34);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = 54 + y * stride + x * 3;
                bytes[o] = 0; bytes[o + 1] = 0; bytes[o + 2] = 255;
            }
        return bytes;
    }

    private static byte[] Render(bool selected)
    {
        var img = new ImageBlock { Width = PicW, Height = PicH, MarginTop = 0, MarginBottom = 0 };
        img.SetImageData(SolidBmp(PicW, PicH), "image/bmp");
        var doc = new FlowDocument();
        doc.Blocks.Add(img);
        var ed = new RichEditor { Document = doc };
        // Selecting a picture is a pointer gesture; the field behind it is private, so the test sets it the
        // way the other render tests reach internals — by reflection.
        if (selected)
            typeof(RichEditor).GetField("_selectedBlock", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(ed, img);

        ed.Measure(new Size(W, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, W, H));
        using var rtb = new RenderTargetBitmap(new PixelSize(W, H));
        rtb.Render(ed);
        var buf = new byte[W * 4 * H];
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try { rtb.CopyPixels(new PixelRect(0, 0, W, H), handle.AddrOfPinnedObject(), buf.Length, W * 4); }
        finally { handle.Free(); }
        return buf;
    }

    // CopyPixels hands back the backend's own channel layout — BGRA on Windows and Linux, RGBA on macOS — so
    // which of channel 0 and 2 holds the picture's red is found from a pixel of the picture, not assumed.
    private static int RedChannel(byte[] px)
    {
        for (int i = 0; i + 3 < px.Length; i += 4)
        {
            if (px[i] > 200 && px[i + 1] < 80 && px[i + 2] < 80) return 0;
            if (px[i + 2] > 200 && px[i + 1] < 80 && px[i] < 80) return 2;
        }
        return -1;
    }

    // How much of the picture a pixel holds, 0..1, as the distance between its red and green channels
    // against the picture's own. White (all channels equal) reads 0, so the page behind never counts; blue
    // border and accent handles read 0 or less; a half-covered edge pixel reads a half. A selected picture
    // wears a translucent accent wash by design, which the reference pixel carries too.
    private static double PictureIn(byte[] px, int x, int y, int red, double full)
    {
        int o = (y * W + x) * 4;
        return Math.Clamp((px[o + red] - px[o + 1]) / full, 0, 1);
    }

    // Measured on the SELECTED picture, whose border is 2 px and opaque: it took 2.6 rows of the picture's
    // colour before the fix and 0.9 after (the rest of that 0.9 is the picture's own antialiased fringe,
    // which any line laid against it darkens). The faint always-on outline is 1 px and 47% opaque, so the
    // same defect there moves this number by only 0.4 of a row — too little to assert on without a brittle
    // threshold — but it is the same rect, through the same Around() helper, drawn four lines away.
    [AvaloniaFact]
    public void TheOutlineAroundAPictureTouchesNoneOfItsPixels()
    {
        const bool selected = true;
        var px = Render(selected);
        int red = RedChannel(Render(selected: false));
        Assert.True(red >= 0, "the picture was not drawn at all");

        // The reference: the picture's own colour at its centre, wherever the control's padding put it.
        int cx = -1, cy = -1;
        for (int y = 0; y < H && cx < 0; y++)
            for (int x = 0; x < W; x++)
                if (px[(y * W + x) * 4 + red] - px[(y * W + x) * 4 + 1] > 60)
                { cx = x + PicW / 2; cy = y + PicH / 2; break; }
        Assert.True(cx > 0, "the picture was not drawn at all");
        double full = px[(cy * W + cx) * 4 + red] - px[(cy * W + cx) * 4 + 1];

        // How much of the picture survives, measured down a strip of it: every column from a little inside
        // the left edge to just short of the middle. Whole pixels and part-covered ones both count, so a soft
        // edge at a fractional position is not mistaken for loss — only colour actually replaced is. The
        // strip avoids the three resize handles (bottom-right corner, right edge, bottom middle), which do
        // sit ON a selected picture, the way Word and HWP draw them.
        int x0 = cx - PicW / 2, stripL = x0 + 20, stripR = x0 + PicW / 2 - 20;
        double area = 0;
        for (int y = 0; y < H; y++)
            for (int x = stripL; x < stripR; x++)
                area += PictureIn(px, x, y, red, full);

        double rows = area / (stripR - stripL);
        // Measured: 117.4 with the border on the picture's rect, 119.1 with it just outside.
        Assert.True(rows > PicH - 1.2,
            $"the picture keeps {rows:F2} of its {PicH} rows of colour — the outline is painting over it");
    }
}
