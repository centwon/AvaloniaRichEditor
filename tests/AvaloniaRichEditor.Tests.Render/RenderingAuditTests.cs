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

// Audit of RichEditor.Rendering.cs (2026-09-23). Real Skia: a picture that fails to decode, and what the
// renderer paints, can only be seen with a live codec and rasteriser.
public class RenderingAuditTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int W = 400, H = 300;

    private static byte[] Pixels(RichEditor ed)
    {
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

    // A picture whose bytes do not decode drew nothing — and did not advance the walk either, so everything
    // after it was drawn its height too high, over where the picture should be, while clicks and the caret
    // (which measure it) stayed where they belong.
    [AvaloniaFact]
    public void APictureThatDoesNotDecode_StillTakesItsHeight()
    {
        var img = new ImageBlock { Width = 200, Height = 150, MarginTop = 0, MarginBottom = 0 };
        img.SetImageData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, "image/png");
        var after = new Paragraph { Inlines = { new Run { Text = "after" } } };
        var doc = new FlowDocument();
        doc.Blocks.Add(img);
        doc.Blocks.Add(after);
        var ed = new RichEditor { Document = doc };
        typeof(RichEditor).GetField("_caretPosition", NP)!.SetValue(ed, new TextPointer(after, 0));

        Pixels(ed);

        var caret = (Point)typeof(RichEditor).GetField("_lastCaretPoint", NP)!.GetValue(ed)!;
        Assert.True(caret.Y >= 150, $"the paragraph after a 150px picture was drawn at y={caret.Y}");
    }

    // The raster PDF fallback read every page's pixels as BGRA. Skia hands back RGBA on macOS (the lesson from
    // round 33's CI), so there a red page came out blue. The bitmap's own format decides now.
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void RasterPdfPixels_FollowTheBitmapsChannelOrder(bool rgba)
    {
        var fmt = rgba ? Avalonia.Platform.PixelFormat.Rgba8888 : Avalonia.Platform.PixelFormat.Bgra8888;
        using var wb = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), fmt, Avalonia.Platform.AlphaFormat.Opaque);
        using (var fb = wb.Lock())
        {
            var red = rgba ? new byte[] { 255, 0, 0, 255 } : new byte[] { 0, 0, 255, 255 };
            for (int i = 0; i < 4; i++)
                Marshal.Copy(red, 0, fb.Address + (i / 2) * fb.RowBytes + (i % 2) * 4, 4);
        }

        var (_, _, rgb) = ((int, int, byte[]))typeof(RichEditor)
            .GetMethod("BitmapToRgb24", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { wb })!;

        Assert.Equal(new byte[] { 255, 0, 0 }, rgb[..3]);
    }

    // Brushes.Silver, the quote bar's colour — distinct from the table's gray border (128) and the text.
    private static bool IsSilver(byte[] px, int x, int y)
    {
        int o = (y * W + x) * 4;
        return Math.Abs(px[o] - 192) < 6 && Math.Abs(px[o + 1] - 192) < 6 && Math.Abs(px[o + 2] - 192) < 6;
    }

    private static int SilverPixels(byte[] px)
    {
        int n = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (IsSilver(px, x, y)) n++;
        return n;
    }

    // Quote on a paragraph in a table cell changed nothing on screen: the cell walk drew no bar (nor a
    // paragraph background), so 인용 in a cell looked like it did nothing.
    [AvaloniaFact]
    public void AQuoteInACell_DrawsItsBar()
    {
        var tb = new TableBlock(1, 1) { MarginTop = 0 };
        tb.ColumnWidths[0] = 300;
        tb.Cells[0][0].Para.Inlines.Clear();
        tb.Cells[0][0].Para.Inlines.Add(new Run { Text = "quoted" });
        tb.Cells[0][0].Para.IsQuote = true; // no indent, as ToggleQuote leaves it
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);

        Assert.True(SilverPixels(Pixels(new RichEditor { Document = doc })) > 10, "no quote bar in the cell");
    }

    // An EMPTY paragraph in a quote broke the bar: the empty-paragraph path returned before drawing it.
    [AvaloniaFact]
    public void AnEmptyQuotedParagraph_KeepsTheBar()
    {
        Paragraph Q(string t) => new() { IsQuote = true, Inlines = { new Run { Text = t } } };
        var doc = new FlowDocument();
        doc.Blocks.Add(Q("first"));
        doc.Blocks.Add(Q(""));
        doc.Blocks.Add(Q("third"));
        var px = Pixels(new RichEditor { Document = doc });

        // The bar's column, and its run of rows from the top of the first to the bottom of the third.
        int bx = -1, top = -1, bottom = -1;
        for (int x = 0; x < W && bx < 0; x++)
            for (int y = 0; y < H; y++)
                if (IsSilver(px, x, y)) { bx = x; break; }
        Assert.True(bx >= 0, "no quote bar at all");
        for (int y = 0; y < H; y++)
            if (IsSilver(px, bx, y)) { if (top < 0) top = y; bottom = y; }
        // A gap is nothing painted: the control has no background, so alpha 0 (index 3 in both BGRA and RGBA).
        // Where two paragraphs' bars meet at a fractional y the seam row is only part-covered (it is there between
        // any two quoted lines, empty or not), which is no gap. Before the fix the empty line's rows read 0.
        for (int y = top; y <= bottom; y++)
            Assert.True(px[(y * W + bx) * 4 + 3] > 50, $"the quote bar has a gap at y={y}");
    }
}
