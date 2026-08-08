using Avalonia;
using System;

namespace AvaloniaRichEditor.Demo;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Headless HTML round-trip harness (Phase 0): AvaloniaRichEditor.Demo.exe --roundtrip <inDir> [outDir]
        if (args.Length >= 2 && args[0] == "--roundtrip")
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            RoundTripHarness.Run(args[1], args.Length >= 3 ? args[2] : args[1]);
            return;
        }

        // Builds the demo's sample document and prints what it contains, without opening a window.
        // The document is the demo's front page and the thing the README screenshots, so it is worth
        // being able to check that it still constructs, still decodes its pictures, and still survives
        // the formatters — none of which a successful compile tells you.
        if (args.Length >= 1 && args[0] == "--sample-check")
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            SampleDocumentCheck.Run();
            return;
        }

        // Performance measurement harness: AvaloniaRichEditor.Demo.exe --bench (image-heavy, N6-6),
        // --bench-text (large text documents, gate ③) or --bench-table (nested/inline tables + the IME
        // composition path, P4). Opens a real window, runs scripted scenarios, writes the results file,
        // exits.
        if (args.Length >= 1 && (args[0] == "--bench" || args[0] == "--bench-text" || args[0] == "--bench-table"))
        {
            BenchHarness.Enabled = true;
            BenchHarness.TextMode = args[0] == "--bench-text";
            BenchHarness.TableMode = args[0] == "--bench-table";
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            // Use the system UI font (Segoe UI on Windows) instead of bundling Inter: a Windows-targeted
            // desktop app doesn't need cross-platform font consistency, and dropping the embedded font
            // trims its memory/disk footprint. FluentTheme falls back to the system default font.
            .LogToTrace();
}
