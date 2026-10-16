using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

// The 1.3.0 audit (2026-10-17), on real Skia: sizes a document states past what layout can sum.
public class Release13RenderAuditTests
{
    private const string Png1x1 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    // Pasted HTML carried "Infinity" and "1e308" into picture sizes, line spacing and paragraph spacing (double.TryParse
    // reads both); summed, they made an infinite desired size, which Avalonia's layout pass throws on. Drawn here
    // unbounded, the way a ScrollViewer measures it, and rasterised by Skia.
    [AvaloniaTheory]
    [InlineData("<p>a<img src=\"data:image/png;base64," + Png1x1 + "\" width=\"Infinity\" height=\"Infinity\">b</p>")]
    [InlineData("<img src=\"data:image/png;base64," + Png1x1 + "\" width=\"1e308\" height=\"1e308\"><img src=\"data:image/png;base64," + Png1x1 + "\" width=\"1e308\" height=\"1e308\">")]
    [InlineData("<p style=\"line-height:Infinity\">a</p>")]
    [InlineData("<p data-are-m=\"1e308,1e308,0\">a</p><p data-are-m=\"1e308,1e308,0\">b</p>")]
    public void HugeSizesFromHtml_LayOutAndRasterise(string html)
    {
        var ed = new RichEditor();
        ed.LoadHtml(html);
        // Measured the way a ScrollViewer measures it (no height limit): an infinite desired size throws right here.
        ed.Measure(new Size(600, double.PositiveInfinity));
        Assert.True(double.IsFinite(ed.DesiredSize.Height));
        ed.Arrange(new Rect(0, 0, 600, 400));
        using var rtb = new RenderTargetBitmap(new PixelSize(600, 400));
        rtb.Render(ed);
    }
}
