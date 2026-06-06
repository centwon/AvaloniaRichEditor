using Avalonia.Controls;
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
    }
}