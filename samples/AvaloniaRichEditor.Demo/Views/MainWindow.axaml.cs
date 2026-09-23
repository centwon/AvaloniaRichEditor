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

        // Page margins (1.3.0). The view's toolbar has paper and orientation but no margins, and the
        // margin band is where the header, the footer and the page number are drawn — the asymmetric
        // preset is the one that tells "four sides" apart from "two", on screen and in print.
        MarginPicker.ItemsSource = MarginPresets.ConvertAll(p => p.Label);
        MarginPicker.SelectedIndex = 0;
        MarginPicker.SelectionChanged += (_, _) =>
        {
            int i = MarginPicker.SelectedIndex;
            if (i >= 0 && i < MarginPresets.Count) Editor.PageMargin = MarginPresets[i].Margin;
        };
    }

    // Millimetres, like the toolbar's own picker — these exist to reach shapes the toolbar's five steps
    // do not: a band too thin for the header, and an asymmetric one (which tells "four sides" from "two").
    private static readonly System.Collections.Generic.List<(string Label, AvaloniaRichEditor.Documents.PageMargins Margin)> MarginPresets =
    [
        ("기본 12.7 / 10.6mm", AvaloniaRichEditor.Documents.PageSetup.DefaultMargin),
        ("아주 좁게 4 / 3mm", new AvaloniaRichEditor.Documents.PageMargins(4, 3, 4, 3)),
        ("아주 넓게 40 / 30mm", new AvaloniaRichEditor.Documents.PageMargins(40, 30, 40, 30)),
        ("비대칭 좌42 상6 우8 하31mm", new AvaloniaRichEditor.Documents.PageMargins(42, 6, 8, 31)),
    ];

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Editor.FocusDocumentEnd();
    }
}
