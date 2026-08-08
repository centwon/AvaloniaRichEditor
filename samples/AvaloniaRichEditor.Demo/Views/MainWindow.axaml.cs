using System;
using Avalonia.Controls;
using AvaloniaRichEditor.Controls;

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
        Editor.Document = SampleDocument.Build();

        // Print is platform-specific; the view raises this and the app drives its own preview/printing.
        EditorView.PrintRequested += (_, _) => new PrintPreviewWindow(Editor).Show(this);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Editor.FocusDocumentEnd();
    }
}
