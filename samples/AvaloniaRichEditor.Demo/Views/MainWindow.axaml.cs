using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaRichEditor;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace AvaloniaRichEditor.Demo.Views;

// The demo window is nothing but the library's RichEditorView. Everything toolbar-related — the
// formatting buttons (library) plus app-shell actions (save/open/export/print, zoom, page) — lives
// in the view's single toolbar: the shell actions are injected via RichEditorToolbar.LeadingItems/
// TrailingItems. No separate chrome rows, so the toolbar is verified entirely inside the view.
public partial class MainWindow : Window
{
    private RichEditor Editor => EditorView.Editor;
    private ComboBox? _zoomCombo;
    private bool _suppressZoomCombo;

    public MainWindow()
    {
        InitializeComponent();
        RegisterDemoStrings();

        Editor.ShowPageNumbers = true;
        Editor.Document = BuildSampleDocument();
        BuildToolbarHostItems();

        // Status bar below the view. Caret/text counts refresh on any caret move (SelectionChanged);
        // page count and the soft image-limit warning (N6-6) need O(blocks) walks, so they ride the
        // content-only TextChanged signal rather than firing on arrow keys / clicks too.
        Editor.TextChanged += (_, _) => OnContentChanged();
        Editor.SelectionChanged += (_, _) => UpdateStatusBar();
        Editor.RecommendedImageLimitExceeded += (_, _) => UpdateLimitWarning();
        OnContentChanged();
    }

    private void OnContentChanged()
    {
        UpdateStatusBar();
        if (this.FindControl<TextBlock>("PageInfo") is { } pageInfo)
            pageInfo.Text = string.Format(Loc("Demo.Pages"), Editor.GetPrintPageCount());
        // Clear the warning once the image count is back within bounds.
        if (this.FindControl<TextBlock>("LimitWarning") is { } warning
            && !string.IsNullOrEmpty(warning.Text)
            && Editor.GetImageCount() <= Editor.MaxRecommendedImages)
            warning.Text = "";
    }

    private void UpdateStatusBar()
    {
        var (chars, words, line, col) = Editor.GetStatus();
        if (this.FindControl<TextBlock>("StatusBar") is { } status)
            status.Text = string.Format(Loc("StatusFormat"), chars, words, line, col);
    }

    private void UpdateLimitWarning()
    {
        if (this.FindControl<TextBlock>("LimitWarning") is { } warning)
            warning.Text = string.Format(Loc("Demo.ImageLimitWarning"), Editor.GetImageCount(), Editor.MaxRecommendedImages);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Editor.FocusDocumentEnd();
        SetFitWidth(); // apply the default fit-to-width now that the view has a real size
    }

    private static FlowDocument BuildSampleDocument()
    {
        var doc = new FlowDocument();

        var p1 = new Paragraph();
        p1.Inlines.Add(new Run { Text = "Hello, Avalonia! ", Foreground = Brushes.Blue, FontWeight = FontWeight.Bold });
        p1.Inlines.Add(new Run { Text = "This is a custom RichTextBox built from scratch. " });
        p1.Inlines.Add(new Run { Text = "It supports multiple styles in a single paragraph.", Foreground = Brushes.Red });
        doc.Blocks.Add(p1);

        var p2 = new Paragraph();
        p2.Inlines.Add(new Run { Text = "This is the second paragraph. " });
        p2.Inlines.Add(new Run { Text = "As you can see, our custom TextLayout engine wraps lines and spaces paragraphs correctly. ", FontWeight = FontWeight.SemiBold });
        p2.Inlines.Add(new Run { Text = "It's just the beginning!", Foreground = Brushes.Green });
        doc.Blocks.Add(p2);

        return doc;
    }

    // ---- Toolbar host items (injected into the view's single toolbar) ----

    private void BuildToolbarHostItems()
    {
        var tb = EditorView.Toolbar;

        // Leading: file actions. FluentIcons here are the *app's* button icons (not the editor's
        // formatting glyphs, which stay the library's built-in vectors).
        // Save covers all formats (JSON / .ardx / HTML) — HTML export is just another save format.
        tb.LeadingItems.Add(ShellButton(SymbolIcon(Symbol.Save), "Demo.Save", () => _ = SaveAsync()));
        tb.LeadingItems.Add(ShellButton(SymbolIcon(Symbol.FolderOpen), "Demo.LoadJson", () => _ = LoadAsync()));
        tb.LeadingItems.Add(ShellButton(SymbolIcon(Symbol.Print), "Demo.PrintPreview", () => new PrintPreviewWindow(Editor).Show(this)));

        // Trailing: zoom (combo only — no +/- buttons) and page-view toggle.
        _zoomCombo = new ComboBox { Width = 84, FontSize = 12, SelectedIndex = 0, VerticalAlignment = VerticalAlignment.Center, BorderBrush = new SolidColorBrush(Color.Parse("#DCDCDC")) };
        _zoomCombo.Items.Add(new ComboBoxItem { Content = Loc("Demo.Fit") });
        foreach (var p in new[] { "50%", "75%", "100%", "125%", "150%", "200%" })
            _zoomCombo.Items.Add(new ComboBoxItem { Content = p });
        ToolTip.SetTip(_zoomCombo, Loc("Demo.ZoomTip"));
        _zoomCombo.SelectionChanged += ZoomCombo_SelectionChanged;
        tb.TrailingItems.Add(_zoomCombo);

        var page = new CheckBox { Content = Loc("Demo.PageView"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        page.IsCheckedChanged += (_, _) => { Editor.PageView = page.IsChecked == true; if (_fitWidth) ApplyFitWidth(); };
        tb.TrailingItems.Add(page);
    }

    private static Control SymbolIcon(Symbol s) => new FluentIcons.Avalonia.SymbolIcon { Symbol = s, FontSize = 16 };

    // A flat toolbar button matching the library's own button look.
    private Button ShellButton(Control content, string tipKey, Action onClick)
    {
        var b = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 5),
            Margin = new Thickness(1, 0),
            MinWidth = 30,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(b, Loc(tipKey));
        b.Click += (_, _) => onClick();
        return b;
    }

    // ---- View zoom (RichEditorView.ZoomFactor; fit-to-width default + combo + Ctrl+wheel/keys) ----

    private bool _fitWidth = true; // default: fit to width

    // Fit-to-width: in page view, scale the A4 page to the viewport; in the reflowing layout the
    // editor already fills the width at 100%, so fit is just 1.0. Recomputed on resize.
    private void ApplyFitWidth()
    {
        if (EditorView.Bounds.Width < 50) return; // not laid out yet
        const double a4 = 794, pad = 40;
        EditorView.ZoomFactor = Editor.PageView
            ? Math.Clamp((EditorView.Bounds.Width - pad) / a4, 0.2, 5.0)
            : 1.0;
        UpdateZoomLabel();
    }

    private void SetFitWidth()
    {
        _fitWidth = true;
        ApplyFitWidth();
        if (_zoomCombo != null) { _suppressZoomCombo = true; _zoomCombo.SelectedIndex = 0; _suppressZoomCombo = false; }
        UpdateZoomLabel();
    }

    private void SetZoomPercent(double factor)
    {
        _fitWidth = false;
        EditorView.ZoomFactor = factor; // clamped by the library (0.2–5.0)
        SyncZoomCombo();
        UpdateZoomLabel();
    }

    private void UpdateZoomLabel()
    {
        if (this.FindControl<TextBlock>("ZoomInfo") is { } z)
            z.Text = $"{(int)Math.Round(EditorView.ZoomFactor * 100)}%" + (_fitWidth ? " · " + Loc("Demo.Fit") : "");
    }

    private void SyncZoomCombo()
    {
        if (_zoomCombo == null) return;
        _suppressZoomCombo = true;
        int pct = (int)Math.Round(EditorView.ZoomFactor * 100);
        ComboBoxItem? match = null;
        for (int i = 1; i < _zoomCombo.Items.Count; i++) // skip index 0 ("fit")
            if (_zoomCombo.Items[i] is ComboBoxItem ci && ci.Content?.ToString() == pct + "%") { match = ci; break; }
        _zoomCombo.SelectedItem = match;
        _suppressZoomCombo = false;
    }

    private void ZoomCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressZoomCombo || _zoomCombo?.SelectedItem is not ComboBoxItem item) return;
        if (_zoomCombo.SelectedIndex == 0) SetFitWidth();
        else if (int.TryParse(item.Content?.ToString()?.TrimEnd('%'), out int pct)) SetZoomPercent(pct / 100.0);
    }

    private void EditorView_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_fitWidth) ApplyFitWidth();
    }

    private void EditorView_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        SetZoomPercent(EditorView.ZoomFactor + (e.Delta.Y > 0 ? 0.1 : -0.1));
        e.Handled = true;
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0)) { SetFitWidth(); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { SetZoomPercent(EditorView.ZoomFactor + 0.1); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { SetZoomPercent(EditorView.ZoomFactor - 0.1); e.Handled = true; }
    }

    // ---- File actions ----

    private async Task SaveAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || Editor.Document == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save Document",
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("JSON document") { Patterns = new[] { "*.json" } },
                new Avalonia.Platform.Storage.FilePickerFileType("ARDX package") { Patterns = new[] { "*.ardx" } },
                new Avalonia.Platform.Storage.FilePickerFileType("HTML document") { Patterns = new[] { "*.html", "*.htm" } },
            }
        });
        if (file == null) return;

        // Format follows the chosen extension: .ardx = ZIP package (raw image bytes), .html/.htm =
        // HTML export, anything else = plain JSON. HTML export is just another save format.
        if (file.Name.EndsWith(".ardx", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = await file.OpenWriteAsync();
            await Editor.SavePackageAsync(stream);
        }
        else if (file.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            string html = Editor.ToHtml();
            using var stream = await file.OpenWriteAsync();
            using var writer = new System.IO.StreamWriter(stream);
            await writer.WriteAsync(html);
        }
        else
        {
            string json = await Editor.ToJsonAsync(); // serialize off the UI thread (N6-3)
            using var stream = await file.OpenWriteAsync();
            using var writer = new System.IO.StreamWriter(stream);
            await writer.WriteAsync(json);
        }
    }

    private async Task LoadAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Load Document",
            AllowMultiple = false
        });
        if (file == null || file.Count == 0) return;

        try
        {
            using var stream = await file[0].OpenReadAsync();
            using var ms = new System.IO.MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            // Sniff the content: ZIP magic ("PK") = .ardx package, anything else = JSON text.
            if (ms.Length >= 2 && ms.GetBuffer()[0] == (byte)'P' && ms.GetBuffer()[1] == (byte)'K')
                await Editor.LoadPackageAsync(ms);
            else
                await Editor.LoadJsonAsync(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load document: {ex.Message}");
        }
    }

    // ---- Localization (app-shell strings) ----

    private static string Loc(string key) => RichEditorLocalization.GetString(key);

    private static void RegisterDemoStrings()
    {
        RichEditorLocalization.Register("en", new Dictionary<string, string>
        {
            ["Demo.Save"] = "Save (JSON / .ardx / HTML)",
            ["Demo.LoadJson"] = "Load JSON",
            ["Demo.ZoomTip"] = "View zoom (Ctrl+wheel, Ctrl+0 = 100%)",
            ["Demo.Fit"] = "Fit",
            ["Demo.PageView"] = "Pages",
            ["Demo.PrintPreview"] = "Print preview",
            ["Demo.Pages"] = "{0} page(s)",
            ["Demo.ImageLimitWarning"] = "⚠ {0} images — exceeds the recommended {1} (may slow down)",
        });
        RichEditorLocalization.Register("ko", new Dictionary<string, string>
        {
            ["Demo.Save"] = "저장 (JSON / .ardx / HTML)",
            ["Demo.LoadJson"] = "JSON 불러오기",
            ["Demo.ZoomTip"] = "보기 배율 (Ctrl+휠, Ctrl+0=100%)",
            ["Demo.Fit"] = "맞춤",
            ["Demo.PageView"] = "페이지",
            ["Demo.PrintPreview"] = "인쇄 미리보기",
            ["Demo.Pages"] = "{0}페이지",
            ["Demo.ImageLimitWarning"] = "⚠ 이미지 {0}개 — 권장 {1}개 초과 (성능 저하 가능)",
        });
    }
}
