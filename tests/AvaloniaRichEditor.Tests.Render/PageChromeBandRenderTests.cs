using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

// The header, the footer and the page number are drawn in the paper's margin bands. That band was a
// constant 40 DIP until the margins became a document setting (2026-09-20), so the line always fitted.
// With a 12 DIP band an 11pt line is centred from -1 to 13: off the paper onto the desk at one end, over
// the body text at the other. Found in the demo, by eye, on the first hands-on run of the feature.
//
// Decision: a band too thin for the line is left empty, so the margins stay exactly what was asked for.
// This needs real glyphs — the main project's no-op backend draws no text to measure.
public class PageChromeBandRenderTests
{
    private static readonly FontFamily Inter = new("avares://Avalonia.Fonts.Inter/Assets#Inter");

    private const int W = 900, H = 320;

    private static byte[] Render(RichEditor ed)
    {
        ed.Measure(new Size(W, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, W, Math.Max(H, ed.DesiredSize.Height)));
        using var rtb = new RenderTargetBitmap(new PixelSize(W, H));
        rtb.Render(ed);
        int stride = W * 4;
        var buf = new byte[stride * H];
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try { rtb.CopyPixels(new PixelRect(0, 0, W, H), handle.AddrOfPinnedObject(), buf.Length, stride); }
        finally { handle.Free(); }
        return buf;
    }

    private static RichEditor PagedEditor(Thickness margin)
    {
        var ed = new RichEditor
        {
            PageSize = RichEditorPageSize.A4,
            DefaultFontFamily = Inter,
            PageHeader = "HEADER TEXT",
            ShowPageNumbers = true,
            PageMargin = margin,
        };
        ed.LoadHtml("<p>body</p>");
        return ed;
    }

    // Anything drawn on the paper: the chrome is grey (128) and the body black, both far from white.
    // Channel-agnostic, because CopyPixels hands back the backend's own layout (BGRA here, RGBA on macOS).
    private static int MarkedPixels(byte[] px, int y0, int y1, int x0, int x1)
    {
        int n = 0;
        for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
            {
                int o = (y * W + x) * 4;
                if (px[o] < 200 && px[o + 1] < 200 && px[o + 2] < 200) n++;
            }
        return n;
    }

    // Paper geometry in view coordinates: the desk gap above the first page, and the page centred in the control.
    private const int PaperTop = 3;                         // RichEditor.PageGap
    private static int PaperLeft(RichEditor ed) => (int)Math.Max(0, (W - ed.GetPaperPixelSize().Width) / 2);
    private static int PaperRight(RichEditor ed) => PaperLeft(ed) + (int)ed.GetPaperPixelSize().Width;

    [AvaloniaFact]
    public void AHeaderIsDrawnInTheBand_WhenItFits()
    {
        var ed = PagedEditor(new Thickness(48, 40, 48, 40)); // the default band, 40 DIP

        var px = Render(ed);

        // Inside the band, clear of the page border (1px) and of the body, which starts at the band's end.
        int inBand = MarkedPixels(px, PaperTop + 2, PaperTop + 38, PaperLeft(ed) + 2, PaperRight(ed) - 2);
        Assert.True(inBand > 20, $"the header should be drawn in the 40 DIP band, found {inBand} marked pixels");
    }

    [AvaloniaFact]
    public void ABandTooThinForTheLine_IsLeftEmpty()
    {
        var ed = PagedEditor(new Thickness(16, 12, 16, 12));

        var px = Render(ed);

        int inBand = MarkedPixels(px, PaperTop + 2, PaperTop + 12, PaperLeft(ed) + 2, PaperRight(ed) - 2);
        Assert.True(inBand == 0, $"a 12 DIP band cannot hold an 11pt line, but {inBand} pixels were drawn in it");
    }

    // The defect's other half — the line also started ABOVE the paper, on the desk — has no test: the
    // strip above the paper is 3 px (the desk gap) and the overhang was 1 px of LINE BOX, not of ink, so
    // the check passed with the fix reverted. A test that cannot fail is worse than none. The claim still
    // holds by construction: a line that fits its band and is centred in it cannot leave the paper.
}
