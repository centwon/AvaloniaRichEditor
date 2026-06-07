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

            // Status bar: live character/word count and caret line/column.
            richTextBox.StatusChanged += (_, _) => UpdateStatusBar();
            UpdateStatusBar();
        }
    }

    private void UpdateStatusBar()
    {
        var richTextBox = this.FindControl<CustomRichTextBox>("RichTextBox");
        var status = this.FindControl<TextBlock>("StatusBar");
        if (richTextBox == null || status == null) return;
        var (chars, words, line, col) = richTextBox.GetStatus();
        status.Text = $"글자 {chars}   단어 {words}   줄 {line}, 칸 {col}";
    }

    // Loads a built-in document that exercises every feature at once (via the HTML parser, so it also
    // checks ParseHtml). Useful for eyeballing rendering — especially the merged-cell table.
    private void SampleButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var richTextBox = this.FindControl<CustomRichTextBox>("RichTextBox");
        if (richTextBox == null) return;
        richTextBox.Document = AvaloniaRichTextBoxPort.Formatters.HtmlDocumentFormatter.ParseHtml(SampleHtml);
        richTextBox.InvalidateVisual();
    }

    private const string SampleHtml = @"
<h1>예제 문서 — 전체 기능 테스트</h1>
<p>인라인 서식: <b>굵게</b>, <i>기울임</i>, <u>밑줄</u>, <s>취소선</s>,
<span style='color:#c00000'>빨강 글자</span>,
<span style='background-color:#fff2a8'>형광펜</span>,
<span style='font-size:20px'>큰 글자</span>,
<span style='font-family:Georgia'>다른 글꼴</span>,
<a href='https://avaloniaui.net'>하이퍼링크</a>.</p>

<h2>목록</h2>
<ul><li>글머리 항목 하나</li><li>글머리 항목 둘</li></ul>
<ol><li>번호 항목 하나</li><li>번호 항목 둘</li></ol>
<blockquote>인용문 블록입니다. 좌측에 세로 바가 표시됩니다.</blockquote>
<hr/>

<h2>문단 정렬</h2>
<p style='text-align:center'>가운데 정렬된 문단</p>
<p style='text-align:right'>오른쪽 정렬된 문단</p>

<h2>표 — 셀 병합(colspan / rowspan)</h2>
<table border='1'>
  <tr><td colspan='3' style='background-color:#dbe5f1'>머리글 (가로 3칸 병합)</td></tr>
  <tr><td rowspan='2'>세로 2칸<br/>병합</td><td>B1</td><td>C1</td></tr>
  <tr><td>B2</td><td>C2</td></tr>
  <tr><td>A3</td><td colspan='2'>가로 2칸 병합</td></tr>
</table>
<p>표 안에서 셀을 가로/세로로 드래그 선택한 뒤 우클릭 → <b>셀 병합</b> / <b>병합 해제</b>를 시험해 보세요.</p>
";

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

    private void NumberingButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.ToggleNumbering();
    private void Heading1_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetHeading(1);
    private void Heading2_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetHeading(2);
    private void Heading3_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetHeading(3);
    private void HeadingBody_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetHeading(0);
    private void HighlightYellow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetHighlight(Avalonia.Media.Brushes.Yellow);
    private void HighlightNone_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.SetHighlight(null);
    private void IndentInc_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.Indent(20);
    private void IndentDec_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.Indent(-20);
    private void DividerButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => this.FindControl<CustomRichTextBox>("RichTextBox")?.InsertDivider();

    private void FontFamilyComboBox_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if ((sender as Avalonia.Controls.ComboBox)?.SelectedItem is Avalonia.Controls.ComboBoxItem item && item.Content is string fam)
            this.FindControl<CustomRichTextBox>("RichTextBox")?.SetFontFamily(fam);
    }
}