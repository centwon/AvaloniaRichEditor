using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using AvaloniaRichEditor.Demo.ViewModels;
using AvaloniaRichEditor.Demo.Views;

namespace AvaloniaRichEditor.Demo;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Swap the editor chrome's text glyphs for Fluent icons (before the first toolbar builds).
        FluentIconProvider.Install();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = BenchHarness.Enabled
                ? new BenchWindow()
                : new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
        }

        base.OnFrameworkInitializationCompleted();
    }
}