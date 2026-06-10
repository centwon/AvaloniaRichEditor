using Avalonia;

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

    public static readonly StyledProperty<bool> AllowImagesProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(AllowImages), true);

    /// <summary>When false, image insertion (command, paste, drag-drop, context menu) is blocked.</summary>
    public bool AllowImages
    {
        get => GetValue(AllowImagesProperty);
        set => SetValue(AllowImagesProperty, value);
    }

    public static readonly StyledProperty<bool> AllowTablesProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(AllowTables), true);

    /// <summary>When false, table insertion (command, tabular paste, context menu) is blocked.</summary>
    public bool AllowTables
    {
        get => GetValue(AllowTablesProperty);
        set => SetValue(AllowTablesProperty, value);
    }

    public static readonly StyledProperty<bool> AllowRichPasteProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(AllowRichPaste), true);

    /// <summary>When false, paste falls back to plain text (no internal rich/HTML structure).</summary>
    public bool AllowRichPaste
    {
        get => GetValue(AllowRichPasteProperty);
        set => SetValue(AllowRichPasteProperty, value);
    }

    public static readonly StyledProperty<bool> AllowFindReplaceProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(AllowFindReplace), true);

    /// <summary>When false, the find/replace commands are no-ops.</summary>
    public bool AllowFindReplace
    {
        get => GetValue(AllowFindReplaceProperty);
        set => SetValue(AllowFindReplaceProperty, value);
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
        InvalidateVisual();
    }
}
