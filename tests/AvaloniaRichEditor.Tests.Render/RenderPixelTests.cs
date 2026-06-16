using System;
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
// we can assert that something was actually drawn and that relative sizes hold. Assertions are
// STRUCTURAL (ink present / heading taller than body / a horizontal divider line), not golden-image
// comparisons — anti-aliasing and font rendering differ per platform, so exact pixels aren't portable;
// relative checks against a bundled deterministic font (Inter) are.
public class RenderPixelTests
{
    private const int W = 400, H = 240;
    private static readonly FontFamily Inter = new("avares://Avalonia.Fonts.Inter/Assets#Inter");

    private static RichEditor MakeEditor(string html)
    {
        var ed = new RichEditor
        {
            PageSize = RichEditorPageSize.Continuous, // no page chrome — text starts at the top-left
            DefaultFontFamily = Inter,                // deterministic glyphs across platforms
        };
        ed.LoadHtml(html);
        return ed;
    }

    // Renders the editor to a real bitmap and returns its BGRA pixels (top-down).
    private static byte[] Render(RichEditor ed)
    {
        ed.Measure(new Size(W, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, W, H));
        var rtb = new RenderTargetBitmap(new PixelSize(W, H));
        rtb.Render(ed);
        int stride = W * 4;
        var buf = new byte[stride * H];
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try { rtb.CopyPixels(new PixelRect(0, 0, W, H), handle.AddrOfPinnedObject(), buf.Length, stride); }
        finally { handle.Free(); }
        return buf;
    }

    // The control fills a transparent background, so any pixel with non-trivial alpha is drawn "ink".
    private static bool IsInk(byte[] bgra, int x, int y) => bgra[(y * W + x) * 4 + 3] > 40;

    private static int InkCount(byte[] bgra)
    {
        int n = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (IsInk(bgra, x, y)) n++;
        return n;
    }

    // Vertical extent (last - first row containing ink), i.e. how tall the drawn content is.
    private static int InkRowSpan(byte[] bgra)
    {
        int first = -1, last = -1;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (IsInk(bgra, x, y)) { if (first < 0) first = y; last = y; break; }
        return first < 0 ? 0 : last - first + 1;
    }

    [AvaloniaFact]
    public void RealSkia_ActuallyRastersGlyphs()
    {
        // The setup proof: with no-op drawing this is 0; with real Skia the text leaves ink.
        var px = Render(MakeEditor("<p>Hello world</p>"));
        Assert.True(InkCount(px) > 50, $"expected rasterized glyphs, got {InkCount(px)} ink pixels");
    }

    [AvaloniaFact]
    public void Heading_RastersTallerThanBody()
    {
        // C1 at the pixel level: a heading paragraph (render-time size, via SetHeading) draws taller
        // glyphs than the same text as body. Same characters ("Hg" = full cap height + a descender).
        var bodyEd = MakeEditor("<p>Hg</p>");
        int bodySpan = InkRowSpan(Render(bodyEd));

        var headingEd = MakeEditor("<p>Hg</p>");
        headingEd.FocusDocumentEnd();
        headingEd.SetHeading(1);
        int headingSpan = InkRowSpan(Render(headingEd));

        Assert.True(bodySpan > 0 && headingSpan > bodySpan,
            $"H1 ink should be taller than body ({headingSpan} vs {bodySpan})");
    }

    [AvaloniaFact]
    public void Divider_DrawsAHorizontalLine()
    {
        // A DividerBlock renders a horizontal rule: some row must carry a long continuous run of ink.
        var px = Render(MakeEditor("<hr/>"));
        int widestRun = 0;
        for (int y = 0; y < H; y++)
        {
            int run = 0, best = 0;
            for (int x = 0; x < W; x++)
            {
                if (IsInk(px, x, y)) { run++; best = Math.Max(best, run); }
                else run = 0;
            }
            widestRun = Math.Max(widestRun, best);
        }
        Assert.True(widestRun > 100, $"expected a horizontal divider line, widest ink run was {widestRun}px");
    }
}
