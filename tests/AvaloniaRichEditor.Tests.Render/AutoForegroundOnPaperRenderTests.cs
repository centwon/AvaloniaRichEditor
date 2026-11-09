using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

// A dark-theme host's light DefaultForeground is for its own canvas: on the white paper — page view with page boundaries,
// the print page — automatic text is black, or it came out faint (2026-11-09, as the WinUI control). A run's own colour stays.
public class AutoForegroundOnPaperRenderTests
{
    private static RichEditor Editor(RichEditorViewMode mode)
    {
        var doc = new FlowDocument { PageSetup = new PageSetup { PageSize = RichEditorPageSize.A4 } };
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Inlines = {
            new Run { Text = "WWWW", FontSize = 40 },
            new Run { Text = "WWWW", FontSize = 40, Foreground = new ImmutableSolidColorBrush(Color.FromRgb(255, 0, 0)) } } });
        return new RichEditor { Document = doc, ViewMode = mode, DefaultForeground = new ImmutableSolidColorBrush(Colors.Lime) };
    }

    private static bool Limeish(byte b, byte g, byte r, byte a) => a > 200 && g > 180 && r < 80 && b < 80;
    private static bool Blackish(byte b, byte g, byte r, byte a) => a > 200 && r < 60 && g < 60 && b < 60;
    private static bool Reddish(byte b, byte g, byte r, byte a) => a > 200 && r > 180 && g < 80 && b < 80;

    private static int Count(byte[] px, Func<byte, byte, byte, byte, bool> test)
    {
        int n = 0;
        for (int o = 0; o + 3 < px.Length; o += 4) if (test(px[o], px[o + 1], px[o + 2], px[o + 3])) n++;
        return n;
    }

    private static byte[] Pixels(Bitmap bmp)
    {
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        var buf = new byte[w * 4 * h];
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try { bmp.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), buf.Length, w * 4); }
        finally { handle.Free(); }
        return buf;
    }

    private static byte[] Screen(RichEditor ed, double w = 900)
    {
        // Tall enough for a second line: on macOS the default font is wider and the red run wraps onto it in page view.
        const int h = 600;
        ed.Measure(new Size(w, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, w, h));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(w), h));
        rtb.Render(ed);
        return Pixels(rtb);
    }

    [AvaloniaFact]
    public void OnWhitePaper_AutomaticTextIsBlack_AndAnExplicitColourStays()
    {
        var canvas = Screen(Editor(RichEditorViewMode.Continuous)); // the host's colour on its own canvas
        Assert.True(Count(canvas, Limeish) > 100);
        Assert.Equal(0, Count(canvas, Blackish));

        var ed = Editor(RichEditorViewMode.Page);
        var page = Screen(ed);
        Assert.True(Count(page, Blackish) > 100);
        Assert.Equal(0, Count(page, Limeish));
        Assert.True(Count(page, Reddish) > 100);

        using var printed = ed.RenderPrintPage(0);
        var print = Pixels(printed);
        Assert.True(Count(print, Blackish) > 100);
        Assert.Equal(0, Count(print, Limeish));
        Assert.True(Count(print, Reddish) > 100);

        // A canvas as wide as the paper's text column: the screen's and the print's layouts share a wrap width, so only the
        // colour tells the cached ones apart — the print must not reuse the screen's, nor the screen the print's.
        var same = Editor(RichEditorViewMode.Continuous);
        same.UseLayoutRounding = false; // the paper's column is fractional (mm); rounding would miss it
        Screen(same);
        double w = same.PaperContentWidth + 900 - same.ContentLayoutWidth;
        var before = Screen(same, w);
        Assert.Equal(same.PaperContentWidth, same.ContentLayoutWidth, 3);
        Assert.Equal(0, Count(before, Blackish));
        using var printedSame = same.RenderPrintPage(0);
        Assert.True(Count(Pixels(printedSame), Blackish) > 100);
        var after = Screen(same, w);
        Assert.True(Count(after, Limeish) > 100);
        Assert.Equal(0, Count(after, Blackish));
    }
}
