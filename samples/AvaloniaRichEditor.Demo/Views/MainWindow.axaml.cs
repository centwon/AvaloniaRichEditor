using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
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
            // This app targets a Korean UI: offer Korean fonts in the editor's right-click font submenu.
            richTextBox.FontFamilyChoices = new[]
            {
                "Malgun Gothic", "Gulim", "Batang", "Dotum", "Arial", "Times New Roman"
            };

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

            // Status bar + toolbar state follow the caret/text.
            richTextBox.StatusChanged += (_, _) => { UpdateStatusBar(); UpdateToolbar(); };
            UpdateStatusBar();
            UpdateToolbar();
        }

        SetupColorFlyout("TextColorButton", highlight: false);
        SetupColorFlyout("HighlightButton", highlight: true);
        SetupTableFlyout("TableButton");
        SetupFontCombo();
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        // Once the window is shown, focus the editor with a blinking caret at the document end so the
        // user can type immediately without clicking.
        this.FindControl<RichEditor>("RichTextBox")?.FocusDocumentEnd();
    }

    // A drag-to-size table picker (hover the grid to choose rows×columns, click to insert).
    private void SetupTableFlyout(string buttonName)
    {
        var btn = this.FindControl<Button>(buttonName);
        if (btn == null) return;
        const int rows = 8, cols = 10;
        var cells = new Border[rows, cols];
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = cols, Rows = rows };
        var label = new TextBlock { Text = "끌어서 크기 선택", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 4, 0, 0) };

        void Highlight(int hr, int hc)
        {
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    cells[r, c].Background = (r <= hr && c <= hc) ? new SolidColorBrush(Color.Parse("#90CAF9")) : Brushes.White;
            label.Text = $"{hr + 1} × {hc + 1}";
        }

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int rr = r, cc = c;
                var cell = new Border
                {
                    Width = 16, Height = 16, Margin = new Avalonia.Thickness(1),
                    Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Avalonia.Thickness(1)
                };
                cell.PointerEntered += (_, _) => Highlight(rr, cc);
                cell.PointerPressed += (_, _) =>
                {
                    this.FindControl<RichEditor>("RichTextBox")?.InsertTable(rr + 1, cc + 1);
                    (btn.Flyout as Avalonia.Controls.Primitives.FlyoutBase)?.Hide();
                };
                cells[r, c] = cell;
                grid.Children.Add(cell);
            }

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(grid);
        panel.Children.Add(label);
        btn.Flyout = new Flyout { Content = panel };
    }

    // A 40-swatch palette (greys + hues in a few shades) used by the text-color/highlight pickers.
    private static readonly string[] Palette =
    {
        "#000000","#444444","#666666","#999999","#BBBBBB","#DDDDDD","#EEEEEE","#FFFFFF",
        "#FF0000","#E67E22","#F1C40F","#2ECC71","#1ABC9C","#3498DB","#9B59B6","#E91E63",
        "#C0392B","#D35400","#F39C12","#27AE60","#16A085","#2980B9","#8E44AD","#AD1457",
        "#7B241C","#935116","#9A7D0A","#196F3D","#0E6251","#1A5276","#5B2C6F","#78281F",
        "#FFCDD2","#FFE0B2","#FFF9C4","#C8E6C9","#B2DFDB","#BBDEFB","#E1BEE7","#F8BBD0",
    };

    // Builds a palette + hex-input flyout on the named toolbar button. `highlight` selects whether the
    // chosen colour is applied as text foreground or as a highlight (background) brush.
    private void SetupColorFlyout(string buttonName, bool highlight)
    {
        var btn = this.FindControl<Button>(buttonName);
        if (btn == null) return;

        // Button face: a glyph over a colour bar that shows the last-picked colour.
        var swatch = new Border
        {
            Height = 4, MinWidth = 18,
            Background = new SolidColorBrush(highlight ? Color.Parse("#FFF176") : Colors.Black),
            Margin = new Avalonia.Thickness(0, 1, 0, 0)
        };
        var face = new StackPanel { Spacing = 0 };
        face.Children.Add(new TextBlock { Text = highlight ? "🖍" : "가", FontSize = 13, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
        face.Children.Add(swatch);
        btn.Content = face;

        void Apply(Color? c)
        {
            var rtb = this.FindControl<RichEditor>("RichTextBox");
            if (rtb == null) return;
            IBrush? brush = c.HasValue ? new SolidColorBrush(c.Value) : null;
            if (highlight) rtb.SetHighlight(brush);
            else rtb.SetForeground(brush ?? Brushes.Black);
            // Reflect the chosen colour on the button's bar (cleared highlight -> light grey).
            swatch.Background = c.HasValue ? new SolidColorBrush(c.Value) : new SolidColorBrush(Color.Parse("#DDDDDD"));
            (btn.Flyout as Avalonia.Controls.Primitives.FlyoutBase)?.Hide();
        }

        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 8 };
        foreach (var hex in Palette)
        {
            var color = Color.Parse(hex);
            var sw = new Button
            {
                Background = new SolidColorBrush(color),
                Width = 22, Height = 22, Margin = new Avalonia.Thickness(1), Padding = new Avalonia.Thickness(0),
                BorderBrush = Brushes.Gray, BorderThickness = new Avalonia.Thickness(1)
            };
            sw.Click += (_, _) => Apply(color);
            grid.Children.Add(sw);
        }

        var panel = new StackPanel { Spacing = 6, Width = 200 };
        panel.Children.Add(grid);

        if (highlight)
        {
            var none = new Button { Content = "형광펜 없음", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            none.Click += (_, _) => Apply(null);
            panel.Children.Add(none);
        }

        var hexRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
        var hexBox = new TextBox { PlaceholderText = "#RRGGBB", Width = 110 };
        var applyBtn = new Button { Content = "적용" };
        applyBtn.Click += (_, _) => { if (Color.TryParse(hexBox.Text, out var c)) Apply(c); };
        hexRow.Children.Add(hexBox);
        hexRow.Children.Add(applyBtn);
        panel.Children.Add(hexRow);

        btn.Flyout = new Flyout { Content = panel };
    }

    private bool _fitWidth = true;       // "맞춤": scale to viewport width
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
        scroll.Background = _fitWidth ? Brushes.White : new SolidColorBrush(Color.Parse("#9E9E9E"));
        if (this.FindControl<Border>("PageBorder") is { } page)
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
        if ((sender as ComboBox)?.SelectedItem is not ComboBoxItem item) return;
        string s = item.Content?.ToString() ?? "맞춤";
        if (s == "맞춤") _fitWidth = true;
        else if (int.TryParse(s.TrimEnd('%'), out int pct)) { _fitWidth = false; _zoom = pct / 100.0; }
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

    public void ResetZoomToFit()
    {
        _fitWidth = true;
        ApplyZoom();
        _suppressZoomCombo = true;
        var combo = this.FindControl<ComboBox>("ZoomCombo");
        if (combo != null) combo.SelectedIndex = 0; // "맞춤"
        _suppressZoomCombo = false;
    }

    private void UpdateStatusBar()
    {
        var richTextBox = this.FindControl<RichEditor>("RichTextBox");
        var status = this.FindControl<TextBlock>("StatusBar");
        if (richTextBox == null || status == null) return;
        var (chars, words, line, col) = richTextBox.GetStatus();
        status.Text = $"글자 {chars}   단어 {words}   줄 {line}, 칸 {col}";
    }

    private bool _suppressToolbar; // guards combo SelectionChanged while we sync the toolbar to the caret

    // Reflects the caret's formatting on the toolbar: active B/I/U/S, alignment, list, font size/family.
    private void UpdateToolbar()
    {
        var rtb = this.FindControl<RichEditor>("RichTextBox");
        if (rtb == null) return;
        var f = rtb.GetCaretFormat();
        SetActive("BoldButton", f.Bold);
        SetActive("ItalicButton", f.Italic);
        SetActive("UnderlineButton", f.Underline);
        SetActive("StrikeButton", f.Strike);
        SetActive("BulletBtn", f.List == Documents.ListKind.Bullet);
        SetActive("NumberBtn", f.List == Documents.ListKind.Ordered);
        SetActive("FormatPainterButton", rtb.IsFormatPainterActive);

        _suppressToolbar = true;
        if (this.FindControl<ComboBox>("FontSizeComboBox") is { } sizeCb)
            SelectByContent(sizeCb, ((int)f.FontSize).ToString());
        if (f.FontFamily != null && this.FindControl<ComboBox>("FontFamilyComboBox") is { } famCb)
            foreach (var it in famCb.Items)
                if (it is ComboBoxItem ci && (ci.Tag as string) == f.FontFamily) { famCb.SelectedItem = ci; break; }
        if (this.FindControl<ComboBox>("HeadingCombo") is { } headCb)
            headCb.SelectedIndex = System.Math.Min(f.Heading, 3); // 0=본문, 1~3=제목
        if (this.FindControl<ComboBox>("AlignCombo") is { } alignCb)
            alignCb.SelectedIndex = f.Align switch { Avalonia.Media.TextAlignment.Center => 1, Avalonia.Media.TextAlignment.Right => 2, _ => 0 };
        _suppressToolbar = false;
    }

    private void SetActive(string name, bool active)
    {
        var b = this.FindControl<Button>(name);
        if (b == null) return;
        if (active) b.Background = new SolidColorBrush(Color.Parse("#90CAF9"));
        else b.ClearValue(Button.BackgroundProperty);
    }

    private static void SelectByContent(ComboBox cb, string content)
    {
        foreach (var it in cb.Items)
            if (it is ComboBoxItem ci && ci.Content?.ToString() == content) { cb.SelectedItem = ci; return; }
    }

    private async void SaveButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;
        
        var richTextBox = this.FindControl<RichEditor>("RichTextBox");
        if (richTextBox?.Document == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save Document",
            DefaultExtension = "json"
        });

        if (file != null)
        {
            string json = AvaloniaRichEditor.Formatters.DocumentSerializer.Serialize(richTextBox.Document);
            using var stream = await file.OpenWriteAsync();
            using var writer = new System.IO.StreamWriter(stream);
            await writer.WriteAsync(json);
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
                using var reader = new System.IO.StreamReader(stream);
                string json = await reader.ReadToEndAsync();

                var richTextBox = this.FindControl<RichEditor>("RichTextBox");
                if (richTextBox != null)
                {
                    richTextBox.Document = AvaloniaRichEditor.Formatters.DocumentSerializer.Deserialize(json);
                    richTextBox.InvalidateVisual();
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

    private async void InsertImageButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Image",
            AllowMultiple = false
        });

        if (file != null && file.Count > 0)
        {
            try
            {
                using var stream = await file[0].OpenReadAsync();
                var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
                var richTextBox = this.FindControl<RichEditor>("RichTextBox");
                richTextBox?.InsertImage(bitmap);
            }
            catch (System.Exception ex)
            {
                // Selected file wasn't a valid/supported image — ignore instead of crashing the app.
                System.Diagnostics.Debug.WriteLine($"Failed to load image: {ex.Message}");
            }
        }
    }

    private void BoldButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<RichEditor>("RichTextBox")?.ToggleBold();
    private void ItalicButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<RichEditor>("RichTextBox")?.ToggleItalic();
    private void StrikethroughButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<RichEditor>("RichTextBox")?.ToggleStrikethrough();
    private void UnderlineButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<RichEditor>("RichTextBox")?.ToggleUnderline();

    private void FormatPainterButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rtb = this.FindControl<RichEditor>("RichTextBox");
        rtb?.StartFormatPainter();
        SetActive("FormatPainterButton", rtb?.IsFormatPainterActive == true);
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
        SetFindStatus(rtb.FindNext(FindText, MatchCase) ? "" : "찾을 수 없음");
    }

    private void FindPrev_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rtb = this.FindControl<RichEditor>("RichTextBox");
        if (rtb == null || string.IsNullOrEmpty(FindText)) return;
        SetFindStatus(rtb.FindPrev(FindText, MatchCase) ? "" : "찾을 수 없음");
    }

    private void ReplaceNext_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rtb = this.FindControl<RichEditor>("RichTextBox");
        if (rtb == null || string.IsNullOrEmpty(FindText)) return;
        SetFindStatus(rtb.ReplaceNext(FindText, ReplaceText, MatchCase) ? "" : "찾을 수 없음");
    }

    private void ReplaceAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rtb = this.FindControl<RichEditor>("RichTextBox");
        if (rtb == null || string.IsNullOrEmpty(FindText)) return;
        int n = rtb.ReplaceAll(FindText, ReplaceText, MatchCase);
        SetFindStatus($"{n}개 바꿈");
    }

    private void CloseFind_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var bar = this.FindControl<Border>("FindBar");
        if (bar != null) bar.IsVisible = false;
        this.FindControl<RichEditor>("RichTextBox")?.Focus();
    }
    
    private void FontSizeComboBox_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressToolbar) return;
        var cb = sender as Avalonia.Controls.ComboBox;
        if (cb?.SelectedItem is Avalonia.Controls.ComboBoxItem item && double.TryParse(item.Content?.ToString(), out double size))
        {
            this.FindControl<RichEditor>("RichTextBox")?.SetFontSize(size);
        }
    }

    private void AlignCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressToolbar) return;
        var align = (sender as ComboBox)?.SelectedIndex switch
        {
            1 => Avalonia.Media.TextAlignment.Center,
            2 => Avalonia.Media.TextAlignment.Right,
            _ => Avalonia.Media.TextAlignment.Left,
        };
        this.FindControl<RichEditor>("RichTextBox")?.SetTextAlignment(align);
    }

    private void SpacingCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressToolbar) return;
        double lh = (sender as ComboBox)?.SelectedIndex switch { 1 => 24, 2 => 32, _ => double.NaN };
        this.FindControl<RichEditor>("RichTextBox")?.SetLineHeight(lh);
    }

    private void BulletButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<RichEditor>("RichTextBox")?.ToggleBullet();


    private void NumberingButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<RichEditor>("RichTextBox")?.ToggleNumbering();
    private void HeadingCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressToolbar) return;
        int level = (sender as ComboBox)?.SelectedIndex ?? 0; // 0=본문, 1~3=제목1~3
        this.FindControl<RichEditor>("RichTextBox")?.SetHeading(level);
    }
    private void IndentInc_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<RichEditor>("RichTextBox")?.Indent(20);
    private void IndentDec_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<RichEditor>("RichTextBox")?.Indent(-20);
    private void DividerButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<RichEditor>("RichTextBox")?.InsertDivider();

    private void FontFamilyComboBox_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressToolbar) return;
        if ((sender as ComboBox)?.SelectedItem is ComboBoxItem item)
        {
            // Tag holds the real font family; Content is the (possibly localized) display name.
            string fam = item.Tag as string ?? item.Content?.ToString() ?? "";
            if (!string.IsNullOrEmpty(fam)) this.FindControl<RichEditor>("RichTextBox")?.SetFontFamily(fam);
        }
    }

    // Font family display names are localized (맑은 고딕 …) on a Korean UI, English otherwise; the real
    // family name lives in Tag so applying/reflecting still uses the actual font.
    private static readonly (string family, string ko)[] Fonts =
    {
        ("Malgun Gothic", "맑은 고딕"), ("Gulim", "굴림"), ("Batang", "바탕"),
        ("Dotum", "돋움"), ("Arial", "Arial"), ("Times New Roman", "Times New Roman"),
    };

    private void SetupFontCombo()
    {
        var combo = this.FindControl<ComboBox>("FontFamilyComboBox");
        if (combo == null) return;
        bool ko = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";
        combo.Items.Clear();
        foreach (var (family, korean) in Fonts)
            combo.Items.Add(new ComboBoxItem { Content = ko ? korean : family, Tag = family });
        combo.SelectedIndex = 0;
    }
}