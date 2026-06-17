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

// 1.0 gate ①: render PIXEL tests. The main test project runs with no-op drawing (fast, but the bitmap
// is blank), so it can only assert geometry/PixelSize. Here the real Skia backend rasterizes glyphs, so
// we can assert that something was actually drawn and that relative sizes/positions/colours hold.
// Assertions are STRUCTURAL (ink present / heading taller than body / a divider line / page-2 content /
// a selection highlight), not golden-image comparisons — anti-aliasing and font rendering differ per
// platform, so exact pixels aren't portable; relative checks against a bundled font (Inter) are.
public class RenderPixelTests
{
    private static readonly FontFamily Inter = new("avares://Avalonia.Fonts.Inter/Assets#Inter");

    private static RichEditor MakeEditor(string html, RichEditorPageSize size = RichEditorPageSize.Continuous)
    {
        var ed = new RichEditor { PageSize = size, DefaultFontFamily = Inter };
        ed.LoadHtml(html);
        return ed;
    }

    // Renders the editor to a real w×h bitmap and returns its BGRA pixels (top-down, premultiplied).
    private static byte[] Render(RichEditor ed, int w, int h)
    {
        ed.Measure(new Size(w, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, w, Math.Max(h, ed.DesiredSize.Height)));
        var rtb = new RenderTargetBitmap(new PixelSize(w, h));
        rtb.Render(ed);
        int stride = w * 4;
        var buf = new byte[stride * h];
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try { rtb.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), buf.Length, stride); }
        finally { handle.Free(); }
        return buf;
    }

    private static (byte b, byte g, byte r, byte a) Px(byte[] bgra, int w, int x, int y)
    {
        int i = (y * w + x) * 4;
        return (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]);
    }

    // Transparent-background modes: any pixel with non-trivial alpha is drawn "ink".
    private static bool IsInk(byte[] bgra, int w, int x, int y) => Px(bgra, w, x, y).a > 40;

    // Page view paints an opaque grey desk + white paper, so alpha is useless there; near-black pixels
    // are the text glyphs (desk 158, paper 255, page border ~128 are all well above this).
    private static bool IsDarkText(byte[] bgra, int w, int x, int y)
    {
        var (b, g, r, _) = Px(bgra, w, x, y);
        return r < 100 && g < 100 && b < 100;
    }

    private static int InkCount(byte[] bgra, int w, int h)
    {
        int n = 0;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (IsInk(bgra, w, x, y)) n++;
        return n;
    }

    // Vertical extent (last - first row containing ink): how tall the drawn content is.
    private static int InkRowSpan(byte[] bgra, int w, int h)
    {
        int first = -1, last = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (IsInk(bgra, w, x, y)) { if (first < 0) first = y; last = y; break; }
        return first < 0 ? 0 : last - first + 1;
    }

    private static bool AnyDarkTextInBand(byte[] bgra, int w, int y0, int y1)
    {
        for (int y = y0; y < y1; y++) for (int x = 0; x < w; x++) if (IsDarkText(bgra, w, x, y)) return true;
        return false;
    }

    // Chromatic (coloured) pixels — one channel well above another. The accent selection highlight is
    // strongly coloured; black text, white paper, and the grey desk are all neutral (channels ≈ equal),
    // so this isolates the highlight. Uses the max−min spread, so it's independent of channel order
    // (RTB byte order can differ across platforms — a blue-vs-red test failed on macOS for that reason).
    private static int ColouredCount(byte[] bgra, int w, int h)
    {
        int n = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                int max = Math.Max(bgra[i], Math.Max(bgra[i + 1], bgra[i + 2]));
                int min = Math.Min(bgra[i], Math.Min(bgra[i + 1], bgra[i + 2]));
                if (bgra[i + 3] > 20 && max - min > 40) n++;
            }
        return n;
    }

    [AvaloniaFact]
    public void RealSkia_ActuallyRastersGlyphs()
    {
        // The setup proof: with no-op drawing this is 0; with real Skia the text leaves ink.
        var px = Render(MakeEditor("<p>Hello world</p>"), 400, 240);
        Assert.True(InkCount(px, 400, 240) > 50, $"expected rasterized glyphs, got {InkCount(px, 400, 240)} ink pixels");
    }

    [AvaloniaFact]
    public void Heading_RastersTallerThanBody()
    {
        // C1 at the pixel level: a heading paragraph (render-time size, via SetHeading) draws taller
        // glyphs than the same text as body. Same characters ("Hg" = full cap height + a descender).
        int bodySpan = InkRowSpan(Render(MakeEditor("<p>Hg</p>"), 400, 240), 400, 240);

        var headingEd = MakeEditor("<p>Hg</p>");
        headingEd.FocusDocumentEnd();
        headingEd.SetHeading(1);
        int headingSpan = InkRowSpan(Render(headingEd, 400, 240), 400, 240);

        Assert.True(bodySpan > 0 && headingSpan > bodySpan,
            $"H1 ink should be taller than body ({headingSpan} vs {bodySpan})");
    }

    [AvaloniaFact]
    public void Divider_DrawsAHorizontalLine()
    {
        // A DividerBlock renders a horizontal rule: some row must carry a long continuous run of ink.
        var px = Render(MakeEditor("<hr/>"), 400, 240);
        int widestRun = 0;
        for (int y = 0; y < 240; y++)
        {
            int run = 0, best = 0;
            for (int x = 0; x < 400; x++)
            {
                if (IsInk(px, 400, x, y)) { run++; best = Math.Max(best, run); }
                else run = 0;
            }
            widestRun = Math.Max(widestRun, best);
        }
        Assert.True(widestRun > 100, $"expected a horizontal divider line, widest ink run was {widestRun}px");
    }

    [AvaloniaFact]
    public void PageView_RendersContentOnSecondPage()
    {
        // The riskiest render path P5 reworked: the page-stack replay draws each page's document slice
        // under its own clip+translation. Verify content actually lands on page 2 — if the replay were
        // broken, page 2 would be blank. (A5 keeps the two-page bitmap a manageable size.)
        var html = string.Concat(Enumerable.Range(0, 60).Select(i => $"<p>Line {i} of the document</p>"));
        var ed = MakeEditor(html, RichEditorPageSize.A5);
        Assert.True(ed.GetPrintPageCount() >= 2, "test setup: content should span more than one page");

        int paperH = (int)ed.GetPaperPixelSize().Height; // A5 portrait ≈ 794
        int w = 700, h = paperH * 2 + 30;
        var px = Render(ed, w, h);

        Assert.True(AnyDarkTextInBand(px, w, 0, paperH), "page 1 should carry text");
        Assert.True(AnyDarkTextInBand(px, w, paperH + 10, h), "page 2 should carry text (page-stack replay)");
    }

    // Rightmost x with ink within the row band [y0, y1).
    private static int RightmostInk(byte[] bgra, int w, int h, int y0, int y1)
    {
        int maxX = -1;
        for (int y = y0; y < Math.Min(y1, h); y++)
            for (int x = w - 1; x > maxX; x--)
                if (IsInk(bgra, w, x, y)) { maxX = x; break; }
        return maxX;
    }

    private static int FirstInkRow(byte[] bgra, int w, int h)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (IsInk(bgra, w, x, y)) return y;
        return -1;
    }

    [AvaloniaFact]
    public void Justify_StretchesNonLastLineToFullWidth()
    {
        // The risk for justify is whether Avalonia 12's TextLayout actually HONOURS it. Decisive check:
        // render a wrapping paragraph left-aligned vs justified — if justify were ignored the two bitmaps
        // would be identical. They must differ, and the justified first (non-last) line must reach
        // further right (filled to the margin) than the ragged left-aligned one.
        const string text = "<p>The quick brown fox jumps over the lazy dog and then keeps running " +
                            "across the wide green field under the bright morning sky every single day</p>";
        int w = 400, h = 240;

        var leftPx = Render(MakeEditor(text), w, h);

        var jEd = MakeEditor(text);
        jEd.FocusDocumentEnd();
        jEd.SetTextAlignment(TextAlignment.Justify);
        var jPx = Render(jEd, w, h);

        int diff = 0;
        for (int i = 0; i < leftPx.Length; i++) if (leftPx[i] != jPx[i]) diff++;
        Assert.True(diff > 200, $"justify should change the rendering vs left-aligned (Avalonia must honour it); differing bytes={diff}");

        // Justify pulls the ragged non-last lines out to the margin, so the total rightward reach
        // (summed over every text row) is larger than left-aligned, where most lines stop short.
        long leftReach = 0, justReach = 0;
        for (int y = 0; y < h; y++)
        {
            int lr = RightmostInk(leftPx, w, h, y, y + 1); if (lr > 0) leftReach += lr;
            int jr = RightmostInk(jPx, w, h, y, y + 1); if (jr > 0) justReach += jr;
        }
        Assert.True(justReach > leftReach, $"justified text should reach further right overall ({justReach} vs {leftReach})");
        Assert.True(RightmostInk(jPx, w, h, FirstInkRow(jPx, w, h), h) > w * 0.8,
            "justified text should fill near the full content width");
    }

    [AvaloniaFact]
    public void Selection_DrawsHighlightPixels()
    {
        // Selecting text paints the accent (coloured) highlight behind it; an unselected render has none.
        int colNone = ColouredCount(Render(MakeEditor("<p>Hello world</p>"), 400, 240), 400, 240);

        var selected = MakeEditor("<p>Hello world</p>");
        Assert.True(selected.FindNext("Hello world", matchCase: false)); // public way to select a range
        int colSel = ColouredCount(Render(selected, 400, 240), 400, 240);

        Assert.True(colNone < 10, $"no selection should paint ~no coloured pixels, got {colNone}");
        Assert.True(colSel > 100, $"selection should paint a coloured highlight, got {colSel}");
    }
}
