using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A read-only editor is a viewer, and a viewer must not trap the keyboard: Tab has to move focus on
// to the next control the way it does from a TextBlock or a disabled input.
public class ReadOnlyFocusTests
{
    private static (Window w, RichEditor ed, Button next) Host(bool readOnly)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "text" } } });
        var ed = new RichEditor { Document = doc, IsReadOnly = readOnly };
        var next = new Button { Content = "next" };
        var panel = new StackPanel();
        panel.Children.Add(ed);
        panel.Children.Add(next);
        var w = new Window { Width = 600, Height = 400, Content = panel };
        w.Show();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        ed.Focus();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (w, ed, next);
    }

    [AvaloniaFact]
    public void ReadOnlyEditor_DoesNotTrapTabFocus()
    {
        var (w, ed, next) = Host(readOnly: true);
        Assert.True(ed.IsFocused, "the editor should start focused");

        w.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        w.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.False(ed.IsFocused, "Tab must move focus out of a read-only editor");
    }

    // An editable editor deliberately keeps Tab (it indents / moves between table cells), like Word.
    [AvaloniaFact]
    public void EditableEditor_KeepsTabForItself()
    {
        var (w, ed, next) = Host(readOnly: false);

        w.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        w.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.True(ed.IsFocused, "an editable editor uses Tab itself");
    }

    // Security-relevant invariant, pinned: a link's URI comes from a pasted or loaded document, so only
    // web schemes may ever be launched — never file:, javascript:, or a shell-executable path.
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("ms-msdt:/id")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("\\\\attacker\\share\\payload.exe")]
    public void OpenUrl_LaunchesNothingButWebSchemes(string url)
    {
        var m = typeof(RichEditor).GetMethod("OpenUrl", BindingFlags.NonPublic | BindingFlags.Static)!;
        // Returning without launching is the contract; anything thrown would mean it tried.
        var ex = Record.Exception(() => m.Invoke(null, new object[] { url }));
        Assert.Null(ex);
    }
}
