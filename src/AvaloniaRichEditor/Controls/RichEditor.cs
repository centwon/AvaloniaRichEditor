using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

using System.Threading.Tasks;
using System.Collections.Generic;

/// <summary>A from-scratch rich text editor built on Avalonia's <c>TextLayout</c> engine.
/// Supports inline formatting, paragraphs, lists, tables, images, HTML/JSON import-export,
/// find/replace, undo/redo, CJK IME, and editor-mode presets (ReadOnly/Basic/Full).</summary>
public partial class RichEditor : Control
{

    private DispatcherTimer _caretTimer;
    private UndoManager _undoManager = new UndoManager();
    private bool _isCaretVisible;
    private TextPointer _caretPosition = new TextPointer(null, 0);
    private TextPointer _selectionStart = new TextPointer(null, 0);
    private TextPointer _selectionEnd = new TextPointer(null, 0);
    private bool _isSelecting = false;
    private Point _lastCaretPoint;
    private double _lastCaretHeight = 20; // line height at the caret (tall on lines with inline images)
    private Block? _selectedBlock; // a block (image/table) selected by clicking it (deletable with Del/Backspace)
    // A "block caret" sits before/after an image/table: Space/Tab indent it, Backspace outdents/deletes,
    // arrows step before -> (table cells) -> after -> next text. Set by clicking or arrow navigation.
    private Block? _caretBlock;
    private bool _caretBlockAfter; // false = caret before the block, true = caret after it

    // Internal rich clipboard: preserves run formatting for copy/paste within the app.
    // The plain text put on the system clipboard is mirrored here; on paste we use the rich
    // version only when the system clipboard text still matches what we copied.
    private static List<Run>? _internalClipboard;
    private static string? _internalClipboardText;
    // When the copied selection spans whole tables/images or multiple top-level blocks, we also
    // keep cloned block structure so paste can rebuild tables instead of flattening to text.
    private static List<Block>? _internalClipboardBlocks;

    // Resizing state
    private List<(Avalonia.Rect rect, TableBlock tb, int colIndex)> _columnBoundaries = new();
    private bool _isResizingColumn;
    private TableBlock? _resizingTable;
    private int _resizingColumnIndex;
    private double _initialMouseX;
    private double _initialColumnWidth;
    private double _initialNextColumnWidth;
    private bool _resizingLastColumn;

    // Row resize state (mirrors column resize; dragging a row's bottom edge sets its min height).
    private List<(Avalonia.Rect rect, TableBlock tb, int rowIndex, double height)> _rowBoundaries = new();
    private bool _isResizingRow;
    private TableBlock? _resizingRowTable;
    private int _resizingRowIndex;
    private double _initialMouseY;
    private double _initialRowHeight;

    // Table interaction mode (HWP-style). In cell-selection mode a click selects whole cells (drag =
    // a block); a double-click drops back into text-editing mode (a caret). Default = text editing.
    private bool _cellSelMode;
    private TableBlock? _cellSelTable;

    // Image resize state
    private List<(Avalonia.Rect rect, ImageBlock img)> _imageHandles = new();
    private bool _isResizingImage;
    private ImageBlock? _resizingImage;
    private double _initialImageWidth;
    private double _initialImageHeight;
    private double _initialImageMouseX;
    private double _imageAspect;

    // Inline-image selection + resize (mirrors the block-image handle pattern). The on-screen rect
    // of every visible inline image is rebuilt each Render pass for click hit-testing; the resize
    // handle exists only for the selected image. Initial size/aspect state above is shared.
    private readonly List<(Avalonia.Rect rect, Paragraph p, InlineImage img)> _inlineImageRects = new();
    private readonly List<(Avalonia.Rect rect, Paragraph p, InlineImage img)> _inlineHandles = new();
    private (Paragraph p, InlineImage img)? _selectedInline;
    private bool _isResizingInline;
    private InlineImage? _resizingInline;

    /// <inheritdoc cref="Document"/>
    public static readonly StyledProperty<FlowDocument?> DocumentProperty =
        AvaloniaProperty.Register<RichEditor, FlowDocument?>(nameof(Document));

    /// <summary>The document model being edited. Assigning a new instance replaces the document
    /// and fires <see cref="DocumentChanged"/>.</summary>
    public FlowDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <inheritdoc cref="IsReadOnly"/>
    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(IsReadOnly));

    /// <summary>When <see langword="true"/>, text input, edits, paste, and resizing are blocked;
    /// selection and copy still work, and the caret is hidden. Equivalent to
    /// <c>EditorMode = EditorMode.ReadOnly</c>.</summary>
    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <inheritdoc cref="SelectionBrush"/>
    public static readonly StyledProperty<IBrush> SelectionBrushProperty =
        AvaloniaProperty.Register<RichEditor, IBrush>(
            nameof(SelectionBrush), new SolidColorBrush(Color.FromArgb(80, 0, 120, 215)));

    /// <summary>Fill brush for the text/cell selection highlight.</summary>
    public IBrush SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <inheritdoc cref="CaretBrush"/>
    public static readonly StyledProperty<IBrush> CaretBrushProperty =
        AvaloniaProperty.Register<RichEditor, IBrush>(nameof(CaretBrush), Brushes.Black);

    /// <summary>Brush for the blinking text caret.</summary>
    public IBrush CaretBrush
    {
        get => GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    // The OS default UI font (Windows message font, e.g. "맑은 고딕" on Korean Windows) so
    // unstyled documents look native rather than using the app's Avalonia font (often Inter).
    // Platforms without the query keep Avalonia's default.
    private static FontFamily SystemDefaultFontFamily() =>
        SystemFontInfo.MessageFontFaceName() is { } name ? new FontFamily(name) : FontFamily.Default;

    /// <inheritdoc cref="DefaultFontFamily"/>
    public static readonly StyledProperty<FontFamily> DefaultFontFamilyProperty =
        AvaloniaProperty.Register<RichEditor, FontFamily>(
            nameof(DefaultFontFamily), SystemDefaultFontFamily());

    /// <summary>Font family used for runs that don't specify one. Defaults to the OS UI font
    /// (e.g. 맑은 고딕 on Korean Windows); assign to override.</summary>
    public FontFamily DefaultFontFamily
    {
        get => GetValue(DefaultFontFamilyProperty);
        set => SetValue(DefaultFontFamilyProperty, value);
    }

    /// <inheritdoc cref="DefaultFontSize"/>
    public static readonly StyledProperty<double> DefaultFontSizeProperty =
        AvaloniaProperty.Register<RichEditor, double>(nameof(DefaultFontSize), 14.0);

    /// <summary>Font size used for runs that don't specify one.</summary>
    public double DefaultFontSize
    {
        get => GetValue(DefaultFontSizeProperty);
        set => SetValue(DefaultFontSizeProperty, value);
    }

    // Cached system font list. The OS reports family names localized to the UI language (e.g.
    // "맑은 고딕" on Korean Windows), and those names resolve back through the same font manager,
    // so they are used as-is for both display and application.
    private static IReadOnlyList<string>? _systemFontChoices;

    private static IReadOnlyList<string> SystemFontChoices()
    {
        if (_systemFontChoices != null) return _systemFontChoices;
        var names = new List<string>();
        foreach (var f in FontManager.Current.SystemFonts) names.Add(f.Name);
        names.Sort(StringComparer.Create(System.Globalization.CultureInfo.CurrentUICulture, ignoreCase: true));
        // Platforms without font enumeration (e.g. headless): widely-available fallback families.
        if (names.Count == 0)
            names.AddRange(new[] { "Arial", "Times New Roman", "Courier New", "Verdana", "Georgia" });
        return _systemFontChoices = names;
    }

    /// <inheritdoc cref="FontFamilyChoices"/>
    public static readonly StyledProperty<IReadOnlyList<string>> FontFamilyChoicesProperty =
        AvaloniaProperty.Register<RichEditor, IReadOnlyList<string>>(
            nameof(FontFamilyChoices), Array.Empty<string>());

    /// <summary>Font families offered in the font pickers (right-click submenu and
    /// <see cref="RichEditorToolbar"/> combo). Defaults to the installed system fonts, sorted for —
    /// and with names localized by — the OS UI language; assign a non-empty list to curate.</summary>
    public IReadOnlyList<string> FontFamilyChoices
    {
        get
        {
            var v = GetValue(FontFamilyChoicesProperty);
            return v.Count > 0 ? v : SystemFontChoices();
        }
        set => SetValue(FontFamilyChoicesProperty, value);
    }

    static RichEditor()
    {
        AffectsRender<RichEditor>(DocumentProperty, SelectionBrushProperty, CaretBrushProperty);
        // EditorMode preset writes the feature-flag bundle; ReadOnly drives caret/IME/undo optimization
        // (whether it arrives via the preset or a direct IsReadOnly assignment). See RichEditor.Modes.cs.
        EditorModeProperty.Changed.AddClassHandler<RichEditor>((x, e) => x.ApplyEditorModePreset(e.GetNewValue<EditorMode>()));
        IsReadOnlyProperty.Changed.AddClassHandler<RichEditor>((x, e) => x.OnReadOnlyChanged(e.GetNewValue<bool>()));
    }

    private readonly RtbInputMethodClient _imClient;
    private string? _preeditText; // IME composition text shown inline at the caret while composing.

    // Per-paragraph TextLayout cache. Building a TextLayout shapes/line-breaks text (the most expensive
    // step), and Render + Measure + every hit-test would otherwise rebuild every paragraph each frame —
    // crippling for large documents and the 2 Hz caret blink. Keyed by paragraph; reused while the
    // paragraph's content signature and wrap width are unchanged.
    private readonly Dictionary<Paragraph, (long sig, double width, Avalonia.Media.TextFormatting.TextLayout layout)> _layoutCache = new();

    /// <summary>Initializes a new <see cref="RichEditor"/> with a single empty paragraph and default settings.</summary>
    public RichEditor()
    {
        Focusable = true;
        Cursor = IbeamCursor;

        // Enable IME (Korean/Japanese/Chinese) composition by advertising a text-input client.
        _imClient = new RtbInputMethodClient(this);
        AddHandler(Avalonia.Input.InputElement.TextInputMethodClientRequestedEvent, OnTextInputMethodClientRequested);

        // Drag & drop images onto the editor.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        _caretTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _caretTimer.Tick += (s, e) =>
        {
            _isCaretVisible = !_isCaretVisible;
            InvalidateVisual();
        };
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            _layoutCache.Clear();
            if (Document != null) UpdateParents(Document);
            _textChangedPending = true; // wholesale content swap
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            InvalidateMeasure();
            InvalidateVisual();
        }
        if (change.Property == DefaultFontFamilyProperty || change.Property == DefaultFontSizeProperty)
        {
            _layoutCache.Clear(); // default font feeds the cached layouts
            InvalidateMeasure();
            InvalidateVisual();
        }
        if (change.Property == IsFocusedProperty)
        {
            if (IsFocused)
            {
                _isCaretVisible = true;
                _caretTimer.Start();
            }
            else
            {
                _caretTimer.Stop();
                _isCaretVisible = false;
            }
            InvalidateVisual();
        }
    }

    private void DoUndo()
    {
        if (Document == null) return;
        var state = _undoManager.Undo(Document, _caretPosition);
        if (state == null) return;
        Document = state.Value.Document;
        UpdateParents(Document);
        _caretPosition = _undoManager.GetPointerFromGlobalIndex(Document, state.Value.CaretGlobalIndex);
        _caretPosition.Offset = state.Value.CaretOffset;
        _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        MarkTextChanged();
        InvalidateVisual();
        NotifyStatus();
    }

    private void DoRedo()
    {
        if (Document == null) return;
        var state = _undoManager.Redo(Document, _caretPosition);
        if (state == null) return;
        Document = state.Value.Document;
        UpdateParents(Document);
        _caretPosition = _undoManager.GetPointerFromGlobalIndex(Document, state.Value.CaretGlobalIndex);
        _caretPosition.Offset = state.Value.CaretOffset;
        _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        MarkTextChanged();
        InvalidateVisual();
        NotifyStatus();
    }

    private void ResetCaretBlink()
    {
        _isCaretVisible = true;
        _caretTimer.Stop();
        _caretTimer.Start();
        _imClient.NotifyCaretChanged();
        _bringCaretIntoView = true;
        // Any caret move / selection change breaks the undo-coalescing run — except when the
        // current key handler re-armed it (Backspace/Delete move the caret by design).
        _editRun = _editRunRearm;
        _editRunRearm = EditRunKind.None;
        InvalidateVisual();
        NotifyStatus();
    }

    private bool _bringCaretIntoView;

    /// <summary>Raised whenever the caret moves or the document changes, so a host status bar can refresh.
    /// Coarse signal — prefer <see cref="TextChanged"/> or <see cref="SelectionChanged"/> for new code.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Raised after the document's text, structure, or formatting is modified.</summary>
    public event EventHandler? TextChanged;

    /// <summary>Raised when the caret position or the selected range changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the <see cref="Document"/> is replaced with a different instance.</summary>
    public event EventHandler? DocumentChanged;

    // Set by any edit; flushed (as TextChanged) from Render, off the render stack, so handlers see the
    // already-mutated document and can't re-enter the render pass.
    private bool _textChangedPending;
    private void MarkTextChanged() => _textChangedPending = true;

    // Kind of edit currently coalescing into one undo checkpoint. A run of same-kind edits shares
    // a single full-document clone (the first-keystroke clone was measured at 162 ms on a
    // 100-image document, so per-keypress clones produce a visible hitch when holding a key).
    private enum EditRunKind { None, Typing, Backspace, Delete }
    private EditRunKind _editRun;
    // Backspace/Delete handlers re-arm the run through this before their trailing ResetCaretBlink
    // (which otherwise ends the run, since those keys move the caret by design).
    private EditRunKind _editRunRearm;

    // Records an undo checkpoint and flags a text change. Single choke point for discrete
    // document mutations; also ends any in-progress coalescing run so the next keystroke
    // checkpoints afresh.
    private void PushUndo()
    {
        if (Document != null) _undoManager.PushState(Document, _caretPosition);
        _textChangedPending = true;
        _editRun = EditRunKind.None;
        _editRunRearm = EditRunKind.None;
    }

    // Undo checkpoint for typed text: coalesces a run of consecutive keystrokes into a single
    // checkpoint. The run ends on any caret move, selection change, or discrete edit
    // (see ResetCaretBlink / PushUndo).
    private void PushUndoTyping()
    {
        if (_editRun != EditRunKind.Typing)
        {
            if (Document != null) _undoManager.PushState(Document, _caretPosition);
            _editRun = EditRunKind.Typing;
        }
        _textChangedPending = true;
    }

    // Undo checkpoint for a plain single-character Backspace/Delete: consecutive same-direction
    // deletes share one checkpoint, mirroring PushUndoTyping. Structural deletes (selection,
    // paragraph merge, block/image removal) go through PushUndo instead.
    private void PushUndoDeleting(bool backspace)
    {
        var kind = backspace ? EditRunKind.Backspace : EditRunKind.Delete;
        if (_editRun != kind && Document != null) _undoManager.PushState(Document, _caretPosition);
        _editRunRearm = kind;
        _textChangedPending = true;
    }

    // Last-seen selection, to fire SelectionChanged only on real movement (not on every repaint/blink).
    private (Paragraph? cp, int co, Paragraph? ss, int so, Paragraph? se, int eo) _selSnapshot;
    private bool SelectionMovedSinceLastSnapshot()
    {
        var now = (_caretPosition.Paragraph, _caretPosition.Offset,
                   _selectionStart.Paragraph, _selectionStart.Offset,
                   _selectionEnd.Paragraph, _selectionEnd.Offset);
        if (now == _selSnapshot) return false;
        _selSnapshot = now;
        return true;
    }

    // Dispatches any pending TextChanged/SelectionChanged after the current render, avoiding re-entrancy.
    private void RaisePendingChangeEvents()
    {
        bool text = _textChangedPending;
        _textChangedPending = false;
        bool sel = SelectionMovedSinceLastSnapshot();
        if (!text && !sel) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (text)
            {
                TextChanged?.Invoke(this, EventArgs.Empty);
                CheckImageLimit();
            }
            if (sel) SelectionChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void NotifyStatus()
    {
        InvalidateMeasure(); // content height may have changed -> let the ScrollViewer update its extent
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Snapshot of the formatting at the caret position, for toolbar state reflection.
    /// Obtain via <see cref="GetCaretFormat"/>.</summary>
    public readonly record struct CaretFormat(bool Bold, bool Italic, bool Underline, bool Strike,
        double FontSize, string? FontFamily, TextAlignment Align, ListKind List, int Heading,
        IBrush? Foreground = null, IBrush? Background = null);

    private static bool HasDeco(Run? r, TextDecorationLocation loc)
    {
        if (r == null) return false;
        if (r.TextDecorations == null)
            return loc == TextDecorationLocation.Underline && !string.IsNullOrEmpty(r.NavigateUri);
        foreach (var d in r.TextDecorations) if (d.Location == loc) return true;
        return false;
    }

    /// <summary>Returns the formatting snapshot at the current caret position for toolbar state display.</summary>
    public CaretFormat GetCaretFormat()
    {
        var p = _caretPosition.Paragraph;
        Run? run = null;
        if (p != null)
        {
            run = RunAtOffset(p, _caretPosition.Offset > 0 ? _caretPosition.Offset - 1 : 0);
            if (run == null) foreach (var inl in p.Inlines) if (inl is Run r0) { run = r0; break; }
        }
        return new CaretFormat(
            run?.FontWeight == FontWeight.Bold,
            run?.FontStyle == FontStyle.Italic,
            HasDeco(run, TextDecorationLocation.Underline),
            HasDeco(run, TextDecorationLocation.Strikethrough),
            run != null && run.FontSize > 0 ? run.FontSize : 14,
            run?.FontFamily,
            p?.TextAlignment ?? TextAlignment.Left,
            p?.ListType ?? ListKind.None,
            p?.HeadingLevel ?? 0,
            run?.Foreground,
            run?.Background);
    }

    /// <summary>Returns document statistics: total character count, word count, and the caret's
    /// 1-based (line, column) position. Inline images count as one character.</summary>
    public (int chars, int words, int line, int col) GetStatus()
    {
        if (Document == null) return (0, 0, 1, 1);
        var full = new System.Text.StringBuilder();
        int caretGlobal = -1;
        foreach (var p in GetAllParagraphsInOrder())
        {
            if (ReferenceEquals(p, _caretPosition.Paragraph))
                caretGlobal = full.Length + Math.Clamp(_caretPosition.Offset, 0, GetParagraphLength(p));
            full.Append(BuildPlain(p));
            full.Append('\n');
        }
        string text = full.ToString();
        int chars = 0;
        foreach (char ch in text) if (ch != '\n') chars++;
        int words = text.Split(new[] { ' ', '\n', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        if (caretGlobal < 0) caretGlobal = 0;
        string before = text.Substring(0, Math.Min(caretGlobal, text.Length));
        int line = 1;
        foreach (char ch in before) if (ch == '\n') line++;
        int lastNl = before.LastIndexOf('\n');
        int col = before.Length - (lastNl + 1) + 1;
        return (chars, words, line, col);
    }

    // Guarantees the caret can always sit next to any image/table: the document starts and ends
    // with a paragraph, and no two non-paragraph blocks are adjacent without a paragraph between.
    // Without this, an image/table at the very start/end (or two in a row) is impossible to reach
    // or delete with the keyboard.
    private void NormalizeBlocks(FlowDocument doc)
    {
        // Keep a paragraph at the very start/end and between any two adjacent non-paragraph blocks,
        // so the caret can always reach a position before/after every image/table WITHOUT inserting
        // extra blank lines around blocks that already sit next to text paragraphs. "Before a table"
        // is then the end of the preceding text line; "after a table" is the start of the next line.
        var blocks = doc.Blocks;
        if (blocks.Count == 0 || blocks[0] is not Paragraph)
            blocks.Insert(0, new Paragraph { Parent = doc });
        if (blocks[blocks.Count - 1] is not Paragraph)
            blocks.Add(new Paragraph { Parent = doc });
        for (int i = 0; i < blocks.Count - 1; i++)
        {
            if (blocks[i] is not Paragraph && blocks[i + 1] is not Paragraph)
                blocks.Insert(i + 1, new Paragraph { Parent = doc });
        }
    }

    private void UpdateParents(FlowDocument doc)
    {
        NormalizeBlocks(doc);
        foreach (var block in doc.Blocks)
        {
            block.Parent = doc;
            if (block is Paragraph p)
            {
                foreach (var inline in p.Inlines) inline.Parent = p;
            }
            else if (block is TableBlock tb)
            {
                for (int r = 0; r < tb.Rows; r++)
                    for (int c = 0; c < tb.Columns; c++)
                    {
                        var cell = tb.Cells[r][c];
                        cell.Parent = tb;
                        foreach (var inline in cell.Inlines) inline.Parent = cell;
                    }
            }
        }
    }

    private int GetParagraphLength(Paragraph? p)
    {
        if (p == null) return 0;
        int len = 0;
        foreach (var inline in p.Inlines) len += InlineLen(inline);
        return len;
    }

    /// <summary>Inserts plain text at the caret, replacing any current selection.
    /// Triggers smart-list detection when <paramref name="text"/> is a space
    /// following a list-prefix pattern at the start of a line.</summary>
    public void InsertText(string text)
    {
        if (Document == null || IsReadOnly || _caretPosition.Paragraph == null) return;
        if (_selectionStart != _selectionEnd) DeleteSelection();

        // Smart list: typing the space after "-"/"*" or "N." at a paragraph start turns it into a list.
        if (text == " " && TryAutoList()) return;

        TryInsertTextCore(_caretPosition.Paragraph, text, _caretPosition.Offset);
        _caretPosition.Offset += text.Length;
        _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        MarkTextChanged();
        InvalidateVisual();
        NotifyStatus();
    }

    // If the caret sits right after a list prefix ("-"/"*" -> bullet, "N." -> ordered) at the very start
    // of its paragraph, convert the paragraph to that list (dropping the typed prefix) and swallow the
    // triggering space. Only fires on a plain paragraph (not already a list). Returns true if it acted.
    private bool TryAutoList()
    {
        var p = _caretPosition.Paragraph;
        if (p == null || p.ListType != ListKind.None) return false;
        int caret = _caretPosition.Offset;
        string plain = BuildPlain(p);
        if (caret <= 0 || caret > plain.Length) return false;
        string prefix = plain.Substring(0, caret);
        ListKind kind;
        if (prefix == "-" || prefix == "*") kind = ListKind.Bullet;
        else if (System.Text.RegularExpressions.Regex.IsMatch(prefix, @"^\d+\.$")) kind = ListKind.Ordered;
        else return false;

        if (Document != null) PushUndo();
        DeleteLocalText(p, 0, prefix.Length);
        p.ListType = kind;
        _caretPosition = new TextPointer(p, 0);
        _selectionStart = new TextPointer(p, 0);
        _selectionEnd = new TextPointer(p, 0);
        InvalidateVisual();
        return true;
    }

    private void DeleteSelection()
    {
        if (_selectionStart != null && _selectionEnd != null && _selectionStart.CompareTo(_selectionEnd) != 0)
        {
            var range = new TextRange(_selectionStart, _selectionEnd);
            range.Delete();

            _caretPosition = new TextPointer(range.Start.Paragraph, range.Start.Offset);
            if (Document != null) NormalizeBlocks(Document);
            MarkTextChanged();
        }

        _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
    }

    // Deletes the logical range [index, index+length) from the paragraph, spanning run boundaries
    // (a single-run early-return here once left residue when e.g. an auto-list prefix was split
    // across two runs). Offsets are in pre-deletion coordinates throughout the walk.
    private void DeleteLocalText(Paragraph? p, int index, int length)
    {
        if (p == null || length <= 0) return;
        int end = index + length;
        int pos = 0;
        for (int i = 0; i < p.Inlines.Count && pos < end; )
        {
            var inl = p.Inlines[i];
            int len = InlineLen(inl);
            int segStart = pos, segEnd = pos + len;
            if (segEnd <= index || len == 0) { pos = segEnd; i++; continue; }
            if (inl is Run run)
            {
                int from = Math.Max(index, segStart) - segStart;
                int to = Math.Min(end, segEnd) - segStart;
                run.Text = run.Text!.Remove(from, to - from);
                if (run.Text.Length == 0) p.Inlines.RemoveAt(i); else i++;
            }
            else if (inl is InlineImage)
            {
                // The image's single position overlaps the range -> remove it.
                p.Inlines.RemoveAt(i);
            }
            else i++;
            pos = segEnd;
        }
    }

    private void TryInsertTextCore(Paragraph p, string text, int localIndex)
    {
        int currentIndex = 0;
        for (int i = 0; i < p.Inlines.Count; i++)
        {
            int len = InlineLen(p.Inlines[i]);
            if (p.Inlines[i] is Run run && localIndex >= currentIndex && localIndex <= currentIndex + len)
            {
                run.Text = (run.Text ?? "").Insert(localIndex - currentIndex, text);
                return;
            }
            // Insertion point sits exactly before an inline image -> insert a new run there.
            if (p.Inlines[i] is InlineImage && localIndex == currentIndex)
            {
                p.Inlines.Insert(i, new Run { Text = text, Parent = p });
                return;
            }
            currentIndex += len;
        }
        p.Inlines.Add(new Run { Text = text, Parent = p });
    }

    // Vertical space a horizontal-rule (DividerBlock) occupies when laid out.
    private const double DividerHeight = 18;

    // The object-replacement character represents one inline image in the logical text stream.
    private const char ObjChar = '￼';

    // Logical length of an inline: a Run's text length, or 1 for an inline image.
    private static int InlineLen(Inline inline) =>
        inline is Run r ? (r.Text?.Length ?? 0) : (inline is InlineImage ? 1 : 0);

    // The paragraph's logical text, with each inline image collapsed to one ObjChar so that
    // character offsets line up with the TextLayout produced by BuildTextLayout.
    private static string BuildPlain(Paragraph p)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var inline in p.Inlines)
        {
            if (inline is Run r && r.Text != null) sb.Append(r.Text);
            else if (inline is InlineImage) sb.Append(ObjChar);
        }
        return sb.ToString();
    }

    // A layout segment is either text (Text != null) or an inline image (Image != null, length 1).
    private struct LayoutSeg
    {
        public string? Text;
        public Avalonia.Media.TextFormatting.TextRunProperties Props;
        public Avalonia.Media.Imaging.Bitmap? Image;
        public Size ImageSize;
    }

    // Draws an inline image inside a text line; occupies one character position (U+FFFC).
    private sealed class ImageTextRun : Avalonia.Media.TextFormatting.DrawableTextRun
    {
        private static readonly ReadOnlyMemory<char> _obj = "￼".AsMemory();
        private readonly Avalonia.Media.Imaging.Bitmap? _bmp;
        private readonly Size _size;
        private readonly Avalonia.Media.TextFormatting.TextRunProperties _props;

        public ImageTextRun(Avalonia.Media.Imaging.Bitmap? bmp, Size size, Avalonia.Media.TextFormatting.TextRunProperties props)
        { _bmp = bmp; _size = size; _props = props; }

        public override ReadOnlyMemory<char> Text => _obj;
        public override int Length => 1;
        public override Avalonia.Media.TextFormatting.TextRunProperties Properties => _props;
        public override Size Size => _size;
        public override double Baseline => _size.Height;

        public override void Draw(DrawingContext context, Point origin)
        {
            if (_bmp != null)
                context.DrawImage(_bmp, new Rect(origin.X, origin.Y, _size.Width, _size.Height));
        }
    }

    // Feeds a paragraph's runs/images to Avalonia's text formatter so a single TextLayout drives
    // rendering, caret geometry, hit-testing and selection rects.
    private sealed class ParagraphTextSource : Avalonia.Media.TextFormatting.ITextSource
    {
        private readonly List<LayoutSeg> _segs;
        public ParagraphTextSource(List<LayoutSeg> segs) => _segs = segs;

        public Avalonia.Media.TextFormatting.TextRun? GetTextRun(int textSourceIndex)
        {
            int pos = 0;
            foreach (var seg in _segs)
            {
                int len = seg.Text != null ? seg.Text.Length : 1;
                if (textSourceIndex < pos + len)
                {
                    if (seg.Text != null)
                    {
                        int start = textSourceIndex - pos;
                        return new Avalonia.Media.TextFormatting.TextCharacters(seg.Text.Substring(start).AsMemory(), seg.Props);
                    }
                    return new ImageTextRun(seg.Image, seg.ImageSize, seg.Props);
                }
                pos += len;
            }
            return null;
        }
    }

    // Width reserved to the left of list-item text for its bullet/number marker.
    private const double ListMarkerWidth = 22;

    // Left x where a paragraph's text starts: base indent + manual indent + nesting + (list marker gap).
    // Single source so render and all hit-tests agree on where text begins.
    private static double ParaLeft(Paragraph p)
        => 10 + p.Indent + p.ListLevel * 20 + (p.ListType != ListKind.None ? ListMarkerWidth : 0);

    // Draws a list item's marker (• or "N.") right-aligned just left of the text (small fixed gap),
    // so the marker hugs the content regardless of its width. No-op for non-list paragraphs.
    private static void DrawListMarker(DrawingContext context, Paragraph p, int num, double textLeft, double y)
    {
        if (p.ListType == ListKind.None) return;
        string m = p.ListType == ListKind.Bullet ? "•" : $"{num}.";
        var ft = new FormattedText(m, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.Black);
        const double gap = 6;
        context.DrawText(ft, new Point(textLeft - gap - ft.Width, y));
    }

    // A cheap content+formatting fingerprint of a paragraph; when it (and the wrap width) are unchanged
    // the cached TextLayout can be reused. Iterating inlines to hash is far cheaper than re-shaping, and
    // it can never go stale silently the way a manual dirty-flag would. Over-invalidation (e.g. a brush
    // re-instantiated to the same colour) is harmless — it just rebuilds.
    private static long ParagraphSig(Paragraph p)
    {
        unchecked
        {
            long h = 1469598103934665603; // FNV-1a 64-bit offset basis
            void Mix(long v) { h = (h ^ v) * 1099511628211; }
            void MixStr(string? s) { if (s == null) { Mix(0); return; } foreach (char ch in s) Mix(ch); Mix(s.Length + 1); }

            Mix((long)p.TextAlignment);
            Mix(BitConverter.DoubleToInt64Bits(p.LineHeight));
            Mix(BitConverter.DoubleToInt64Bits(p.Indent));
            Mix((long)p.ListType);
            Mix(p.ListLevel);
            foreach (var inl in p.Inlines)
            {
                if (inl is Run r)
                {
                    MixStr(r.Text);
                    MixStr(r.FontFamily);
                    MixStr(r.NavigateUri);
                    Mix(BitConverter.DoubleToInt64Bits(r.FontSize));
                    Mix((long)r.FontWeight);
                    Mix((long)r.FontStyle);
                    Mix(r.Foreground?.GetHashCode() ?? 0);
                    Mix(r.Background?.GetHashCode() ?? 0);
                    if (r.TextDecorations != null)
                        foreach (var d in r.TextDecorations) Mix((long)d.Location + 101);
                }
                else if (inl is InlineImage img)
                {
                    Mix(7);
                    Mix(BitConverter.DoubleToInt64Bits(img.Width));
                    Mix(BitConverter.DoubleToInt64Bits(img.Height));
                    // Identity only — never the Image getter here: it lazily decodes RawBytes, and
                    // the signature must stay cheap (and decode-free) on every cache lookup (N6-2).
                    Mix(img.RawBytes?.GetHashCode() ?? img.Image?.GetHashCode() ?? 0);
                }
            }
            return h;
        }
    }

    private Avalonia.Media.TextFormatting.TextLayout BuildTextLayout(Paragraph p, double maxWidth,
        int preeditOffset = -1, string? preeditText = null)
    {
        // IME composition is transient and paragraph-local; never serve/store it from the cache.
        bool hasPreedit = !string.IsNullOrEmpty(preeditText) && preeditOffset >= 0;
        long sig = 0;
        if (!hasPreedit)
        {
            sig = ParagraphSig(p);
            if (_layoutCache.TryGetValue(p, out var cached) && cached.width == maxWidth && cached.sig == sig)
                return cached.layout;
        }

        var defaultFamily = DefaultFontFamily;
        double defaultSize = DefaultFontSize;
        var defaultProps = new Avalonia.Media.TextFormatting.GenericTextRunProperties(
            new Typeface(defaultFamily), defaultSize, null, Brushes.Black);

        var segs = new List<LayoutSeg>();
        foreach (var inline in p.Inlines)
        {
            if (inline is Run r && !string.IsNullOrEmpty(r.Text))
            {
                var family = string.IsNullOrEmpty(r.FontFamily) ? defaultFamily : new FontFamily(r.FontFamily);
                var typeface = new Typeface(family, r.FontStyle, r.FontWeight);
                TextDecorationCollection? decos = r.TextDecorations;
                if (decos == null && !string.IsNullOrEmpty(r.NavigateUri)) decos = TextDecorations.Underline;
                var props = new Avalonia.Media.TextFormatting.GenericTextRunProperties(
                    typeface,
                    r.FontSize <= 0 ? defaultSize : r.FontSize,
                    decos,
                    r.Foreground ?? Brushes.Black,
                    r.Background);
                segs.Add(new LayoutSeg { Text = r.Text, Props = props });
            }
            else if (inline is InlineImage img)
            {
                segs.Add(new LayoutSeg
                {
                    Image = img.Image,
                    ImageSize = new Size(img.Width > 0 ? img.Width : 16, img.Height > 0 ? img.Height : 16),
                    Props = defaultProps
                });
            }
        }

        if (!string.IsNullOrEmpty(preeditText) && preeditOffset >= 0)
        {
            var preeditProps = new Avalonia.Media.TextFormatting.GenericTextRunProperties(
                Typeface.Default, 14, TextDecorations.Underline, Brushes.Black);
            SplicePreedit(segs, preeditOffset, preeditText!, preeditProps);
        }

        double lh = !double.IsNaN(p.LineHeight) ? p.LineHeight : double.NaN;
        var paraProps = new Avalonia.Media.TextFormatting.GenericTextParagraphProperties(
            FlowDirection.LeftToRight,
            p.TextAlignment,
            true,
            false,
            defaultProps,
            TextWrapping.Wrap,
            lh,
            0,
            0);

        var layout = new Avalonia.Media.TextFormatting.TextLayout(
            new ParagraphTextSource(segs), paraProps, null, Math.Max(1, maxWidth));

        if (!hasPreedit)
        {
            // Entries for deleted paragraphs are never re-accessed and would linger; a generous cap keeps
            // the cache from growing without bound over a long editing session (it just rebuilds lazily).
            if (_layoutCache.Count > 10000) _layoutCache.Clear();
            _layoutCache[p] = (sig, maxWidth, layout);
        }
        return layout;
    }

    // Inserts the IME preedit text at a character offset, splitting a text segment if needed.
    private static void SplicePreedit(List<LayoutSeg> segs, int offset, string preedit,
        Avalonia.Media.TextFormatting.TextRunProperties preeditProps)
    {
        int idx = 0;
        for (int i = 0; i < segs.Count; i++)
        {
            int len = segs[i].Text != null ? segs[i].Text!.Length : 1;
            if (offset <= idx + len)
            {
                int local = offset - idx;
                var pre = new LayoutSeg { Text = preedit, Props = preeditProps };
                if (segs[i].Text != null && local > 0 && local < len)
                {
                    var left = new LayoutSeg { Text = segs[i].Text!.Substring(0, local), Props = segs[i].Props };
                    var right = new LayoutSeg { Text = segs[i].Text!.Substring(local), Props = segs[i].Props };
                    segs[i] = left;
                    segs.Insert(i + 1, pre);
                    segs.Insert(i + 2, right);
                }
                else if (local <= 0)
                {
                    segs.Insert(i, pre);
                }
                else
                {
                    segs.Insert(i + 1, pre);
                }
                return;
            }
            idx += len;
        }
        segs.Add(new LayoutSeg { Text = preedit, Props = preeditProps });
    }

    private Block? FindTopLevelBlock(Paragraph p)
    {
        if (Document == null) return null;
        foreach (var b in Document.Blocks)
        {
            if (b == p) return b;
            if (b is TableBlock tb)
                for (int r = 0; r < tb.Rows; r++)
                    for (int c = 0; c < tb.Columns; c++)
                        if (tb.Cells[r][c] == p) return tb;
        }
        return null;
    }

    private void InsertParsedDocument(FlowDocument parsed)
    {
        if (Document == null) return;

        // Inline-only single paragraph: paste inline at the caret to keep the current line's flow.
        if (parsed.Blocks.Count == 1 && parsed.Blocks[0] is Paragraph sp && _caretPosition.Paragraph != null)
        {
            var runs = new List<Run>();
            foreach (var inl in sp.Inlines) if (inl is Run r) runs.Add((Run)r.Clone());
            if (runs.Count > 0) { InsertRuns(runs); return; }
        }

        // Otherwise splice the parsed blocks in after the caret's top-level block.
        int insertIndex = Document.Blocks.Count;
        var caretBlock = _caretPosition.Paragraph != null ? FindTopLevelBlock(_caretPosition.Paragraph) : null;
        if (caretBlock != null)
        {
            int i = Document.Blocks.IndexOf(caretBlock);
            if (i >= 0) insertIndex = i + 1;
        }

        Paragraph? lastPara = null;
        foreach (var b in parsed.Blocks.ToList())
        {
            Document.Blocks.Insert(insertIndex++, b);
            if (b is Paragraph bp) lastPara = bp;
            else if (b is TableBlock tbb && tbb.Rows > 0 && tbb.Columns > 0) lastPara = tbb.LogicalCells().Select(x => x.cell).LastOrDefault() ?? tbb.Cells[tbb.Rows - 1][tbb.Columns - 1];
        }
        UpdateParents(Document);

        if (lastPara != null)
        {
            _caretPosition = new TextPointer(lastPara, GetParagraphLength(lastPara));
            _selectionStart = new TextPointer(lastPara, _caretPosition.Offset);
            _selectionEnd = new TextPointer(lastPara, _caretPosition.Offset);
        }
    }

    // Inserts a top-level block immediately after the caret's current block (or at the end
    // when the caret isn't in a normal paragraph), instead of always appending to the document.
    private void InsertBlockAtCaret(Block b)
    {
        if (Document == null) return;
        int insertIndex = Document.Blocks.Count;
        var caretBlock = _caretPosition.Paragraph != null ? FindTopLevelBlock(_caretPosition.Paragraph) : null;
        if (caretBlock != null)
        {
            int i = Document.Blocks.IndexOf(caretBlock);
            if (i >= 0) insertIndex = i + 1;
        }
        Document.Blocks.Insert(insertIndex, b);
        UpdateParents(Document);
    }

    /// <summary>Inserts a block image from a <see cref="Avalonia.Media.Imaging.Bitmap"/> at the caret.
    /// When the encoded bytes are available, prefer <see cref="InsertImageBytes(byte[])"/> to avoid re-encoding.</summary>
    public void InsertImage(Avalonia.Media.Imaging.Bitmap image)
    {
        if (Document == null || IsReadOnly || !AllowImages) return;
        PushUndo();
        var ib = new ImageBlock { Image = image, Width = image.Size.Width, Height = image.Size.Height };
        InsertBlockAtCaret(ib);
        InvalidateVisual();
    }

    /// <summary>Inserts an empty <paramref name="rows"/>×<paramref name="cols"/> table at the caret.</summary>
    public void InsertTable(int rows, int cols)
    {
        if (Document == null || IsReadOnly || !AllowTables) return;
        PushUndo();
        // The (rows, cols) constructor builds Cells, ColumnWidths and the span grids together, all
        // consistent. (An object initializer that rebuilt Cells alone would desync the span grids.)
        var tb = new TableBlock(rows, cols);
        InsertBlockAtCaret(tb);
        InvalidateVisual();
    }

    // Splits the caret's (top-level) paragraph at the caret into a new following paragraph, which
    // inherits list/indent/alignment/background (not heading level). Used by Enter.
    private void SplitParagraphAtCaret()
    {
        var p = _caretPosition.Paragraph;
        if (Document == null || p == null) return;
        int idx = Document.Blocks.IndexOf(p);
        if (idx < 0) return;
        int insertAt = SplitInlinesAt(p, _caretPosition.Offset);
        var np = new Paragraph
        {
            ListType = p.ListType,
            ListLevel = p.ListLevel,
            Indent = p.Indent,
            TextAlignment = p.TextAlignment,
            Background = p.Background
        };
        while (p.Inlines.Count > insertAt)
        {
            var inl = p.Inlines[insertAt];
            p.Inlines.RemoveAt(insertAt);
            inl.Parent = np;
            np.Inlines.Add(inl);
        }
        if (np.Inlines.Count == 0) np.Inlines.Add(new Run { Text = "" });
        if (p.Inlines.Count == 0) p.Inlines.Add(new Run { Text = "" });
        np.Parent = Document;
        Document.Blocks.Insert(idx + 1, np);
        _caretPosition = new TextPointer(np, 0);
        _selectionStart = new TextPointer(np, 0);
        _selectionEnd = new TextPointer(np, 0);
    }

    private void ApplyInlinesToFormattedText(FormattedText formattedText, Paragraph paragraph)
    {
        int localIndex = 0;
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is Run run && !string.IsNullOrEmpty(run.Text))
            {
                int length = run.Text.Length;
                if (run.FontWeight != FontWeight.Normal)
                    formattedText.SetFontWeight(run.FontWeight, localIndex, length);
                if (run.FontStyle != FontStyle.Normal)
                    formattedText.SetFontStyle(run.FontStyle, localIndex, length);
                if (run.FontSize != 14)
                    formattedText.SetFontSize(run.FontSize, localIndex, length);
                if (run.Foreground != null)
                    formattedText.SetForegroundBrush(run.Foreground, localIndex, length);
                if (!string.IsNullOrEmpty(run.NavigateUri))
                    formattedText.SetTextDecorations(TextDecorations.Underline, localIndex, length);
                if (run.TextDecorations != null)
                    formattedText.SetTextDecorations(run.TextDecorations, localIndex, length);
                localIndex += length;
            }
        }
    }

    private List<Paragraph> GetAllParagraphsInOrder()
    {
        var result = new List<Paragraph>();
        if (Document == null) return result;
        foreach (var block in Document.Blocks)
        {
            if (block is Paragraph p) result.Add(p);
            else if (block is TableBlock tb)
            {
                foreach (var (_, _, cell) in tb.LogicalCells())
                    result.Add(cell);
            }
        }
        return result;
    }

    private void SelectAll()
    {
        var allParas = GetAllParagraphsInOrder();
        if (allParas.Count == 0) return;
        _selectionStart = new TextPointer(allParas[0], 0);
        var lastPara = allParas[allParas.Count - 1];
        _selectionEnd = new TextPointer(lastPara, GetParagraphLength(lastPara));
        _caretPosition = new TextPointer(_selectionEnd.Paragraph, _selectionEnd.Offset);
        InvalidateVisual();
    }

    // Copies the currently selected image, if any: right-click-selected block image, block caret
    // sitting on an image, or a selected inline image. Returns false when nothing image-like is
    // selected (the caller falls back to the text-selection copy).
    private bool TryCopySelectedImage()
    {
        if ((_selectedBlock as ImageBlock ?? _caretBlock as ImageBlock) is { } ib)
        {
            _ = CopyImageToClipboardAsync(ib.RawBytes, ib.Image, inline: false, ib.Width, ib.Height);
            return true;
        }
        if (_selectedInline is { } si)
        {
            _ = CopyImageToClipboardAsync(si.img.RawBytes, si.img.Image, inline: true, si.img.Width, si.img.Height);
            return true;
        }
        return false;
    }

    // Pastes an image as an inline (character-like) image at the caret, splitting the run under it
    // like text insertion. Used when the clipboard meta says the image was inline when copied.
    private void InsertInlineImageAtCaret(byte[] bytes, double w, double h)
    {
        if (Document == null || IsReadOnly || !AllowImages) return;
        if (_selectionStart != _selectionEnd) DeleteSelection();
        var p = _caretPosition.Paragraph;
        if (p == null) return;

        var im = new InlineImage { Width = w > 0 ? w : 16, Height = h > 0 ? h : 16 };
        im.SetImageData(bytes, ImageMime.Detect(bytes));
        if (w <= 0 || h <= 0)
        {
            // No display size in the meta — fall back to the natural size when decodable.
            try
            {
                using var ms = new System.IO.MemoryStream(bytes);
                var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
                if (w <= 0) im.Width = bmp.Size.Width;
                if (h <= 0) im.Height = bmp.Size.Height;
            }
            catch { }
        }

        int at = SplitInlinesAt(p, _caretPosition.Offset);
        p.Inlines.Insert(at, im);
        UpdateParents(Document);
        _caretPosition.Offset += 1;
        _selectionStart = new TextPointer(p, _caretPosition.Offset);
        _selectionEnd = new TextPointer(p, _caretPosition.Offset);
        MarkTextChanged();
        InvalidateVisual();
        NotifyStatus();
    }

    private async void CopySelectionToClipboard()
    {
        if (_selectionStart.Paragraph == null || _selectionEnd.Paragraph == null || _selectionStart.CompareTo(_selectionEnd) == 0) return;
        var range = new TextRange(_selectionStart, _selectionEnd);
        string text = range.GetText();
        // Capture the rich fragment synchronously (cloned) before any await / later edits.
        _internalClipboard = range.GetRichRuns();
        _internalClipboardText = text;
        _internalClipboardBlocks = CaptureBlockStructure(range);

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null && !string.IsNullOrEmpty(text))
        {
            // Another process can hold the clipboard open; an unhandled throw here would
            // crash the process (async void). The internal rich slots above are already set.
            try { await clipboard.SetTextAsync(text); }
            catch { }
        }
    }

    // If the selection spans whole tables/images or crosses multiple top-level blocks, clone the
    // full top-level blocks so paste can reproduce table structure. Returns null for plain inline
    // selections (those use the run-based clipboard instead).
    private List<Block>? CaptureBlockStructure(TextRange range)
    {
        if (Document == null) return null;
        var startTop = range.Start.Paragraph != null ? FindTopLevelBlock(range.Start.Paragraph) : null;
        var endTop = range.End.Paragraph != null ? FindTopLevelBlock(range.End.Paragraph) : null;
        if (startTop == null || endTop == null) return null;

        int si = Document.Blocks.IndexOf(startTop);
        int ei = Document.Blocks.IndexOf(endTop);
        if (si < 0 || ei < 0 || si > ei) return null;

        bool spansMultiple = si != ei;
        bool hasNonParagraph = false;
        for (int k = si; k <= ei; k++)
            if (!(Document.Blocks[k] is Paragraph)) { hasNonParagraph = true; break; }

        if (!spansMultiple && !hasNonParagraph) return null; // plain inline selection

        var blocks = new List<Block>();
        for (int k = si; k <= ei; k++)
            if (Document.Blocks[k].Clone() is Block cl) blocks.Add(cl);
        return blocks.Count > 0 ? blocks : null;
    }

    // Inserts cloned blocks (e.g. tables) after the caret's block, preserving structure.
    private void InsertBlocks(List<Block> blocks)
    {
        if (Document == null || blocks.Count == 0) return;
        if (_selectionStart != _selectionEnd) DeleteSelection();

        int insertIndex = Document.Blocks.Count;
        var caretBlock = _caretPosition.Paragraph != null ? FindTopLevelBlock(_caretPosition.Paragraph) : null;
        if (caretBlock != null)
        {
            int i = Document.Blocks.IndexOf(caretBlock);
            if (i >= 0) insertIndex = i + 1;
        }

        Paragraph? lastPara = null;
        foreach (var b in blocks)
        {
            if (b.Clone() is not Block cl) continue; // clone again so repeated pastes stay independent
            Document.Blocks.Insert(insertIndex++, cl);
            if (cl is Paragraph bp) lastPara = bp;
            else if (cl is TableBlock tbb && tbb.Rows > 0 && tbb.Columns > 0) lastPara = tbb.LogicalCells().Select(x => x.cell).LastOrDefault() ?? tbb.Cells[tbb.Rows - 1][tbb.Columns - 1];
        }
        UpdateParents(Document);

        if (lastPara != null)
        {
            _caretPosition = new TextPointer(lastPara, GetParagraphLength(lastPara));
            _selectionStart = new TextPointer(lastPara, _caretPosition.Offset);
            _selectionEnd = new TextPointer(lastPara, _caretPosition.Offset);
        }
    }

    // Inserts a list of formatted Runs at the current caret, splitting the run under the caret.
    private void InsertRuns(List<Run> runs)
    {
        if (Document == null || _caretPosition.Paragraph == null || runs.Count == 0) return;
        if (_selectionStart != _selectionEnd) DeleteSelection();

        var p = _caretPosition.Paragraph;
        int insertAt = SplitInlinesAt(p, _caretPosition.Offset);
        int added = 0;
        foreach (var r in runs)
        {
            var clone = (Run)r.Clone();
            clone.Parent = p;
            p.Inlines.Insert(insertAt++, clone);
            added += clone.Text?.Length ?? 0;
        }
        _caretPosition.Offset += added;
        _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        InvalidateVisual();
    }

    // Splits the run straddling the given character offset and returns the inline index
    // at which new content should be inserted.
    private int SplitInlinesAt(Paragraph p, int offset)
    {
        int currentIndex = 0;
        for (int i = 0; i < p.Inlines.Count; i++)
        {
            int len = InlineLen(p.Inlines[i]);
            if (offset <= currentIndex) return i;
            if (p.Inlines[i] is Run run && offset < currentIndex + len)
            {
                int local = offset - currentIndex;
                string t1 = run.Text!.Substring(0, local);
                string t2 = run.Text!.Substring(local);
                run.Text = t1;
                var nr = (Run)run.Clone();
                nr.Text = t2;
                nr.Parent = p;
                p.Inlines.Insert(i + 1, nr);
                return i + 1;
            }
            currentIndex += len;
        }
        return p.Inlines.Count;
    }

    private void DrawSelectionHighlight(DrawingContext context, Avalonia.Media.TextFormatting.TextLayout layout,
        int startOffset, int endOffset, double originX, double originY)
    {
        if (endOffset <= startOffset) return;
        var brush = SelectionBrush;
        foreach (var rect in layout.HitTestTextRange(startOffset, endOffset - startOffset))
            context.FillRectangle(brush, new Rect(originX + rect.X, originY + rect.Y, rect.Width, rect.Height));
    }

    private void DeleteBlock(Block b)
    {
        if (Document == null) return;
        PushUndo();
        Document.Blocks.Remove(b);
        _selectedBlock = null;
        NormalizeBlocks(Document);
        InvalidateVisual();
    }

    /// <summary>Undoes the last edit. No-op when <see cref="CanUndo"/> is <see langword="false"/>.</summary>
    public void Undo() => DoUndo();
    /// <summary>Redoes the last undone edit. No-op when <see cref="CanRedo"/> is <see langword="false"/>.</summary>
    public void Redo() => DoRedo();


}
