using Avalonia;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

/// <summary>
///     Public hyperlink / selection helpers. The editor has always rendered links
///     (<see cref="Run.NavigateUri" />), round-tripped them through HTML and opened them on click,
///     but hosts could not build their own link UI: selection state, the link hit test and the
///     "apply link" operation were all private. This partial exposes the minimum surface a host
///     needs (for example our toolbar button and the click menu in Corkboard).
/// </summary>
public partial class RichEditor
{
    /// <summary>
    ///     <see langword="true" /> when the selection covers at least one character.
    ///     A caret with no selection is <see langword="false" />.
    /// </summary>
    public bool HasSelection =>
        _selectionStart.Paragraph is not null
        && _selectionEnd.Paragraph is not null
        && _selectionStart.CompareTo(_selectionEnd) != 0;

    /// <summary>
    ///     Returns the hyperlink target of the link under <paramref name="point" /> (view coordinates),
    ///     or <see langword="null" /> when there is no link there.
    /// </summary>
    public string? GetHyperlinkAt(Point point)
    {
        var run = GetLinkRunAtPoint(point);
        return string.IsNullOrEmpty(run?.NavigateUri) ? null : run!.NavigateUri;
    }

    /// <summary>
    ///     Applies <paramref name="url" /> to the current selection (or clears the link when it is
    ///     <see langword="null" /> / empty). Returns <see langword="false" /> when nothing is selected,
    ///     the document is read-only, or there is no document.
    /// </summary>
    public bool TrySetHyperlink(string? url)
    {
        if (Document is null || IsReadOnly || !HasSelection)
            return false;

        SetHyperlink(url, null);
        return true;
    }

    /// <summary>
    ///     Applies <paramref name="url" /> to the link run under <paramref name="point" /> (or removes it
    ///     when the url is <see langword="null" /> / empty). The caret is collapsed at the click position
    ///     first, so an existing selection elsewhere is never modified by mistake.
    /// </summary>
    /// <returns><see langword="false" /> when there is no link at that point or the editor is read-only.</returns>
    public bool TrySetHyperlinkAt(Point point, string? url)
    {
        if (Document is null || IsReadOnly)
            return false;

        var run = GetLinkRunAtPoint(point);
        if (run is null)
            return false;

        var pointer = GetPositionFromPoint(point);
        if (pointer.Paragraph is null)
            return false;

        // Collapse the selection at the click position, mirroring a normal click: TextRange-based
        // edits inside SetHyperlink prefer the selection, so leaving an unrelated selection would
        // apply the url to the wrong text.
        _selectionStart = new TextPointer(pointer.Paragraph, pointer.Offset);
        _selectionEnd = new TextPointer(pointer.Paragraph, pointer.Offset);
        _caretPosition = new TextPointer(pointer.Paragraph, pointer.Offset);

        SetHyperlink(url, run);
        return true;
    }
}
