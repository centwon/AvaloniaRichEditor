using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using AvaloniaRichTextBoxPort.Documents;
using AvaloniaRichTextBoxPort.Controls;

namespace AvaloniaRichTextBoxPort.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var richTextBox = this.FindControl<CustomRichTextBox>("RichTextBox");
        if (richTextBox != null)
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

            richTextBox.Document = doc;
        }
    }

    private async void SaveButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;
        
        var richTextBox = this.FindControl<CustomRichTextBox>("RichTextBox");
        if (richTextBox?.Document == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save Document",
            DefaultExtension = "json"
        });

        if (file != null)
        {
            string json = AvaloniaRichTextBoxPort.Formatters.DocumentSerializer.Serialize(richTextBox.Document);
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

                var richTextBox = this.FindControl<CustomRichTextBox>("RichTextBox");
                if (richTextBox != null)
                {
                    richTextBox.Document = AvaloniaRichTextBoxPort.Formatters.DocumentSerializer.Deserialize(json);
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
        var richTextBox = this.FindControl<CustomRichTextBox>("RichTextBox");
        if (topLevel == null || richTextBox?.Document == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export HTML",
            DefaultExtension = "html"
        });

        if (file != null)
        {
            string html = AvaloniaRichTextBoxPort.Formatters.HtmlDocumentFormatter.ToHtml(richTextBox.Document);
            using var stream = await file.OpenWriteAsync();
            using var writer = new System.IO.StreamWriter(stream);
            await writer.WriteAsync(html);
        }
    }

    private void PasteHtmlButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var richTextBox = this.FindControl<CustomRichTextBox>("RichTextBox");
        richTextBox?.PasteFromClipboardAsync();
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
                var richTextBox = this.FindControl<CustomRichTextBox>("RichTextBox");
                richTextBox?.InsertImage(bitmap);
            }
            catch (System.Exception ex)
            {
                // Selected file wasn't a valid/supported image — ignore instead of crashing the app.
                System.Diagnostics.Debug.WriteLine($"Failed to load image: {ex.Message}");
            }
        }
    }

    private void BoldButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.ToggleBold();
    private void ItalicButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.ToggleItalic();
    private void StrikethroughButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.ToggleStrikethrough();
    private void UnderlineButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.ToggleUnderline();

    // ---- Find / Replace bar ----

    private void Window_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.F && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
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
                this.FindControl<CustomRichTextBox>("RichTextBox")?.Focus();
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
        var rtb = this.FindControl<CustomRichTextBox>("RichTextBox");
        if (rtb == null || string.IsNullOrEmpty(FindText)) return;
        SetFindStatus(rtb.FindNext(FindText, MatchCase) ? "" : "찾을 수 없음");
    }

    private void FindPrev_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rtb = this.FindControl<CustomRichTextBox>("RichTextBox");
        if (rtb == null || string.IsNullOrEmpty(FindText)) return;
        SetFindStatus(rtb.FindPrev(FindText, MatchCase) ? "" : "찾을 수 없음");
    }

    private void ReplaceNext_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rtb = this.FindControl<CustomRichTextBox>("RichTextBox");
        if (rtb == null || string.IsNullOrEmpty(FindText)) return;
        SetFindStatus(rtb.ReplaceNext(FindText, ReplaceText, MatchCase) ? "" : "찾을 수 없음");
    }

    private void ReplaceAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var rtb = this.FindControl<CustomRichTextBox>("RichTextBox");
        if (rtb == null || string.IsNullOrEmpty(FindText)) return;
        int n = rtb.ReplaceAll(FindText, ReplaceText, MatchCase);
        SetFindStatus($"{n}개 바꿈");
    }

    private void CloseFind_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var bar = this.FindControl<Border>("FindBar");
        if (bar != null) bar.IsVisible = false;
        this.FindControl<CustomRichTextBox>("RichTextBox")?.Focus();
    }
    
    private void FontSizeComboBox_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var cb = sender as Avalonia.Controls.ComboBox;
        if (cb?.SelectedItem is Avalonia.Controls.ComboBoxItem item && double.TryParse(item.Content?.ToString(), out double size))
        {
            this.FindControl<CustomRichTextBox>("RichTextBox")?.SetFontSize(size);
        }
    }

    private void ColorBlack_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetForeground(Avalonia.Media.Brushes.Black);
    private void ColorRed_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetForeground(Avalonia.Media.Brushes.Red);
    private void ColorBlue_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetForeground(Avalonia.Media.Brushes.Blue);

    private void AlignLeft_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetTextAlignment(Avalonia.Media.TextAlignment.Left);
    private void AlignCenter_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetTextAlignment(Avalonia.Media.TextAlignment.Center);
    private void AlignRight_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetTextAlignment(Avalonia.Media.TextAlignment.Right);

    private void Spacing10_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetLineHeight(double.NaN);
    private void Spacing15_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetLineHeight(24);
    private void Spacing20_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetLineHeight(32);

    private void BulletButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.ToggleBullet();

    private void InsertTableButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.InsertTable(3, 3);
}