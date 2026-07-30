using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
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
    // The Fluent theme is here for one reason: a popup (context menu, toolbar flyout) can only open
    // into the overlay layer that lives in the Window template, and an untemplated window has none, so
    // every right-click threw "Unable to create IPopupImpl and no overlay layer is found".
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .AfterSetup(b => ((Application)b.Instance!).Styles.Add(new FluentTheme()));
}
