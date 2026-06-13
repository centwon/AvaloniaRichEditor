using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaRichEditor;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Controls;

namespace AvaloniaRichEditor.Demo.Views;

// App shell around the library's RichEditorView bundle (layer ③). The shell owns only what the
// library deliberately leaves out — file save/open, HTML export, print/PDF, find/replace, and the
// status bar. Zoom is the view's ZoomFactor; page mode is the editor's PageView. No icon provider,
// so the bundled toolbar shows the library's built-in vector glyphs.
public partial class MainWindow : Window
{
    private RichEditor Editor => EditorView.Editor;

    public MainWindow()
    {
        InitializeComponent();

        // Page-margin chrome demo: numbers show in page view and print/PDF output.
        Editor.ShowPageNumbers = true;
        Editor.Document = BuildSampleDocument();

        // Status bar follows the caret/text (the toolbar subscribes on its own).
        Editor.StatusChanged += (_, _) => UpdateStatusBar();
        // Soft image-count limit (N6-6): warn once when exceeded; cleared in UpdateStatusBar.
        Editor.RecommendedImageLimitExceeded += (_, _) => UpdateLimitWarning();

        RegisterDemoStrings();
        LocalizeChrome();
        UpdateStatusBar();
        ApplyZoomLabel();
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        // Once the window is shown, focus the editor with a blinking caret at the document end so the
        // user can type immediately without clicking.
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

    // ---- Localization (app shell) ----

    private static string Loc(string key) => RichEditorLocalization.GetString(key);

    // App-shell strings (save/load/zoom) are demo-specific, so they aren't part of the library's
    // built-in table — Register() merges per key, letting any host add its own keys (and languages).
    private static void RegisterDemoStrings()
    {
        RichEditorLocalization.Register("en", new Dictionary<string, string>
        {
            ["Demo.SaveJson"] = "Save as JSON",
            ["Demo.LoadJson"] = "Load JSON",
            ["Demo.ExportHtml"] = "Export as HTML",
            ["Demo.ZoomIn"] = "Zoom in (Ctrl++)",
            ["Demo.ZoomOut"] = "Zoom out (Ctrl+-)",
            ["Demo.ZoomTip"] = "View zoom (Ctrl+wheel, Ctrl+0 = 100%)",
            ["Demo.Fit"] = "Fit",
            ["Demo.PageView"] = "Pages",
            ["Demo.PrintPreview"] = "Print preview",
            ["Demo.Print"] = "Print",
            ["Demo.SavePdf"] = "Save PDF",
            ["Demo.PrintDone"] = "Sent to printer",
            ["Demo.PdfDone"] = "PDF saved",
            ["Demo.ImageLimitWarning"] = "⚠ {0} images — exceeds the recommended {1} (may slow down)",
        });
        RichEditorLocalization.Register("ko", new Dictionary<string, string>
        {
            ["Demo.SaveJson"] = "JSON으로 저장",
            ["Demo.LoadJson"] = "JSON 불러오기",
            ["Demo.ExportHtml"] = "HTML로 내보내기",
            ["Demo.ZoomIn"] = "확대 (Ctrl++)",
            ["Demo.ZoomOut"] = "축소 (Ctrl+-)",
            ["Demo.ZoomTip"] = "보기 배율 (Ctrl+휠, Ctrl+0=100%)",
            ["Demo.Fit"] = "맞춤",
            ["Demo.PageView"] = "페이지",
            ["Demo.PrintPreview"] = "인쇄 미리보기",
            ["Demo.Print"] = "인쇄",
            ["Demo.SavePdf"] = "PDF 저장",
            ["Demo.PrintDone"] = "프린터로 전송됨",
            ["Demo.PdfDone"] = "PDF 저장됨",
            ["Demo.ImageLimitWarning"] = "⚠ 이미지 {0}개 — 권장 {1}개 초과 (성능 저하 가능)",
        });
    }

    // Applies localized texts/tooltips to the XAML chrome (file row, zoom, find bar).
    private void LocalizeChrome()
    {
        void Tip(string name, string key)
        {
            if (this.FindControl<Control>(name) is { } c) ToolTip.SetTip(c, Loc(key));
        }
        Tip("SaveButton", "Demo.SaveJson");
        Tip("LoadButton", "Demo.LoadJson");
        Tip("ExportHtmlButton", "Demo.ExportHtml");
        Tip("PrintPreviewButton", "Demo.PrintPreview");
        Tip("ZoomInButton", "Demo.ZoomIn");
        Tip("ZoomOutButton", "Demo.ZoomOut");
        Tip("ZoomCombo", "Demo.ZoomTip");
        if (this.FindControl<ComboBoxItem>("ZoomFitItem") is { } fit) fit.Content = Loc("Demo.Fit");
        if (this.FindControl<CheckBox>("PageViewToggle") is { } pv) pv.Content = Loc("Demo.PageView");

        if (this.FindControl<TextBlock>("FindLabel") is { } fl) fl.Text = Loc("Find") + ":";
        if (this.FindControl<Button>("FindPrevButton") is { } fp) fp.Content = Loc("FindPrevious");
        if (this.FindControl<Button>("FindNextButton") is { } fn) fn.Content = Loc("FindNext");
        if (this.FindControl<TextBlock>("ReplaceLabel") is { } rl) rl.Text = Loc("Replace") + ":";
        if (this.FindControl<Button>("ReplaceNextButton") is { } rn) rn.Content = Loc("Replace");
        if (this.FindControl<Button>("ReplaceAllButton") is { } ra) ra.Content = Loc("ReplaceAll");
        if (this.FindControl<CheckBox>("MatchCaseBox") is { } mc) mc.Content = Loc("MatchCase");
    }

    // ---- View zoom (drives RichEditorView.ZoomFactor; the bundle owns the scroller/scaling) ----

    private bool _suppressZoomCombo;

    private void SetZoom(double factor)
    {
        EditorView.ZoomFactor = factor; // clamped by the library (0.2–5.0)
        ApplyZoomLabel();
        SyncZoomCombo();
    }

    private void ApplyZoomLabel()
    {
        if (this.FindControl<TextBlock>("ZoomLabel") is { } zl)
            zl.Text = $"{System.Math.Round(EditorView.ZoomFactor * 100)}%";
    }

    // Reflect the current factor on the combo (snap to a listed %, else clear the selection).
    private void SyncZoomCombo()
    {
        _suppressZoomCombo = true;
        if (this.FindControl<ComboBox>("ZoomCombo") is { } combo)
        {
            int pct = (int)System.Math.Round(EditorView.ZoomFactor * 100);
            ComboBoxItem? match = null;
            foreach (var it in combo.Items)
                if (it is ComboBoxItem ci && ci.Name != "ZoomFitItem" && ci.Content?.ToString() == pct + "%") { match = ci; break; }
            combo.SelectedItem = match;
        }
        _suppressZoomCombo = false;
    }

    private void ZoomCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressZoomCombo) return;
        var combo = sender as ComboBox;
        if (combo?.SelectedItem is not ComboBoxItem item) return;
        // Index 0 = "fit": in the bundle's reflowing layout the editor already fills the width, so
        // fit is simply 100%. The rest are fixed percentages.
        if (combo.SelectedIndex == 0) SetZoom(1.0);
        else if (int.TryParse(item.Content?.ToString()?.TrimEnd('%'), out int pct)) SetZoom(pct / 100.0);
    }

    private void ZoomIn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SetZoom(EditorView.ZoomFactor + 0.1);
    private void ZoomOut_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SetZoom(EditorView.ZoomFactor - 0.1);

    private void EditorView_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)) return;
        SetZoom(EditorView.ZoomFactor + (e.Delta.Y > 0 ? 0.1 : -0.1));
        e.Handled = true;
    }

    private void PrintPreviewButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => new PrintPreviewWindow(Editor).Show(this);

    private void PageViewToggle_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        Editor.PageView = this.FindControl<CheckBox>("PageViewToggle")?.IsChecked == true;
    }

    // ---- Status bar ----

    private void UpdateStatusBar()
    {
        var status = this.FindControl<TextBlock>("StatusBar");
        if (status == null) return;
        var (chars, words, line, col) = Editor.GetStatus();
        status.Text = string.Format(Loc("StatusFormat"), chars, words, line, col);

        // Clear the soft-limit warning once the image count is back within bounds.
        var warning = this.FindControl<TextBlock>("LimitWarning");
        if (warning != null && !string.IsNullOrEmpty(warning.Text)
            && Editor.GetImageCount() <= Editor.MaxRecommendedImages)
            warning.Text = "";
    }

    private void UpdateLimitWarning()
    {
        if (this.FindControl<TextBlock>("LimitWarning") is not { } warning) return;
        warning.Text = string.Format(Loc("Demo.ImageLimitWarning"), Editor.GetImageCount(), Editor.MaxRecommendedImages);
    }

    // ---- File actions ----

    private async void SaveButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

        if (file != null)
        {
            // .ardx = ZIP package (raw image bytes, no base64); anything else = plain JSON.
            if (file.Name.EndsWith(".ardx", System.StringComparison.OrdinalIgnoreCase))
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
    }

    private async void LoadButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Load Document",
            AllowMultiple = false
        });

        if (file != null && file.Count > 0)
        {
            try
            {
                using var stream = await file[0].OpenReadAsync();
                using var ms = new System.IO.MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;

                // Sniff the content: ZIP magic ("PK") = .ardx package, anything else = JSON text.
                if (ms.Length >= 2 && ms.GetBuffer()[0] == (byte)'P' && ms.GetBuffer()[1] == (byte)'K')
                {
                    await Editor.LoadPackageAsync(ms);
                }
                else
                {
                    string json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                    await Editor.LoadJsonAsync(json); // parse off the UI thread (N6-3)
                }
            }
            catch (System.Exception ex)
            {
                // Invalid/corrupt document file — ignore instead of crashing the app.
                System.Diagnostics.Debug.WriteLine($"Failed to load document: {ex.Message}");
            }
        }
    }

    private async void ExportHtmlButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || Editor.Document == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export HTML",
            DefaultExtension = "html"
        });

        if (file != null)
        {
            string html = AvaloniaRichEditor.Formatters.HtmlDocumentFormatter.ToHtml(Editor.Document);
            using var stream = await file.OpenWriteAsync();
            using var writer = new System.IO.StreamWriter(stream);
            await writer.WriteAsync(html);
        }
    }

    // ---- Find / Replace bar ----

    private void Window_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
        if (ctrl && (e.Key == Avalonia.Input.Key.D0 || e.Key == Avalonia.Input.Key.NumPad0)) { SetZoom(1.0); e.Handled = true; return; }
        if (ctrl && (e.Key == Avalonia.Input.Key.OemPlus || e.Key == Avalonia.Input.Key.Add)) { SetZoom(EditorView.ZoomFactor + 0.1); e.Handled = true; return; }
        if (ctrl && (e.Key == Avalonia.Input.Key.OemMinus || e.Key == Avalonia.Input.Key.Subtract)) { SetZoom(EditorView.ZoomFactor - 0.1); e.Handled = true; return; }

        if (e.Key == Avalonia.Input.Key.F && ctrl)
        {
            ShowFindBar();
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            var bar = this.FindControl<Border>("FindBar");
            if (bar is { IsVisible: true })
            {
                bar.IsVisible = false;
                Editor.Focus();
                e.Handled = true;
            }
        }
    }

    private void ShowFindBar()
    {
        if (this.FindControl<Border>("FindBar") is { } bar) bar.IsVisible = true;
        var box = this.FindControl<TextBox>("FindBox");
        box?.Focus();
        box?.SelectAll();
    }

    private bool MatchCase => this.FindControl<CheckBox>("MatchCaseBox")?.IsChecked == true;
    private string FindText => this.FindControl<TextBox>("FindBox")?.Text ?? "";
    private string ReplaceText => this.FindControl<TextBox>("ReplaceBox")?.Text ?? "";

    private void SetFindStatus(string text)
    {
        if (this.FindControl<TextBlock>("FindStatus") is { } status) status.Text = text;
    }

    private void FindNext_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(FindText)) return;
        SetFindStatus(Editor.FindNext(FindText, MatchCase) ? "" : Loc("NotFound"));
    }

    private void FindPrev_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(FindText)) return;
        SetFindStatus(Editor.FindPrev(FindText, MatchCase) ? "" : Loc("NotFound"));
    }

    private void ReplaceNext_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(FindText)) return;
        SetFindStatus(Editor.ReplaceNext(FindText, ReplaceText, MatchCase) ? "" : Loc("NotFound"));
    }

    private void ReplaceAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(FindText)) return;
        int n = Editor.ReplaceAll(FindText, ReplaceText, MatchCase);
        SetFindStatus(string.Format(Loc("ReplacedFormat"), n));
    }

    private void CloseFind_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.FindControl<Border>("FindBar") is { } bar) bar.IsVisible = false;
        Editor.Focus();
    }
}
