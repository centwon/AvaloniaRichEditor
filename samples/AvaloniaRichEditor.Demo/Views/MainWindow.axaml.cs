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

    private static readonly System.Collections.Generic.List<(string Label, Avalonia.Thickness Margin)> MarginPresets =
    [
        ("기본 48 / 40", new Avalonia.Thickness(48, 40, 48, 40)),
        ("좁게 16 / 12", new Avalonia.Thickness(16, 12, 16, 12)),
        ("넓게 120 / 96", new Avalonia.Thickness(120, 96, 120, 96)),
        ("비대칭 좌160 상24 우32 하120", new Avalonia.Thickness(160, 24, 32, 120)),
    ];

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Editor.FocusDocumentEnd();
    }
}
