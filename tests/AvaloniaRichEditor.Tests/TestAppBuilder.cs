using Avalonia;
using Avalonia.Headless;
using AvaloniaRichEditor.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// Avalonia's headless app/dispatcher is single-threaded; running test collections in parallel races
// the platform initialization. Serialize the whole assembly.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace AvaloniaRichEditor.Tests;

// Headless Avalonia app used by [AvaloniaFact] tests so control-level code (caret, editing, undo,
// layout invalidation) runs on a real UI thread without a display.
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
