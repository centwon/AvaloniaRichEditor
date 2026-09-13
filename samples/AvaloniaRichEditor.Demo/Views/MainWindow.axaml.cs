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
        // The full right-click menu (character · paragraph · list · heading groups) while editing, as the WinUI
        // demo shows it — without it the two demos' menus differed and the list items could not be found here.
        Editor.ShowFormattingMenu = true;
        Editor.Document = SampleDocument.Build();

        // Print is platform-specific; the view raises this and the app drives its own preview/printing.
        EditorView.PrintRequested += (_, _) => new PrintPreviewWindow(Editor).Show(this);

        // Viewer mode. The view has no switch for it, so without this the read-only behaviour (a table's
        // right-click Copy, the table-border click) could not be checked in the demo at all.
        ReadOnlyToggle.IsCheckedChanged += (_, _) =>
        {
            Editor.IsReadOnly = ReadOnlyToggle.IsChecked == true;
            Editor.ShowFormattingMenu = !Editor.IsReadOnly;
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Editor.FocusDocumentEnd();
    }
}
