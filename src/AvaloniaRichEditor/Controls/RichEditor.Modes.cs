using System;
using Avalonia;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Feature flags (roadmap N3.5). Capability is expressed directly through IsReadOnly (the core
// viewer/editor switch) and the individual Allow* flags — there is no bundled preset. Flags are
// consulted at the guard sites in the key/paste/drop handlers, the public insert commands, the context
// menu, and find/replace. ReadOnly additionally disables the caret blink, IME, and undo history (see
// OnReadOnlyChanged).
public partial class RichEditor
{
    /// <inheritdoc cref="AllowImages"/>
    public static readonly StyledProperty<bool> AllowImagesProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(AllowImages), true);

    /// <summary>When false, image insertion (command, paste, drag-drop, context menu) is blocked.</summary>
    public bool AllowImages
    {
        get => GetValue(AllowImagesProperty);
        set => SetValue(AllowImagesProperty, value);
    }

    /// <inheritdoc cref="AllowTables"/>
    public static readonly StyledProperty<bool> AllowTablesProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(AllowTables), true);

    /// <summary>When false, table insertion (command, tabular paste, context menu) is blocked.</summary>
    public bool AllowTables
    {
        get => GetValue(AllowTablesProperty);
        set => SetValue(AllowTablesProperty, value);
    }

    /// <inheritdoc cref="AllowRichPaste"/>
    public static readonly StyledProperty<bool> AllowRichPasteProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(AllowRichPaste), true);

    /// <summary>When false, paste falls back to plain text (no internal rich/HTML structure).</summary>
    public bool AllowRichPaste
    {
        get => GetValue(AllowRichPasteProperty);
        set => SetValue(AllowRichPasteProperty, value);
    }

    /// <inheritdoc cref="AllowFindReplace"/>
    public static readonly StyledProperty<bool> AllowFindReplaceProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(AllowFindReplace), true);

    /// <summary>When false, the find/replace commands are no-ops.</summary>
    public bool AllowFindReplace
    {
        get => GetValue(AllowFindReplaceProperty);
        set => SetValue(AllowFindReplaceProperty, value);
    }

    /// <inheritdoc cref="AllowLocalFileImages"/>
    public static readonly StyledProperty<bool> AllowLocalFileImagesProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(AllowLocalFileImages), true);

    /// <summary>When false, <c>file://</c> image sources in ingested HTML (paste, <see cref="LoadHtml"/>,
    /// <see cref="InsertHtml"/>) are skipped instead of being read from disk and embedded — closing the
    /// path by which untrusted HTML can pull local files into the document. Default true (HTML copied
    /// from local files keeps its images). Independent of <see cref="IsReadOnly"/> and the other flags.</summary>
    public bool AllowLocalFileImages
    {
        get => GetValue(AllowLocalFileImagesProperty);
        set => SetValue(AllowLocalFileImagesProperty, value);
    }

    /// <inheritdoc cref="AllowRemoteImagesOnPaste"/>
    public static readonly StyledProperty<bool> AllowRemoteImagesOnPasteProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(AllowRemoteImagesOnPaste), true);

    /// <summary>When false, remote (<c>http</c>/<c>https</c>) <c>&lt;img&gt;</c> sources in pasted HTML are
    /// not fetched — closing the path by which pasting web content silently issues network requests (e.g.
    /// to tracking pixels). Default true. <c>data:</c> and <c>file:</c> images are unaffected. Independent
    /// of <see cref="IsReadOnly"/> and the other flags.</summary>
    public bool AllowRemoteImagesOnPaste
    {
        get => GetValue(AllowRemoteImagesOnPasteProperty);
        set => SetValue(AllowRemoteImagesOnPasteProperty, value);
    }

    /// <inheritdoc cref="MaxRecommendedImages"/>
    public static readonly StyledProperty<int> MaxRecommendedImagesProperty =
        AvaloniaProperty.Register<RichEditor, int>(nameof(MaxRecommendedImages), 50);

    /// <summary>
    /// Soft limit on the document's image count (block, inline, and table-cell images). When the count
    /// first exceeds this value, <see cref="RecommendedImageLimitExceeded"/> is raised once; editing is
    /// never blocked. Benchmarks (800×600 photos): smooth up to ~50 images, scroll fps and the first
    /// keystroke's undo clone degrade around 100. Viewer (ReadOnly) hosts can safely raise this to 100+.
    /// Zero or negative disables the warning. Default 50.
    /// </summary>
    public int MaxRecommendedImages
    {
        get => GetValue(MaxRecommendedImagesProperty);
        set => SetValue(MaxRecommendedImagesProperty, value);
    }

    /// <summary>
    /// Raised (once per crossing) when the document's image count exceeds <see cref="MaxRecommendedImages"/>,
    /// so the host can warn the user about likely performance degradation. Re-arms when the count drops
    /// back to the limit or below. Query <see cref="GetImageCount"/> for the current count.
    /// </summary>
    public event EventHandler? RecommendedImageLimitExceeded;

    // True after the limit warning fired; cleared when the count returns to the limit or below.
    private bool _imageLimitNotified;

    /// <summary>Counts the document's images: top-level <see cref="ImageBlock"/>s plus
    /// <see cref="InlineImage"/>s in paragraphs and table cells.</summary>
    public int GetImageCount()
    {
        var doc = Document;
        if (doc == null) return 0;
        int n = 0;
        foreach (var b in doc.Blocks)
        {
            switch (b)
            {
                case ImageBlock: n++; break;
                case Paragraph p: n += CountInlineImages(p); break;
                case TableBlock t:
                    foreach (var (_, _, cell) in t.LogicalCells())
                        foreach (var cb in cell.Blocks)
                        {
                            if (cb is Paragraph cp) n += CountInlineImages(cp);
                            else if (cb is ImageBlock) n++; // P4-2a: block images in cells count too
                        }
                    break;
            }
        }
        return n;

        static int CountInlineImages(Paragraph p)
        {
            int k = 0;
            foreach (var i in p.Inlines) if (i is InlineImage) k++;
            return k;
        }
    }

    // Edge-triggered soft-limit check, run after each flushed text change (see RaisePendingChangeEvents).
    internal void CheckImageLimit()
    {
        int limit = MaxRecommendedImages;
        if (limit <= 0) { _imageLimitNotified = false; return; }
        if (GetImageCount() > limit)
        {
            if (!_imageLimitNotified)
            {
                _imageLimitNotified = true;
                RecommendedImageLimitExceeded?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            _imageLimitNotified = false;
        }
    }

    // ReadOnly perf/optimization: a viewer needs no blinking caret (2 Hz repaint), no IME machinery,
    // and no undo history. Centralized here so it fires on any IsReadOnly assignment.
    private void OnReadOnlyChanged(bool readOnly)
    {
        if (readOnly)
        {
            _caretTimer.Stop();
            _isCaretVisible = false;
            _undoManager.Clear();
        }
        else if (IsFocused)
        {
            _isCaretVisible = true;
            _caretTimer.Start();
        }
        // Let assistive tech know the control's editability flipped (exposed via the Value pattern's
        // IsReadOnly). No-op until an automation peer has been created.
        _automationPeer?.NotifyReadOnlyChanged(!readOnly, readOnly);
        InvalidateVisual();
    }
}
