using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AvaloniaRichEditor.Controls;

// Minimal modal text-input dialog (OK/Cancel). Returns the entered text, or null on cancel.
internal static class InputDialog
{
    public static async Task<string?> ShowAsync(Window owner, string title, string initial)
    {
        var box = new TextBox { Text = initial, PlaceholderText = "https://...", Width = 320 };
        string? result = null;

        var ok = new Button { Content = RichEditorLocalization.GetString("OK"), IsDefault = true };
        var cancel = new Button { Content = RichEditorLocalization.GetString("Cancel"), IsCancel = true };

        var dialog = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        ok.Click += (_, _) => { result = box.Text; dialog.Close(); };
        cancel.Click += (_, _) => { result = null; dialog.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog(owner);
        return result;
    }
}
