namespace AvaloniaRichEditor.Controls;

/// <summary>How a <see cref="RichEditor"/> shows its document on screen. The page setup (<see cref="RichEditorPageSize"/>,
/// margins, header and footer) is the DOCUMENT's and is kept, saved and used for printing either way — like a word
/// processor's draft view against its print layout.</summary>
public enum RichEditorViewMode
{
    /// <summary>The text reflows to the control's width with no pages drawn, whatever paper the document names. The
    /// editor's default, so a small editing box shows any document as a box of text.</summary>
    Continuous = 0,
    /// <summary>Pages on a desk at the document's paper size (a continuous paper still reflows). The default of
    /// <see cref="RichEditorView"/>.</summary>
    Page = 1,
}
