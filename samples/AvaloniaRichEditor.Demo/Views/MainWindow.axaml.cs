using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaRichEditor;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Controls;

namespace AvaloniaRichEditor.Demo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var richTextBox = this.FindControl<RichEditor>("RichTextBox");
        if (richTextBox != null)
        {
            // Font pickers (toolbar combo + right-click submenu) default to the installed system
            // fonts with OS-localized names ("맑은 고딕" on Korean Windows) — no override needed.

            // Connect the library toolbar: commands, caret-state reflection and feature-flag
            // visibility are all driven through this single Target link.
            if (this.FindControl<RichEditorToolbar>("EditorToolbar") is { } toolbar)
                toolbar.Target = richTextBox;

            // Page-margin chrome demo: numbers show in page view and print/PDF output.
            richTextBox.ShowPageNumbers = true;

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

            richTextBox.Document = doc;

            // Status bar follows the caret/text (the toolbar subscribes on its own).
            richTextBox.StatusChanged += (_, _) => UpdateStatusBar();
            // Soft image-count limit (N6-6): warn once when exceeded; cleared in UpdateStatusBar.
            richTextBox.RecommendedImageLimitExceeded += (_, _) => UpdateLimitWarning();
            UpdateStatusBar();
        }
        RegisterDemoStrings();
        LocalizeChrome();
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        // Once the window is shown, focus the editor with a blinking caret at the document end so the
        // user can type immediately without clicking.
        this.FindControl<RichEditor>("RichTextBox")?.FocusDocumentEnd();
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
            ["Demo.ZoomTip"] = "View zoom (Ctrl+wheel, Ctrl+0 = fit)",
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
            ["Demo.ZoomTip"] = "보기 배율 (Ctrl+휠, Ctrl+0=맞춤)",
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

    // ---- View zoom ----

    private bool _fitWidth = true;       // fit: scale to viewport width
    private double _zoom = 1.0;          // fixed zoom factor when not fit-to-width
    private bool _suppressZoomCombo;
    private const double PageW = 794;

    private void EditorScroll_SizeChanged(object? sender, SizeChangedEventArgs e) => ApplyZoom();

    // Applies the current view mode to the page's LayoutTransform.
    private void ApplyZoom()
    {
        var lt = this.FindControl<LayoutTransformControl>("PageScale");
        var scroll = this.FindControl<ScrollViewer>("EditorScroll");
        if (lt == null || scroll == null) return;
        double scale = _fitWidth
            ? System.Math.Clamp((scroll.Bounds.Width - 8) / PageW, 0.2, 3.0)
            : _zoom;
        lt.LayoutTransform = new ScaleTransform(scale, scale);
        scroll.HorizontalScrollBarVisibility = _fitWidth
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        // Fit fills the viewport (plain white); fixed zoom shows the paper on a grey desk with a border.
        // In page view the library draws its own desk/papers, so the demo's paper chrome stays off.
        bool pageView = this.FindControl<CheckBox>("PageViewToggle")?.IsChecked == true;
        scroll.Background = _fitWidth && !pageView ? Brushes.White : new SolidColorBrush(Color.Parse("#9E9E9E"));
        if (!pageView && this.FindControl<Border>("PageBorder") is { } page)
        {
            page.BorderThickness = new Avalonia.Thickness(_fitWidth ? 0 : 1);
            page.BoxShadow = _fitWidth ? default : BoxShadows.Parse("0 1 10 0 #40000000");
        }
        if (this.FindControl<TextBlock>("ZoomLabel") is { } zl)
            zl.Text = $"{System.Math.Round(scale * 100)}%";
    }

    private void ZoomCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressZoomCombo) return;
        var combo = sender as ComboBox;
        if (combo?.SelectedItem is not ComboBoxItem item) return;
        // Index 0 is the (localized) fit-to-width entry; the rest are fixed percentages.
        if (combo.SelectedIndex == 0) _fitWidth = true;
        else if (int.TryParse(item.Content?.ToString()?.TrimEnd('%'), out int pct)) { _fitWidth = false; _zoom = pct / 100.0; }
        ApplyZoom();
    }

    private void SetZoomPercent(double factor)
    {
        _fitWidth = false;
        _zoom = System.Math.Clamp(factor, 0.2, 3.0);
        ApplyZoom();
        // Reflect on the combo (snap to nearest listed %, else leave as-is).
        _suppressZoomCombo = true;
        var combo = this.FindControl<ComboBox>("ZoomCombo");
        if (combo != null)
        {
            int pct = (int)System.Math.Round(_zoom * 100);
            ComboBoxItem? match = null;
            foreach (var it in combo.Items)
                if (it is ComboBoxItem ci && ci.Content?.ToString() == pct + "%") { match = ci; break; }
            combo.SelectedItem = match; // null clears selection when not a listed value
        }
        _suppressZoomCombo = false;
    }

    private void ZoomIn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SetZoomPercent((_fitWidth ? 1.0 : _zoom) + 0.1);
    private void ZoomOut_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SetZoomPercent((_fitWidth ? 1.0 : _zoom) - 0.1);

    private void EditorScroll_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)) return;
        SetZoomPercent((_fitWidth ? 1.0 : _zoom) + (e.Delta.Y > 0 ? 0.1 : -0.1));
        e.Handled = true;
    }

    private void PrintPreviewButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var editor = this.FindControl<AvaloniaRichEditor.Controls.RichEditor>("RichTextBox");
        if (editor == null) return;
        new PrintPreviewWindow(editor).Show(this);
    }

    // Page view (library P-milestone Phase 2): the editor draws its own desk + A4 papers, so the
    // demo's single-paper Border is neutralized while it's on and restored when it's off.
    private void PageViewToggle_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool on = this.FindControl<CheckBox>("PageViewToggle")?.IsChecked == true;
        var editor = this.FindControl<AvaloniaRichEditor.Controls.RichEditor>("RichTextBox");
        var border = this.FindControl<Border>("PageBorder");
        var lt = this.FindControl<LayoutTransformControl>("PageScale");
        if (editor == null || border == null || lt == null) return;

        editor.PageView = on;
        if (on)
        {
            border.Width = double.NaN;
            border.MinHeight = 0;
            border.Padding = default;
            border.Margin = default;
            border.Background = Brushes.Transparent;
            border.BorderThickness = default;
            border.BoxShadow = default;
            lt.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        }
        else
        {
            border.Width = 794;
            border.MinHeight = 1000;
            border.Padding = new Avalonia.Thickness(48, 40);
            border.Margin = new Avalonia.Thickness(0, 16);
            border.Background = Brushes.White;
            lt.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        }
        ApplyZoom(); // restores/clears the paper chrome consistently with the current zoom mode
    }

    public void ResetZoomToFit()
    {
        _fitWidth = true;
        ApplyZoom();
        _suppressZoomCombo = true;
        var combo = this.FindControl<ComboBox>("ZoomCombo");
        if (combo != null) combo.SelectedIndex = 0; // fit
        _suppressZoomCombo = false;
    }

    // ---- Status bar ----

    private void UpdateStatusBar()
    {
        var richTextBox = this.FindControl<RichEditor>("RichTextBox");
        var status = this.FindControl<TextBlock>("StatusBar");
        if (richTextBox == null || status == null) return;
        var (chars, words, line, col) = richTextBox.GetStatus();
        status.Text = string.Format(Loc("StatusFormat"), chars, words, line, col);

        // Clear the soft-limit warning once the image count is back within bounds.
        var warning = this.FindControl<TextBlock>("LimitWarning");
        if (warning != null && !string.IsNullOrEmpty(warning.Text)
            && richTextBox.GetImageCount() <= richTextBox.MaxRecommendedImages)
            warning.Text = "";
    }

    private void UpdateLimitWarning()
    {
        var richTextBox = this.FindControl<RichEditor>("RichTextBox");
        var warning = this.FindControl<TextBlock>("LimitWarning");
        if (richTextBox == null || warning == null) return;
        warning.Text = string.Format(Loc("Demo.ImageLimitWarning"),
            richTextBox.GetImageCount(), richTextBox.MaxRecommendedImages);
    }

    // ---- File actions ----

    private async void SaveButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var richTextBox = this.FindControl<RichEditor>("RichTextBox");
        if (richTextBox?.Document == null) return;

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
                await richTextBox.SavePackageAsync(stream);
            }
            else
            {
                string json = await richTextBox.ToJsonAsync(); // serialize off the UI thread (N6-3)
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

                var richTextBox = this.FindControl<RichEditor>("RichTextBox");
                if (richTextBox == null) return;

                // Sniff the content: ZIP magic ("PK") = .ardx package, anything else = JSON text.
                if (ms.Length >= 2 && ms.GetBuffer()[0] == (byte)'P' && ms.GetBuffer()[1] == (byte)'K')
                {
                    await richTextBox.LoadPackageAsync(ms);
                }
                else
                {
                    string json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                    await richTextBox.LoadJsonAsync(json); // parse off the UI thread (N6-3)
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
        var richTextBox = this.FindControl<RichEditor>("RichTextBox");
        if (topLevel == null || richTextBox?.Document == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export HTML",
            DefaultExtension = "html"
        });

        if (file != null)
        {
            string html = AvaloniaRichEditor.Formatters.HtmlDocumentFormatter.ToHtml(richTextBox.Document);
            using var stream = await file.OpenWriteAsync();
            using var writer = new System.IO.StreamWriter(stream);
            await writer.WriteAsync(html);
        }
    }

    // ---- Find / Replace bar ----

    private void Window_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
        if (ctrl && (e.Key == Avalonia.Input.Key.D0 || e.Key == Avalonia.Input.Key.NumPad0)) { ResetZoomToFit(); e.Handled = true; return; }
        if (ctrl && (e.Key == Avalonia.Input.Key.OemPlus || e.Key == Avalonia.Input.Key.Add)) { SetZoomPercent((_fitWidth ? 1.0 : _zoom) + 0.1); e.Handled = true; return; }
        if (ctrl && (e.Key == Avalonia.Input.Key.OemMinus || e.Key == Avalonia.Input.Key.Subtract)) { SetZoomPercent((_fitWidth ? 1.0 : _zoom) - 0.1); e.Handled = true; return; }

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
                this.FindControl<RichEditor>("RichTextBox")?.Focus();
                e.Handled = true;
            }
        }
    }

    private void ShowFindBar()
    {
        var bar = this.FindControl<Border>("FindBar");
        if (bar != null) bar.IsVisible = true;
        var box = this.FindControl<TextBox>("FindBox");
        box?.Focus();
        box?.SelectAll();
    }

    private bool MatchCase => this.FindControl<CheckBox>("MatchCaseBox")?.IsChecked == true;
    private string FindText => this.FindControl<TextBox>("FindBox")?.Text ?? "";
    private string ReplaceText => this.FindControl<TextBox>("ReplaceBox")?.Text ?? "";

    private void SetFindStatus(string text)
    {
        var status = this.FindControl<TextBlock>("FindStatus");
        if (status != null) status.Text = text;
    }

    private void FindNext_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rtb = this.FindControl<RichEditor>("RichTextBox");
        if (rtb == null || string.IsNullOrEmpty(FindText)) return;
        SetFindStatus(rtb.FindNext(FindText, MatchCase) ? "" : Loc("NotFound"));
    }

    private void FindPrev_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rtb = this.FindControl<RichEditor>("RichTextBox");
        if (rtb == null || string.IsNullOrEmpty(FindText)) return;
        SetFindStatus(rtb.FindPrev(FindText, MatchCase) ? "" : Loc("NotFound"));
    }

    private void ReplaceNext_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rtb = this.FindControl<RichEditor>("RichTextBox");
        if (rtb == null || string.IsNullOrEmpty(FindText)) return;
        SetFindStatus(rtb.ReplaceNext(FindText, ReplaceText, MatchCase) ? "" : Loc("NotFound"));
    }

    private void ReplaceAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rtb = this.FindControl<RichEditor>("RichTextBox");
        if (rtb == null || string.IsNullOrEmpty(FindText)) return;
        int n = rtb.ReplaceAll(FindText, ReplaceText, MatchCase);
        SetFindStatus(string.Format(Loc("ReplacedFormat"), n));
    }

    private void CloseFind_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var bar = this.FindControl<Border>("FindBar");
        if (bar != null) bar.IsVisible = false;
        this.FindControl<RichEditor>("RichTextBox")?.Focus();
    }
}
