using Avalonia;
using Avalonia.Headless;
using AvaloniaRichEditor.Tests.Render;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// Headless Avalonia is single-threaded; serialize the whole assembly (matches the main test project).
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace AvaloniaRichEditor.Tests.Render;

// Headless app that renders with REAL Skia (UseHeadlessDrawing = false) instead of the default no-op
// drawing, so RenderTargetBitmap.Render actually rasterizes glyphs and pixel assertions are possible.
// Inter is bundled and used as the default family so glyph shapes/sizes are deterministic across
// Windows/macOS/Linux (the OS default font would differ per platform).
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .WithInterFont();
}
