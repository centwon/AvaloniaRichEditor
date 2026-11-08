using Avalonia;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

/// <summary>
///     Public hyperlink / selection helpers, so a host can build its own link UI (a toolbar button, a menu on
///     a clicked link) instead of relying on the built-in context menu.
/// </summary>
public partial class RichEditor
{
    /// <summary>
    ///     <see langword="true" /> when text, or a block of table cells, is selected.
    ///     A caret with no selection is <see langword="false" />.
    /// </summary>
    public bool HasSelection => HasTextOrCellSelection;

    /// <summary>
    ///     Returns the target of the link under <paramref name="point" />, or <see langword="null" /> when there is
    ///     no link there. The point is relative to this editor, as <c>e.GetPosition(editor)</c> gives it.
    /// </summary>
    public string? GetHyperlinkAt(Point point)
    {
        var uri = GetLinkRunAtPoint(MapViewToDoc(point))?.NavigateUri;
        return string.IsNullOrEmpty(uri) ? null : uri;
    }

    /// <summary>
    ///     Links the selection to <paramref name="url" /> — or, with no selection, the word at the caret, like the
    ///     other formatting commands. <see langword="null" /> or empty removes the link. Does nothing when the
    ///     editor is read-only.
    /// </summary>
    public void SetHyperlink(string? url) => SetHyperlink(string.IsNullOrEmpty(url) ? null : url, null);

    /// <summary>
    ///     Changes the target of the link under <paramref name="point" /> to <paramref name="url" />, or removes the
    ///     link when it is <see langword="null" /> or empty. The selection is left as it is. The point is relative
    ///     to this editor, as <c>e.GetPosition(editor)</c> gives it.
    /// </summary>
    /// <returns><see langword="false" /> when there is no link at that point or the editor is read-only.</returns>
    public bool TrySetHyperlinkAt(Point point, string? url)
    {
        if (Document == null || IsReadOnly) return false;
        if (GetLinkRunAtPoint(MapViewToDoc(point)) is not { NavigateUri.Length: > 0 } run) return false;
        if (string.IsNullOrEmpty(url)) url = null;
        if (run.NavigateUri == url) return true; // unchanged: no empty undo step
        PushUndo();
        run.NavigateUri = url;
        InvalidateVisual();
        return true;
    }
}
