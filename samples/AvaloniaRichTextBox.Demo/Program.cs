using Avalonia;
using System;

namespace AvaloniaRichTextBox.Demo;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Headless HTML round-trip harness (Phase 0): AvaloniaRichTextBox.Demo.exe --roundtrip <inDir> [outDir]
        if (args.Length >= 2 && args[0] == "--roundtrip")
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            AvaloniaRichTextBox.Formatters.RoundTripHarness.Run(args[1], args.Length >= 3 ? args[2] : args[1]);
            return;
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
            .WithInterFont()
            .LogToTrace();
}
