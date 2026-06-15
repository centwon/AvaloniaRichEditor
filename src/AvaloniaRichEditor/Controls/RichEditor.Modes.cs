using System;
using Avalonia;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

/// <summary>
/// Capability preset for <see cref="RichEditor"/>. Setting <see cref="RichEditor.EditorMode"/>
/// applies a bundle of feature flags at once; individual flags can still be overridden afterwards.
/// </summary>
public enum EditorMode
{
    /// <summary>View-only: renders content, allows selection/copy, but blocks all editing.</summary>
    ReadOnly,
    /// <summary>Text + basic character/paragraph formatting only — no images, tables, or rich paste.</summary>
    Basic,
    /// <summary>Everything: images, tables, rich paste, find/replace.</summary>
    Full
}

// Editor modes and feature flags (roadmap N3.5). Default is Full, so existing hosts see no behaviour
// change. Flags are consulted at the guard sites in the key/paste/drop handlers, the public insert
// commands, the context menu, and find/replace. ReadOnly additionally disables the caret blink, IME,
// and undo history (see OnReadOnlyChanged).
public partial class RichEditor
{
    /// <inheritdoc cref="EditorMode"/>
    public static readonly StyledProperty<EditorMode> EditorModeProperty =
        AvaloniaProperty.Register<RichEditor, EditorMode>(nameof(EditorMode), EditorMode.Full);

    /// <summary>
    /// Capability preset. Assigning this applies a bundle of feature flags (and toggles
    /// <see cref="IsReadOnly"/>); individual flags may be overridden afterwards.
    /// </summary>
    public EditorMode EditorMode
    {
        get => GetValue(EditorModeProperty);
        set => SetValue(EditorModeProperty, value);
    }

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
    /// from local files keeps its images). Not part of the <see cref="EditorMode"/> presets.</summary>
    public bool AllowLocalFileImages
    {
        get => GetValue(AllowLocalFileImagesProperty);
        set => SetValue(AllowLocalFileImagesProperty, value);
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
                    foreach (var (_, _, cell) in t.LogicalCells()) n += CountInlineImages(cell);
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

    // Writes the flag bundle for the chosen preset. Setting IsReadOnly here cascades into
    // OnReadOnlyChanged (caret/IME/undo handling). Hosts may override individual flags afterwards.
    private void ApplyEditorModePreset(EditorMode mode)
    {
        switch (mode)
        {
            case EditorMode.ReadOnly:
                AllowImages = false; AllowTables = false; AllowRichPaste = false; AllowFindReplace = false;
                IsReadOnly = true;
                break;
            case EditorMode.Basic:
                AllowImages = false; AllowTables = false; AllowRichPaste = false; AllowFindReplace = false;
                IsReadOnly = false;
                break;
            case EditorMode.Full:
                AllowImages = true; AllowTables = true; AllowRichPaste = true; AllowFindReplace = true;
                IsReadOnly = false;
                break;
        }
    }

    // ReadOnly perf/optimization: a viewer needs no blinking caret (2 Hz repaint), no IME machinery,
    // and no undo history. Centralized here so it fires whether ReadOnly arrives via EditorMode or a
    // direct IsReadOnly assignment.
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
