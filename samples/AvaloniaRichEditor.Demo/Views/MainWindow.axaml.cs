using System;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Demo.Views;

// The demo window is nothing but the library's RichEditorView. The toolbar (formatting, page size,
// orientation, outline, zoom) and the bottom status bar are all built into the view; Export/Import use
// the view's built-in file actions. The only app-specific wiring is Print, which is platform-specific
// (PrintPreviewWindow uses Windows System.Drawing) and so is delegated via RichEditorView.PrintRequested.
public partial class MainWindow : Window
{
    private RichEditor Editor => EditorView.Editor;

    public MainWindow()
    {
        InitializeComponent();

        Editor.ShowPageNumbers = true;
        Editor.Document = BuildSampleDocument();

        // Print is platform-specific; the view raises this and the app drives its own preview/printing.
        EditorView.PrintRequested += (_, _) => new PrintPreviewWindow(Editor).Show(this);
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
}
