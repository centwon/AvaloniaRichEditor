using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;

namespace AvaloniaRichEditor.Demo.Controls;

// Drop-in-style host around RichEditor exposing a web-editor-compatible API surface:
//   Mode (ReadOnly/Simple/Full), Text (HTML, two-way), TextChanged, GetHtmlAsync, InsertHtmlAsync, PrintAsync.
// Full mode shows a built-in toolbar; Simple hides it; ReadOnly also disables editing.
public class NativeEditor : UserControl
{
    public enum EditorMode { ReadOnly, Simple, Full }

    private readonly RichEditor _editor = new();
    private readonly Border _toolbar;
    private bool _suppressTextChanged;

    public event EventHandler<string>? TextChanged;

    public static readonly StyledProperty<EditorMode> ModeProperty =
        AvaloniaProperty.Register<NativeEditor, EditorMode>(nameof(Mode), EditorMode.Simple);

    public EditorMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    // HTML content. Get serializes the document; set replaces it.
    public string Text
    {
        get => _editor.ToHtml();
        set { _suppressTextChanged = true; _editor.LoadHtml(value); _suppressTextChanged = false; }
    }

    public NativeEditor()
    {
        _toolbar = BuildToolbar();

        var scroller = new ScrollViewer
        {
            Padding = new Thickness(12),
            Content = _editor,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        var dock = new DockPanel();
        DockPanel.SetDock(_toolbar, Dock.Top);
        dock.Children.Add(_toolbar);
        dock.Children.Add(scroller);
        Content = dock;

        // Coarse change signal: emit current HTML when focus leaves the editor.
        _editor.LostFocus += (_, _) =>
        {
            if (!_suppressTextChanged) TextChanged?.Invoke(this, _editor.ToHtml());
        };

        ApplyMode();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ModeProperty) ApplyMode();
    }

    private void ApplyMode()
    {
        _editor.IsReadOnly = Mode == EditorMode.ReadOnly;
        _toolbar.IsVisible = Mode == EditorMode.Full;
    }

    public Task<string> GetHtmlAsync() => Task.FromResult(_editor.ToHtml());

    public Task InsertHtmlAsync(string html)
    {
        _editor.InsertHtml(html);
        return Task.CompletedTask;
    }

    // Print is bypassed (no native print engine): export to a temp HTML file and let the OS open it
    // in the default browser, where the user can print via the browser's print dialog.
    public async Task PrintAsync()
    {
        string body = _editor.ToHtml();
        string doc = "<!DOCTYPE html><html><head><meta charset=\"UTF-8\"><style>" +
            "body{font-family:'Malgun Gothic',sans-serif;font-size:12px;line-height:1.6;margin:24px;}" +
            "img{max-width:100%;height:auto;}table{border-collapse:collapse;}td,th{border:1px solid #999;padding:4px 8px;}" +
            "</style></head><body>" + body + "</body></html>";
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"print_{Guid.NewGuid():N}.html");
        await System.IO.File.WriteAllTextAsync(path, doc);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    // ---- toolbar ----

    // The formatting strip is the library's RichEditorToolbar (promoted from this demo, roadmap N3.6);
    // only the print button is NativeEditor-specific. The wrapping Border lets Simple mode hide the
    // whole row without fighting the toolbar's own flag-driven visibility.
    private Border BuildToolbar()
    {
        var print = new Button { Content = "🖨", Margin = new Thickness(2), Padding = new Thickness(6, 2), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        print.Click += (_, _) => { _ = PrintAsync(); };

        var dock = new DockPanel();
        DockPanel.SetDock(print, Dock.Right);
        dock.Children.Add(print);
        dock.Children.Add(new RichEditorToolbar { Target = _editor });

        return new Border { Background = new SolidColorBrush(Color.Parse("#DDDDDD")), Child = dock };
    }
}
