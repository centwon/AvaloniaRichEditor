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
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Editor.FocusDocumentEnd();
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
        tb.LeadingItems.Add(ShellButton(SymbolIcon(Symbol.Save), "Demo.SaveJson", () => _ = SaveAsync()));
        tb.LeadingItems.Add(ShellButton(SymbolIcon(Symbol.FolderOpen), "Demo.LoadJson", () => _ = LoadAsync()));
        tb.LeadingItems.Add(ShellButton(SymbolIcon(Symbol.ArrowExport), "Demo.ExportHtml", () => _ = ExportHtmlAsync()));
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
        page.IsCheckedChanged += (_, _) => Editor.PageView = page.IsChecked == true;
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

    // ---- View zoom (RichEditorView.ZoomFactor; combo + Ctrl+wheel/keys) ----

    private void SetZoom(double factor)
    {
        EditorView.ZoomFactor = factor; // clamped by the library (0.2–5.0)
        SyncZoomCombo();
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
        if (_zoomCombo.SelectedIndex == 0) SetZoom(1.0); // "fit" = 100% in the reflowing layout
        else if (int.TryParse(item.Content?.ToString()?.TrimEnd('%'), out int pct)) SetZoom(pct / 100.0);
    }

    private void EditorView_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        SetZoom(EditorView.ZoomFactor + (e.Delta.Y > 0 ? 0.1 : -0.1));
        e.Handled = true;
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0)) { SetZoom(1.0); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { SetZoom(EditorView.ZoomFactor + 0.1); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { SetZoom(EditorView.ZoomFactor - 0.1); e.Handled = true; }
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
            }
        });
        if (file == null) return;

        // .ardx = ZIP package (raw image bytes, no base64); anything else = plain JSON.
        if (file.Name.EndsWith(".ardx", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = await file.OpenWriteAsync();
            await Editor.SavePackageAsync(stream);
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

    private async Task ExportHtmlAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || Editor.Document == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export HTML",
            DefaultExtension = "html"
        });
        if (file == null) return;

        string html = AvaloniaRichEditor.Formatters.HtmlDocumentFormatter.ToHtml(Editor.Document);
        using var stream = await file.OpenWriteAsync();
        using var writer = new System.IO.StreamWriter(stream);
        await writer.WriteAsync(html);
    }

    // ---- Localization (app-shell strings) ----

    private static string Loc(string key) => RichEditorLocalization.GetString(key);

    private static void RegisterDemoStrings()
    {
        RichEditorLocalization.Register("en", new Dictionary<string, string>
        {
            ["Demo.SaveJson"] = "Save as JSON",
            ["Demo.LoadJson"] = "Load JSON",
            ["Demo.ExportHtml"] = "Export as HTML",
            ["Demo.ZoomTip"] = "View zoom (Ctrl+wheel, Ctrl+0 = 100%)",
            ["Demo.Fit"] = "Fit",
            ["Demo.PageView"] = "Pages",
            ["Demo.PrintPreview"] = "Print preview",
        });
        RichEditorLocalization.Register("ko", new Dictionary<string, string>
        {
            ["Demo.SaveJson"] = "JSON으로 저장",
            ["Demo.LoadJson"] = "JSON 불러오기",
            ["Demo.ExportHtml"] = "HTML로 내보내기",
            ["Demo.ZoomTip"] = "보기 배율 (Ctrl+휠, Ctrl+0=100%)",
            ["Demo.Fit"] = "맞춤",
            ["Demo.PageView"] = "페이지",
            ["Demo.PrintPreview"] = "인쇄 미리보기",
        });
    }
}
