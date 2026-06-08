using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;

namespace AvaloniaRichEditor.Demo.Controls;

// Drop-in-style host around RichEditor exposing the same surface SaemDesk's JoditEditor uses:
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
    // in the default browser, where the user can print. Mirrors the old Jodit print behavior closely.
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

    private Border BuildToolbar()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };

        void Add(Control c) => panel.Children.Add(c);
        Button B(string content, Action click)
        {
            var b = new Button { Content = content, Margin = new Thickness(2), Padding = new Thickness(6, 2) };
            b.Click += (_, _) => click();
            return b;
        }

        Add(B("B", _editor.ToggleBold));
        Add(B("I", _editor.ToggleItalic));
        Add(B("U", _editor.ToggleUnderline));
        Add(B("S", _editor.ToggleStrikethrough));

        var fonts = new ComboBox { Margin = new Thickness(6, 2, 2, 2), MinWidth = 120 };
        foreach (var f in new[] { "Malgun Gothic", "Gulim", "Batang", "Dotum", "Arial", "Times New Roman" })
            fonts.Items.Add(new ComboBoxItem { Content = f });
        fonts.SelectionChanged += (_, _) => { if (fonts.SelectedItem is ComboBoxItem it && it.Content is string fam) _editor.SetFontFamily(fam); };
        Add(fonts);

        var sizes = new ComboBox { Margin = new Thickness(2), MinWidth = 60 };
        foreach (var s in new[] { 10.0, 12, 14, 18, 24, 32 })
            sizes.Items.Add(new ComboBoxItem { Content = s.ToString() });
        sizes.SelectionChanged += (_, _) => { if (sizes.SelectedItem is ComboBoxItem it && double.TryParse(it.Content?.ToString(), out var sz)) _editor.SetFontSize(sz); };
        Add(sizes);

        Add(B("A", () => _editor.SetForeground(Brushes.Black)));
        Add(B("A!", () => _editor.SetForeground(Brushes.Red)));
        Add(B("🖍", () => _editor.SetHighlight(Brushes.Yellow)));
        Add(B("🖍✕", () => _editor.SetHighlight(null)));

        Add(B("⯇", () => _editor.SetTextAlignment(TextAlignment.Left)));
        Add(B("≡", () => _editor.SetTextAlignment(TextAlignment.Center)));
        Add(B("⯈", () => _editor.SetTextAlignment(TextAlignment.Right)));

        Add(B("•", _editor.ToggleBullet));
        Add(B("1.", _editor.ToggleNumbering));
        Add(B("→|", () => _editor.Indent(20)));
        Add(B("|←", () => _editor.Indent(-20)));

        Add(B("H1", () => _editor.SetHeading(1)));
        Add(B("H2", () => _editor.SetHeading(2)));
        Add(B("H3", () => _editor.SetHeading(3)));
        Add(B("¶", () => _editor.SetHeading(0)));

        Add(B("―", _editor.InsertDivider));
        Add(B("표", () => _editor.InsertTable(3, 3)));
        Add(B("🖼", () => { _ = _editor.InsertImageFromFileAsync(); }));

        Add(B("↶", _editor.Undo));
        Add(B("↷", _editor.Redo));
        Add(B("🖨", () => { _ = PrintAsync(); }));

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#DDDDDD")),
            Padding = new Thickness(4),
            Child = panel
        };
    }
}
