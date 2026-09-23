using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

// Reported from the demo (2026-09-20): a table's top outline came out cut at a page boundary — part of
// the line at the bottom of one page, the rest at the top of the next.
//
// A 1px pen is centred on the rect it strokes, so a cell on the table's edge puts half its line outside
// the table's own box. A page break lands on that box, and the page's clip cuts whatever is on the far
// side. Probed across filler lengths: with the break at 1024 the line came out at 67% of the weight it
// has when the break falls elsewhere. The fix pulls the table's OUTER edges half a pen inwards.
//
// Needs real glyph/line rasterisation, hence this project: the main suite's backend draws nothing.
public class TableAtPageTopRenderTests
{
    private static readonly FontFamily Inter = new("avares://Avalonia.Fonts.Inter/Assets#Inter");

    private const int W = 900;

    private static byte[] Render(RichEditor ed, int h)
    {
        ed.Measure(new Size(W, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, W, Math.Max(h, ed.DesiredSize.Height)));
        using var rtb = new RenderTargetBitmap(new PixelSize(W, h));
        rtb.Render(ed);
        var buf = new byte[W * 4 * h];
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try { rtb.CopyPixels(new PixelRect(0, 0, W, h), handle.AddrOfPinnedObject(), buf.Length, W * 4); }
        finally { handle.Free(); }
        return buf;
    }

    // How dark one row is INSIDE the paper. Two traps this walked into first: counting touched pixels
    // instead of weight (a half-clipped line covers the same columns, only lighter), and measuring the
    // full bitmap width (the desk beside the paper is grey and swamped every row with the same value).
    // Channel-agnostic — CopyPixels hands back the backend's layout, BGRA here and RGBA on macOS.
    private static long RowDarkness(byte[] px, int h, int y, int x0, int x1)
    {
        if (y < 0 || y >= h) return 0;
        long sum = 0;
        for (int x = x0; x < x1; x++)
        {
            int o = (y * W + x) * 4;
            int lightest = Math.Max(px[o], Math.Max(px[o + 1], px[o + 2]));
            if (lightest < 255) sum += 255 - lightest;
        }
        return sum;
    }

    // 48 filler paragraphs put the page break exactly on the table's top edge (found by sweeping the
    // count and watching where the line lost weight); 46 leaves the break elsewhere, as the control.
    private static RichEditor Editor(int fillerLines)
    {
        var ed = new RichEditor
        {
            PageSize = RichEditorPageSize.A4,
            DefaultFontFamily = Inter,
            ShowPageBoundaries = true,
        };
        string paras = string.Concat(Enumerable.Range(0, fillerLines).Select(i => $"<p>Filler line {i}</p>"));
        ed.LoadHtml(paras + "<table><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>");
        return ed;
    }

    // The table's top line, wherever the break falls, carries the weight of a whole line — measured
    // against the same document with the break somewhere else, so no absolute ink constant is baked in.
    [AvaloniaFact]
    public void ATableTopLineKeepsItsWeight_WhenThePageBreakLandsOnIt()
    {
        long OnPage2Top(int filler)
        {
            var ed = Editor(filler);
            Assert.Equal(2, ed.GetPrintPageCount());
            int paperH = (int)ed.GetPaperPixelSize().Height, paperW = (int)ed.GetPaperPixelSize().Width;
            int h = paperH * 2 + 40;
            var px = Render(ed, h);
            int left = (W - paperW) / 2;
            // Page 2's content box top: the desk gap, the first paper, the gap again, then the margin band.
            int contentTop = 3 + paperH + 3 + 40;
            return Enumerable.Range(contentTop - 3, 7)
                .Select(y => RowDarkness(px, h, y, left + 50, left + paperW - 50)).Max();
        }

        long onTheBreak = OnPage2Top(48);
        long elsewhere = OnPage2Top(46);

        Assert.True(onTheBreak >= elsewhere * 0.9,
            $"the table's top line carries {onTheBreak} of ink when the break lands on it, {elsewhere} when it does not");
    }
}
