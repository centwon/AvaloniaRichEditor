using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaRichEditor;
using AvaloniaRichEditor.Controls;

namespace AvaloniaRichEditor.Demo.Views;

// Print preview (P-milestone Phase 3): pages rendered by the library's RenderPrintPage at screen
// DPI, stacked on a grey desk. Print/PDF buttons arrive with Phase 4 — this window is view-only.
internal class PrintPreviewWindow : Window
{
    public PrintPreviewWindow(RichEditor editor)
    {
        int pages = editor.GetPrintPageCount();
        Title = $"{RichEditorLocalization.GetString("Demo.PrintPreview")} — {pages}p";
        Width = 900;
        Height = 1000;
        Background = new SolidColorBrush(Color.Parse("#9E9E9E"));

        var stack = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        for (int i = 0; i < pages; i++)
        {
            stack.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.Parse("#CFCFCF")),
                BorderThickness = new Thickness(1),
                BoxShadow = BoxShadows.Parse("0 1 10 0 #40000000"),
                Child = new Image
                {
                    Source = editor.RenderPrintPage(i),
                    Width = 794,  // A4 @96DPI, matching the library's page metrics
                    Height = 1123
                }
            });
        }
        Content = new ScrollViewer { Content = stack };
    }
}
