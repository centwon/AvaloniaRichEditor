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
        Cursor = new Cursor(StandardCursorType.Ibeam);

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

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);

        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            ShowContextMenu(point);
            e.Handled = true;
            return;
        }

        _caretBlock = null; // any press clears the block caret unless an image/table sets it below

        if (!IsReadOnly)
            foreach (var h in _imageHandles)
            {
                if (h.rect.Contains(point))
                {
                    if (Document != null) PushUndo();
                    _isResizingImage = true;
                    _resizingImage = h.img;
                    _initialImageWidth = h.img.Width > 0 ? h.img.Width : 200;
                    _initialImageHeight = h.img.Height > 0 ? h.img.Height : 200;
                    _imageAspect = _initialImageHeight > 0 ? _initialImageWidth / _initialImageHeight : 1;
                    _initialImageMouseX = point.X;
                    e.Pointer.Capture(this);
                    return;
                }
            }

        if (!IsReadOnly)
            foreach (var h in _inlineHandles)
            {
                if (h.rect.Contains(point))
                {
                    if (Document != null) PushUndo();
                    _isResizingInline = true;
                    _resizingInline = h.img;
                    _selectedInline = (h.p, h.img); // keep the selection chrome through the drag
                    _initialImageWidth = h.img.Width > 0 ? h.img.Width : 16;
                    _initialImageHeight = h.img.Height > 0 ? h.img.Height : 16;
                    _imageAspect = _initialImageHeight > 0 ? _initialImageWidth / _initialImageHeight : 1;
                    _initialImageMouseX = point.X;
                    e.Pointer.Capture(this);
                    return;
                }
            }

        _selectedInline = null; // any other press clears the inline-image selection (may re-set below)

        if (!IsReadOnly)
            foreach (var b in _columnBoundaries)
            {
                if (b.rect.Contains(point))
                {
                    if (Document != null) PushUndo();
                    _isResizingColumn = true;
                    _resizingTable = b.tb;
                    _resizingColumnIndex = b.colIndex;
                    _resizingLastColumn = b.colIndex >= b.tb.Columns - 1;
                    _initialMouseX = point.X;
                    _initialColumnWidth = (b.colIndex < b.tb.ColumnWidths.Count) ? b.tb.ColumnWidths[b.colIndex] : 100;
                    _initialNextColumnWidth = (b.colIndex + 1 < b.tb.ColumnWidths.Count) ? b.tb.ColumnWidths[b.colIndex + 1] : 100;
                    e.Pointer.Capture(this);
                    return;
                }
            }

        if (!IsReadOnly)
            foreach (var b in _rowBoundaries)
            {
                if (b.rect.Contains(point))
                {
                    if (Document != null) PushUndo();
                    _isResizingRow = true;
                    _resizingRowTable = b.tb;
                    _resizingRowIndex = b.rowIndex;
                    _initialMouseY = point.Y;
                    _initialRowHeight = b.height; // current rendered row height (content- or user-driven)
                    e.Pointer.Capture(this);
                    return;
                }
            }

        // Click on a hyperlink opens it in the default browser instead of placing the caret.
        var linkRun = GetLinkRunAtPoint(point);
        if (linkRun != null && !string.IsNullOrEmpty(linkRun.NavigateUri))
        {
            OpenUrl(linkRun.NavigateUri!);
            return;
        }

        // Clicking a block image/table selects it (so it can be deleted with Delete/Backspace).
        // Images: single click selects. Tables: single click selects, double-click (or a click
        // while already editing that table) enters cell editing.
        var clickedBlock = GetBlockAtPoint(point);
        if (clickedBlock is ImageBlock)
        {
            // Place a block caret in front of the image (Space indents it; Del/Backspace deletes).
            _caretBlock = clickedBlock; _caretBlockAfter = false;
            _selectedBlock = null;
            _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            ResetCaretBlink();
            InvalidateVisual();
            return;
        }
        if (clickedBlock is TableBlock table)
        {
            // Outer left/top border -> select the whole table (right/bottom edges are resize handles,
            // already consumed above). Lets the table be deleted as a unit.
            if (IsOnTableLeftOrTopBorder(table, point))
            {
                // Block caret in front of the table (Space indents the whole table; Del deletes it).
                _caretBlock = table; _caretBlockAfter = false;
                _selectedBlock = null;
                _cellSelMode = false; _cellSelTable = null;
                _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
                _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
                ResetCaretBlink();
                InvalidateVisual();
                return;
            }
            if (_cellSelMode && _cellSelTable == table)
            {
                if (e.ClickCount >= 2)
                {
                    // Cell-selection -> text editing: drop into the cell with a caret.
                    _cellSelMode = false; _cellSelTable = null; _selectedBlock = null;
                    _caretPosition = GetPositionFromPoint(point);
                    _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
                    _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
                    ResetCaretBlink();
                    InvalidateVisual();
                    e.Pointer.Capture(this);
                    return;
                }
                // Single click selects exactly one cell; a drag (OnPointerMoved) extends to a block.
                _selectedBlock = null;
                var tp = GetPositionFromPoint(point);
                if (tp.Paragraph != null) FocusCell(tp.Paragraph);
                e.Pointer.Capture(this);
                _isSelecting = true;
                return;
            }
            // else: text-editing mode -> fall through to caret placement below.
        }
        // Any click that isn't on the active cell-selection table leaves cell-selection mode.
        _cellSelMode = false; _cellSelTable = null;
        _selectedBlock = null;

        _caretPosition = GetPositionFromPoint(point);
        _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);

        // A single click on an inline image selects it (border + corner resize handle), mirroring
        // block images. The caret was already placed at the image edge above.
        if (e.ClickCount == 1)
            foreach (var ir in _inlineImageRects)
                if (ir.rect.Contains(point))
                {
                    _selectedInline = (ir.p, ir.img);
                    ResetCaretBlink();
                    InvalidateVisual();
                    return; // no drag-selection from this press
                }

        // Double-click selects the word under the caret; triple-click selects the whole paragraph.
        if (e.ClickCount >= 2 && _caretPosition.Paragraph != null)
        {
            if (e.ClickCount >= 3)
            {
                int len = GetParagraphLength(_caretPosition.Paragraph);
                _selectionStart = new TextPointer(_caretPosition.Paragraph, 0);
                _selectionEnd = new TextPointer(_caretPosition.Paragraph, len);
                _caretPosition = new TextPointer(_caretPosition.Paragraph, len);
            }
            else
            {
                var (ws, we) = WordBoundsAt(BuildPlain(_caretPosition.Paragraph), _caretPosition.Offset);
                _selectionStart = new TextPointer(_caretPosition.Paragraph, ws);
                _selectionEnd = new TextPointer(_caretPosition.Paragraph, we);
                _caretPosition = new TextPointer(_caretPosition.Paragraph, we);
            }
            ResetCaretBlink();
            InvalidateVisual();
            e.Pointer.Capture(this);
            return; // no drag-select; keep the word/paragraph selection intact
        }

        ResetCaretBlink();
        e.Pointer.Capture(this);
        _isSelecting = true;
    }

    // Word span [start,end) around `offset` in plain text. A "word" is a run of letters/digits/_
    // (Hangul syllables count as letters). On whitespace/punctuation, selects that single character.
    private static (int start, int end) WordBoundsAt(string text, int offset)
    {
        if (text.Length == 0) return (0, 0);
        static bool IsWord(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
        int i = Math.Clamp(offset, 0, text.Length);
        // Prefer the word the caret sits inside; if on a boundary, look one char left.
        int probe = (i < text.Length && IsWord(text[i])) ? i : (i > 0 && IsWord(text[i - 1]) ? i - 1 : -1);
        if (probe < 0)
            return i < text.Length ? (i, i + 1) : (Math.Max(0, i - 1), i);
        int start = probe, end = probe;
        while (start > 0 && IsWord(text[start - 1])) start--;
        while (end < text.Length && IsWord(text[end])) end++;
        return (start, end);
    }

    // Word boundaries on plain text: a word is a run of non-whitespace (so Korean eojeol = one word).
    private static int PrevWord(string text, int offset)
    {
        int i = Math.Clamp(offset, 0, text.Length);
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
        while (i > 0 && !char.IsWhiteSpace(text[i - 1])) i--;
        return i;
    }
    private static int NextWord(string text, int offset)
    {
        int i = Math.Clamp(offset, 0, text.Length);
        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return i;
    }

    private void ApplyCaretSelection(bool shift)
    {
        if (!shift) _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        ResetCaretBlink();
    }

    private void WordMove(bool forward, bool shift)
    {
        var p = _caretPosition.Paragraph;
        if (p != null)
        {
            int off = _caretPosition.Offset;
            if (forward && off < GetParagraphLength(p)) _caretPosition = new TextPointer(p, NextWord(BuildPlain(p), off));
            else if (!forward && off > 0) _caretPosition = new TextPointer(p, PrevWord(BuildPlain(p), off));
            else if (forward) MoveCaretRight();
            else MoveCaretLeft();
        }
        ApplyCaretSelection(shift);
    }

    private void WordDelete(bool forward)
    {
        if (Document != null) PushUndo();
        if (_selectionStart != _selectionEnd) { DeleteSelection(); InvalidateVisual(); ResetCaretBlink(); return; }
        var p = _caretPosition.Paragraph;
        if (p != null)
        {
            int off = _caretPosition.Offset;
            if (forward && off < GetParagraphLength(p))
            {
                int no = NextWord(BuildPlain(p), off);
                new TextRange(new TextPointer(p, off), new TextPointer(p, no)).Delete();
            }
            else if (!forward && off > 0)
            {
                int no = PrevWord(BuildPlain(p), off);
                new TextRange(new TextPointer(p, no), new TextPointer(p, off)).Delete();
                _caretPosition = new TextPointer(p, no);
            }
        }
        _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        InvalidateVisual();
        ResetCaretBlink();
    }

    // Viewport height in document (unscaled) pixels, for PageUp/Down. Accounts for the fit-to-width zoom.
    private double PageStep()
    {
        var sv = Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.ScrollViewer>(this);
        double h = sv?.Viewport.Height ?? 500;
        var lt = Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.LayoutTransformControl>(this);
        double scale = (lt?.LayoutTransform as ScaleTransform)?.ScaleY ?? 1;
        if (scale > 0.01) h /= scale;
        return Math.Max(100, h - 40); // keep a little overlap between pages
    }

    /// <summary>Focuses the editor and moves the caret to the end of the last paragraph.</summary>
    public void FocusDocumentEnd()
    {
        var paras = GetAllParagraphsInOrder();
        if (paras.Count > 0)
        {
            var last = paras[^1];
            int off = GetParagraphLength(last);
            _caretPosition = new TextPointer(last, off);
            _selectionStart = new TextPointer(last, off);
            _selectionEnd = new TextPointer(last, off);
        }
        Focus();
        ResetCaretBlink();
    }

    private void GoToDocEdge(bool start, bool shift)
    {
        var paras = GetAllParagraphsInOrder();
        if (paras.Count == 0) return;
        var target = start ? paras[0] : paras[^1];
        _caretPosition = new TextPointer(target, start ? 0 : GetParagraphLength(target));
        ApplyCaretSelection(shift);
    }

    private static void OpenUrl(string url)
    {
        // Only launch web links from pasted content; never arbitrary schemes (file:, etc.).
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    // Returns the Run directly under the point if the point lands on rendered text, else null.
    // Used for hyperlink hover/click detection.
    private Run? GetLinkRunAtPoint(Point p)
    {
        if (Document == null) return null;
        double yOffset = 0;
        double maxWidth = Bounds.Width;

        foreach (var block in Document.Blocks)
        {
            if (block is TableBlock tb)
            {
                var tl = LayoutTable(tb, 10 + tb.Indent, yOffset);
                foreach (var (r, c, rect) in tl.AnchorRects)
                {
                    if (rect.Contains(p))
                    {
                        var cell = tb.Cells[r][c];
                        var layout = BuildTextLayout(cell, Math.Max(10, rect.Width - 10));
                        var hit = layout.HitTestPoint(new Point(p.X - (rect.X + 5), p.Y - (rect.Y + 5)));
                        return hit.IsInside ? RunAtOffset(cell, hit.TextPosition) : null;
                    }
                }
                yOffset += tl.TotalHeight + 10;
            }
            else if (block is Paragraph paragraph)
            {
                string fullText = BuildPlain(paragraph);
                if (fullText == "")
                {
                    yOffset += paragraph.MarginBottom + (!double.IsNaN(paragraph.LineHeight) ? paragraph.LineHeight : 20);
                    continue;
                }
                double plink = ParaLeft(paragraph);
                var layout = BuildTextLayout(paragraph, Math.Max(10, maxWidth - 20 - plink));
                double height = layout.Height;
                if (p.Y >= yOffset && p.Y <= yOffset + height)
                {
                    var hit = layout.HitTestPoint(new Point(p.X - plink, p.Y - yOffset));
                    return hit.IsInside ? RunAtOffset(paragraph, hit.TextPosition) : null;
                }
                yOffset += height + paragraph.MarginBottom;
            }
            else if (block is ImageBlock img)
            {
                yOffset += (img.Height > 0 ? img.Height : 200) + 10;
            }
            else if (block is DividerBlock)
            {
                yOffset += DividerHeight;
            }
        }
        return null;
    }

    private static bool IsCellOf(TableBlock tb, Paragraph p)
    {
        for (int r = 0; r < tb.Rows; r++)
            for (int c = 0; c < tb.Columns; c++)
                if (tb.Cells[r][c] == p) return true;
        return false;
    }

    // Pixel geometry of one table. Anchor cells get a rect spanning their merged columns/rows;
    // covered cells are absorbed into their anchor and never appear here.
    private readonly struct TableLayout
    {
        public readonly double[] ColX;       // length Columns+1: left edge of each column + right end
        public readonly double[] RowY;       // length Rows+1: top edge of each row + bottom end
        public readonly double TableWidth;
        public readonly double TotalHeight;
        public readonly List<(int r, int c, Rect rect)> AnchorRects;
        public TableLayout(double[] colX, double[] rowY, double w, double h, List<(int, int, Rect)> anchors)
        { ColX = colX; RowY = rowY; TableWidth = w; TotalHeight = h; AnchorRects = anchors; }
    }

    // Single source of truth for a table's geometry. Render and all three hit-tests consume this so
    // merged-cell rects and skipped (covered) cells stay identical across every consumer.
    private TableLayout LayoutTable(TableBlock tb, double startX, double top)
    {
        int cols = tb.Columns, rows = tb.Rows;
        var colX = new double[cols + 1];
        colX[0] = startX;
        for (int c = 0; c < cols; c++)
            colX[c + 1] = colX[c] + ((c < tb.ColumnWidths.Count) ? tb.ColumnWidths[c] : 100);

        var rowH = new double[rows];
        for (int r = 0; r < rows; r++) rowH[r] = 20;

        // Base row heights come from single-row cells (rowSpan == 1) measured at their merged width.
        foreach (var (r, c, cell) in tb.LogicalCells())
        {
            var (cs, rs) = tb.SpanOf(r, c);
            if (rs != 1) continue;
            double w = colX[Math.Min(c + cs, cols)] - colX[c];
            var l = BuildTextLayout(cell, Math.Max(10, w - 10));
            if (l.Height + 10 > rowH[r]) rowH[r] = l.Height + 10;
        }
        for (int r = 0; r < rows; r++)
            if (r < tb.RowHeights.Count && tb.RowHeights[r] > rowH[r]) rowH[r] = tb.RowHeights[r];

        // Row-spanning cells: if content needs more than the spanned rows provide, grow the last row.
        foreach (var (r, c, cell) in tb.LogicalCells())
        {
            var (cs, rs) = tb.SpanOf(r, c);
            if (rs <= 1) continue;
            double w = colX[Math.Min(c + cs, cols)] - colX[c];
            var l = BuildTextLayout(cell, Math.Max(10, w - 10));
            double need = l.Height + 10, have = 0;
            for (int rr = r; rr < r + rs && rr < rows; rr++) have += rowH[rr];
            int last = Math.Min(r + rs - 1, rows - 1);
            if (need > have) rowH[last] += need - have;
        }

        var rowY = new double[rows + 1];
        rowY[0] = top;
        for (int r = 0; r < rows; r++) rowY[r + 1] = rowY[r] + rowH[r];

        var anchors = new List<(int, int, Rect)>();
        foreach (var (r, c, cell) in tb.LogicalCells())
        {
            var (cs, rs) = tb.SpanOf(r, c);
            int cEnd = Math.Min(c + cs, cols), rEnd = Math.Min(r + rs, rows);
            anchors.Add((r, c, new Rect(colX[c], rowY[r], colX[cEnd] - colX[c], rowY[rEnd] - rowY[r])));
        }
        return new TableLayout(colX, rowY, colX[cols] - startX, rowY[rows] - top, anchors);
    }

    // The block (image or table) whose rendered rectangle contains the point, or null.
    private Block? GetBlockAtPoint(Point p)
    {
        if (Document == null) return null;
        double yOffset = 0, listIndent = 10, maxWidth = Bounds.Width;
        foreach (var block in Document.Blocks)
        {
            if (block is TableBlock tb)
            {
                double tableTop = yOffset;
                var tl = LayoutTable(tb, 10 + tb.Indent, yOffset);
                yOffset += tl.TotalHeight;
                if (p.X >= 10 + tb.Indent && p.X <= 10 + tb.Indent + tl.TableWidth && p.Y >= tableTop && p.Y <= yOffset) return tb;
                yOffset += 10;
            }
            else if (block is Paragraph paragraph)
            {
                if (BuildPlain(paragraph) == "")
                {
                    yOffset += paragraph.MarginBottom + (!double.IsNaN(paragraph.LineHeight) ? paragraph.LineHeight : 20);
                    continue;
                }
                var layout = BuildTextLayout(paragraph, Math.Max(10, maxWidth - 20 - ParaLeft(paragraph)));
                yOffset += layout.Height + paragraph.MarginBottom;
            }
            else if (block is DividerBlock)
            {
                yOffset += DividerHeight;
            }
            else if (block is ImageBlock img)
            {
                double w = img.Width > 0 ? img.Width : 200;
                double h = img.Height > 0 ? img.Height : 200;
                if (p.X >= listIndent + img.Indent && p.X <= listIndent + img.Indent + w && p.Y >= yOffset && p.Y <= yOffset + h) return img;
                yOffset += h + 10;
            }
        }
        return null;
    }

    // Top y and geometry of a given table, mirroring the block advancement used by the hit-tests.
    private (double top, TableLayout tl)? GetTableRect(TableBlock target)
    {
        if (Document == null) return null;
        double yOffset = 0, maxWidth = Bounds.Width;
        foreach (var block in Document.Blocks)
        {
            if (block is TableBlock tb)
            {
                var tl = LayoutTable(tb, 10 + tb.Indent, yOffset);
                if (tb == target) return (yOffset, tl);
                yOffset += tl.TotalHeight + 10;
            }
            else if (block is Paragraph paragraph)
            {
                if (BuildPlain(paragraph) == "")
                {
                    yOffset += paragraph.MarginBottom + (!double.IsNaN(paragraph.LineHeight) ? paragraph.LineHeight : 20);
                    continue;
                }
                var layout = BuildTextLayout(paragraph, Math.Max(10, maxWidth - 20 - ParaLeft(paragraph)));
                yOffset += layout.Height + paragraph.MarginBottom;
            }
            else if (block is DividerBlock) yOffset += DividerHeight;
            else if (block is ImageBlock img) yOffset += (img.Height > 0 ? img.Height : 200) + 10;
        }
        return null;
    }

    // True when the point sits on the table's outer left or top border (a thin band). The right/bottom
    // borders are reserved for resize handles, so only left/top trigger whole-table selection.
    private bool IsOnTableLeftOrTopBorder(TableBlock tb, Point p)
    {
        if (GetTableRect(tb) is not { } tr) return false;
        double top = tr.top, w = tr.tl.TableWidth, h = tr.tl.TotalHeight, left = 10 + tb.Indent;
        const double m = 4;
        bool inY = p.Y >= top - m && p.Y <= top + h + m;
        bool inX = p.X >= left - m && p.X <= left + w + m;
        return (inY && Math.Abs(p.X - left) <= m) || (inX && Math.Abs(p.Y - top) <= m);
    }

    private static Run? RunAtOffset(Paragraph p, int offset)
    {
        int idx = 0;
        foreach (var inl in p.Inlines)
        {
            int len = InlineLen(inl);
            if (inl is Run run && offset >= idx && offset < idx + len) return run;
            idx += len;
        }
        return null;
    }

    // The inline image whose logical position ends exactly at `offset` (i.e. the caret sits right
    // after it). Used to correct the caret X next to a trailing image.
    private static InlineImage? InlineImageEndingAt(Paragraph p, int offset)
    {
        int idx = 0;
        foreach (var inl in p.Inlines)
        {
            idx += InlineLen(inl);
            if (inl is InlineImage img && idx == offset) return img;
            if (idx > offset) break;
        }
        return null;
    }

    // Works around an Avalonia hit-test quirk: a DrawableTextRun at the very end of a line is
    // excluded from caret-distance computation, so the position right after a trailing inline
    // image collapses to the image's *left* edge (typing there visibly lands "behind" the image
    // and Right-arrow appears stuck). When the caret sits right after an image and the reported X
    // didn't advance past the image's start, pin it to the image's right edge.
    internal static Rect FixCaretAfterTrailingImage(Avalonia.Media.TextFormatting.TextLayout layout,
        Paragraph p, int logicalOffset, int displayIndex, Rect cr)
    {
        if (displayIndex <= 0 || InlineImageEndingAt(p, logicalOffset) is not { } img) return cr;
        var ir = layout.HitTestTextPosition(displayIndex - 1);
        double w = Math.Max(8, img.Width > 0 ? img.Width : 16);
        return cr.X <= ir.X + 0.5 ? cr.WithX(ir.X + w) : cr;
    }

    // The inline image occupying the logical position at `offset`. An image is one position wide, so a
    // click on it can land on either edge — check both the position and the one before it.
    private static InlineImage? InlineImageAt(Paragraph p, int offset)
    {
        int idx = 0;
        foreach (var inl in p.Inlines)
        {
            int len = InlineLen(inl);
            if (inl is InlineImage img && (offset == idx || offset == idx + len)) return img;
            idx += len;
        }
        return null;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);

        if (_isResizingInline && _resizingInline != null)
        {
            double diff = point.X - _initialImageMouseX;
            double newW = Math.Max(8, _initialImageWidth + diff);
            _resizingInline.Width = newW;
            _resizingInline.Height = _imageAspect > 0 ? newW / _imageAspect : _resizingInline.Height; // keep aspect ratio
            InvalidateVisual();
            return;
        }

        if (_isResizingImage && _resizingImage != null)
        {
            double diff = point.X - _initialImageMouseX;
            double newW = Math.Max(20, _initialImageWidth + diff);
            _resizingImage.Width = newW;
            _resizingImage.Height = _imageAspect > 0 ? newW / _imageAspect : _resizingImage.Height; // keep aspect ratio
            InvalidateVisual();
            return;
        }

        if (_isResizingColumn && _resizingTable != null)
        {
            const double minW = 20;
            double diff = point.X - _initialMouseX;

            if (_resizingLastColumn)
            {
                // Outer-right edge: grow/shrink this column, changing the table's total width.
                while (_resizingTable.ColumnWidths.Count <= _resizingColumnIndex)
                    _resizingTable.ColumnWidths.Add(100);
                _resizingTable.ColumnWidths[_resizingColumnIndex] = Math.Max(minW, _initialColumnWidth + diff);
            }
            else
            {
                // Internal edge: redistribute between the two adjacent columns, total fixed.
                while (_resizingTable.ColumnWidths.Count <= _resizingColumnIndex + 1)
                    _resizingTable.ColumnWidths.Add(100);
                double minDiff = -(_initialColumnWidth - minW);
                double maxDiff = _initialNextColumnWidth - minW;
                diff = Math.Clamp(diff, minDiff, maxDiff);
                _resizingTable.ColumnWidths[_resizingColumnIndex] = _initialColumnWidth + diff;
                _resizingTable.ColumnWidths[_resizingColumnIndex + 1] = _initialNextColumnWidth - diff;
            }

            InvalidateVisual();
            return;
        }

        if (_isResizingRow && _resizingRowTable != null)
        {
            double diff = point.Y - _initialMouseY;
            while (_resizingRowTable.RowHeights.Count <= _resizingRowIndex)
                _resizingRowTable.RowHeights.Add(0);
            // Renderer clamps up to content height, so 20 is just a hard floor for the stored value.
            _resizingRowTable.RowHeights[_resizingRowIndex] = Math.Max(20, _initialRowHeight + diff);
            InvalidateVisual();
            return;
        }

        if (_isSelecting)
        {
            _selectionEnd = GetPositionFromPoint(point);
            _caretPosition = new TextPointer(_selectionEnd.Paragraph, _selectionEnd.Offset);
            // A drag spanning two different cells of one table is a cell-block selection: enter cell mode
            // so subsequent single clicks select whole cells (HWP behaviour).
            var sc = _selectionStart.Paragraph != null ? FindCell(_selectionStart.Paragraph) : null;
            var ec = _selectionEnd.Paragraph != null ? FindCell(_selectionEnd.Paragraph) : null;
            if (sc is { } s && ec is { } en && s.tb == en.tb && (s.r != en.r || s.c != en.c))
            {
                _cellSelMode = true;
                _cellSelTable = s.tb;
            }
            InvalidateVisual();
            return;
        }

        foreach (var h in _imageHandles)
        {
            if (h.rect.Contains(point))
            {
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.BottomRightCorner);
                return;
            }
        }

        foreach (var h in _inlineHandles)
        {
            if (h.rect.Contains(point))
            {
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.BottomRightCorner);
                return;
            }
        }

        foreach (var b in _columnBoundaries)
        {
            if (b.rect.Contains(point))
            {
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeWestEast);
                return;
            }
        }

        foreach (var b in _rowBoundaries)
        {
            if (b.rect.Contains(point))
            {
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeNorthSouth);
                return;
            }
        }

        // Outer left/top table border selects the whole table on click -> show an arrow there.
        if (GetBlockAtPoint(point) is TableBlock ht && IsOnTableLeftOrTopBorder(ht, point))
        {
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Arrow);
            return;
        }

        var hoverLink = GetLinkRunAtPoint(point);
        Cursor = (hoverLink != null && !string.IsNullOrEmpty(hoverLink.NavigateUri))
            ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            : new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Ibeam);
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isResizingImage)
        {
            // Pre-resize state already pushed on press; undo restores original size in one step.
            _isResizingImage = false;
            _resizingImage = null;
            e.Pointer.Capture(null);
            return;
        }

        if (_isResizingInline)
        {
            // Pre-resize state already pushed on press; undo restores original size in one step.
            _isResizingInline = false;
            _resizingInline = null;
            e.Pointer.Capture(null);
            return;
        }

        if (_isResizingColumn)
        {
            // Pre-resize state was already pushed on pointer-press, so undo restores
            // the original width in a single step. Don't push the post-resize state here.
            _isResizingColumn = false;
            _resizingTable = null;
            e.Pointer.Capture(null);
            return;
        }

        if (_isResizingRow)
        {
            // Pre-resize state pushed on press; undo restores the original height in one step.
            _isResizingRow = false;
            _resizingRowTable = null;
            e.Pointer.Capture(null);
            return;
        }

        if (_isSelecting)
        {
            _isSelecting = false;
            e.Pointer.Capture(null);
            // Format painter: a just-completed selection receives the captured formatting, then disarms.
            ApplyFormatPainterToSelection();
        }
    }

    /// <inheritdoc/>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (IsReadOnly) return;
        if (string.IsNullOrEmpty(e.Text)) return;
        _selectedBlock = null;
        _caretBlock = null;
        PushUndoTyping(); // coalesce consecutive keystrokes into one undo checkpoint
        InsertText(e.Text);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (IsReadOnly)
        {
            // Allow caret movement and copy/select-all; block everything that edits.
            bool nav = e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown;
            bool copyOrAll = ctrl && (e.Key == Key.C || e.Key == Key.A);
            if (!nav && !copyOrAll) { e.Handled = true; return; }
        }

        // Block caret in front of an image/table.
        if (_caretBlock != null && !ctrl && !IsReadOnly)
        {
            if (e.Key == Key.Space || (e.Key == Key.Tab && !shift))
            {
                if (Document != null) PushUndo();
                _caretBlock.Indent = Math.Min(_caretBlock.Indent + 20, 600);
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            if ((e.Key == Key.Tab && shift) || (e.Key == Key.Back && _caretBlock.Indent > 0))
            {
                if (Document != null) PushUndo();
                _caretBlock.Indent = Math.Max(0, _caretBlock.Indent - 20);
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            if ((e.Key == Key.Back || e.Key == Key.Delete) && Document != null)
            {
                PushUndo();
                var blk = _caretBlock; _caretBlock = null;
                MoveCaretToBlockNeighbor(blk, before: true);
                Document.Blocks.Remove(blk);
                NormalizeBlocks(Document);
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            if (e.Key is Key.Left or Key.Up or Key.Right or Key.Down)
            {
                HandleBlockCaretArrow(forward: e.Key is Key.Right or Key.Down);
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            // Any other key dismisses the block caret and continues normally.
            _caretBlock = null;
            InvalidateVisual();
        }

        // Any key other than the deletion keys cancels a block-image selection — except Ctrl
        // combos: Ctrl+C/X must still see the selection to copy/cut the image.
        if (_selectedBlock != null && e.Key != Key.Back && e.Key != Key.Delete && !ctrl)
        {
            _selectedBlock = null;
            InvalidateVisual();
        }
        // Same for an inline-image selection.
        if (_selectedInline != null && e.Key != Key.Back && e.Key != Key.Delete && !ctrl)
        {
            _selectedInline = null;
            InvalidateVisual();
        }

        if (e.Key == Key.Z && ctrl)
        {
            DoUndo();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Y && ctrl)
        {
            DoRedo();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A && ctrl)
        {
            SelectAll();
            e.Handled = true;
            return;
        }

        bool hasTextSel = _selectionStart.Paragraph != null && _selectionEnd.Paragraph != null
            && _selectionStart.CompareTo(_selectionEnd) != 0;

        if (e.Key == Key.C && ctrl)
        {
            // No text selection but an image is selected (clicked block / inline / block caret)
            // -> copy the image itself.
            if (!hasTextSel && TryCopySelectedImage()) { e.Handled = true; return; }
            CopySelectionToClipboard();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.X && ctrl)
        {
            if (!hasTextSel && Document != null && !IsReadOnly)
            {
                // Cut a selected image as a unit: copy, then remove it.
                if ((_selectedBlock as ImageBlock ?? _caretBlock as ImageBlock) is { } xb)
                {
                    _ = CopyImageToClipboardAsync(xb.RawBytes, xb.Image, inline: false, xb.Width, xb.Height);
                    PushUndo();
                    _selectedBlock = null; _caretBlock = null;
                    Document.Blocks.Remove(xb);
                    NormalizeBlocks(Document);
                    ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
                }
                if (_selectedInline is { } xi)
                {
                    _ = CopyImageToClipboardAsync(xi.img.RawBytes, xi.img.Image, inline: true, xi.img.Width, xi.img.Height);
                    PushUndo();
                    xi.p.Inlines.Remove(xi.img);
                    _selectedInline = null;
                    ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
                }
            }
            if (Document != null) PushUndo();
            CopySelectionToClipboard();
            DeleteSelection();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V && ctrl)
        {
            _ = PasteFromClipboardAsync();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.B) { ToggleBold(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.I) { ToggleItalic(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.U) { ToggleUnderline(); e.Handled = true; return; }

        // Word-level navigation/deletion and document start/end (Ctrl + arrows/Home/End/Back/Delete).
        if (ctrl && e.Key == Key.Home) { GoToDocEdge(start: true, shift); e.Handled = true; return; }
        if (ctrl && e.Key == Key.End) { GoToDocEdge(start: false, shift); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Left) { WordMove(forward: false, shift); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Right) { WordMove(forward: true, shift); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Back && !IsReadOnly) { WordDelete(forward: false); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Delete && !IsReadOnly) { WordDelete(forward: true); e.Handled = true; return; }

        // PageUp/Down move the caret by ~a viewport height (and the auto-scroll follows). We handle them
        // so the caret moves with the view instead of the ScrollViewer scrolling the caret off-screen.
        if (e.Key == Key.PageUp || e.Key == Key.PageDown)
        {
            double step = PageStep();
            double ty = e.Key == Key.PageUp ? Math.Max(0, _lastCaretPoint.Y - step) : _lastCaretPoint.Y + step;
            _caretPosition = GetPositionFromPoint(new Point(_lastCaretPoint.X, ty));
            if (!shift) { _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); }
            else _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            ResetCaretBlink(); e.Handled = true; return;
        }

        if (e.Key == Key.Tab)
        {
            HandleTab(shift);
            e.Handled = true;
            return;
        }

        // Push state before destructive keys
        if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Enter)
        {
            if (Document != null) PushUndo();
        }

        if (e.Key == Key.Left)
        {
            // At a paragraph start, step onto an adjacent image/table as a block caret (from its right = "after").
            if (!shift && _caretPosition.Offset == 0 && AdjacentBlock(before: true) is { } lb)
            {
                _caretBlock = lb; _caretBlockAfter = true;
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            MoveCaretLeft();
            if (!shift) { _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); }
            else _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            ResetCaretBlink();
            e.Handled = true;
            return;
        }
        else if (e.Key == Key.Right)
        {
            if (!shift && _caretPosition.Offset >= GetParagraphLength(_caretPosition.Paragraph) && AdjacentBlock(before: false) is { } rb)
            {
                _caretBlock = rb; _caretBlockAfter = false;
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            MoveCaretRight();
            if (!shift) { _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); }
            else _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            ResetCaretBlink();
            e.Handled = true;
            return;
        }
        else if (e.Key == Key.Up)
        {
            // Leaving a table cell upward from its first row -> the table's "before" caret.
            if (!shift && _caretPosition.Paragraph != null && FindCell(_caretPosition.Paragraph) is { } fc && fc.r == 0)
            {
                _caretBlock = fc.tb; _caretBlockAfter = false;
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            double ty = Math.Max(0, _lastCaretPoint.Y - 20);
            if (!shift && BlockAtY(ty) is { } ub && !CaretInBlock(ub))
            {
                _caretBlock = ub; _caretBlockAfter = true; // entering a block from below
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            _caretPosition = GetPositionFromPoint(new Point(_lastCaretPoint.X, ty));
            if (!shift) { _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); }
            else _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            ResetCaretBlink();
            e.Handled = true;
            return;
        }
        else if (e.Key == Key.Down)
        {
            // Leaving a table cell downward from its last row -> the table's "after" caret.
            if (!shift && _caretPosition.Paragraph != null && FindCell(_caretPosition.Paragraph) is { } fc
                && fc.r + fc.tb.SpanOf(fc.r, fc.c).rs - 1 >= fc.tb.Rows - 1)
            {
                _caretBlock = fc.tb; _caretBlockAfter = true;
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            double ty = _lastCaretPoint.Y + 30;
            if (!shift && BlockAtY(ty) is { } db && !CaretInBlock(db))
            {
                _caretBlock = db; _caretBlockAfter = false; // entering a block from above
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            _caretPosition = GetPositionFromPoint(new Point(_lastCaretPoint.X, ty));
            if (!shift) { _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); }
            else _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            ResetCaretBlink();
            e.Handled = true;
            return;
        }
        else if ((e.Key == Key.Back || e.Key == Key.Delete) && _selectedInline is { } selInl && Document != null)
        {
            // A selected inline image deletes as a unit (state was pushed above).
            selInl.p.Inlines.Remove(selInl.img);
            _selectedInline = null;
            ResetCaretBlink(); e.Handled = true;
            return;
        }
        else if ((e.Key == Key.Back || e.Key == Key.Delete) && _selectedBlock != null && Document != null)
        {
            // A block image is selected -> delete it.
            Document.Blocks.Remove(_selectedBlock);
            _selectedBlock = null;
            NormalizeBlocks(Document);
            ResetCaretBlink(); e.Handled = true;
            return;
        }
        else if (e.Key == Key.Back)
        {
            if (_selectionStart != _selectionEnd) DeleteSelection();
            else if (_caretPosition.Offset > 0)
            {
                DeleteLocalText(_caretPosition.Paragraph, _caretPosition.Offset - 1, 1);
                _caretPosition.Offset--;
            }
            else if (_caretPosition.Paragraph?.Parent is FlowDocument && Document != null)
            {
                int idx = Document.Blocks.IndexOf(_caretPosition.Paragraph);
                var prevBlock = idx > 0 ? Document.Blocks[idx - 1] : null;
                if (prevBlock is ImageBlock || prevBlock is TableBlock || prevBlock is DividerBlock)
                {
                    // Caret at start of paragraph, previous block is an image/table/divider -> delete it.
                    Document.Blocks.RemoveAt(idx - 1);
                }
                else if (prevBlock is Paragraph prev)
                {
                    int prevLen = GetParagraphLength(prev);
                    foreach (var inline in _caretPosition.Paragraph.Inlines)
                    {
                        inline.Parent = prev;
                        prev.Inlines.Add(inline);
                    }
                    Document.Blocks.Remove(_caretPosition.Paragraph);
                    _caretPosition.Paragraph = prev;
                    _caretPosition.Offset = prevLen;
                }
            }
            _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            ResetCaretBlink(); e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (_selectionStart != _selectionEnd) DeleteSelection();
            else if (_caretPosition.Offset < GetParagraphLength(_caretPosition.Paragraph))
                DeleteLocalText(_caretPosition.Paragraph, _caretPosition.Offset, 1);
            else if (_caretPosition.Paragraph?.Parent is FlowDocument && Document != null)
            {
                int idx = Document.Blocks.IndexOf(_caretPosition.Paragraph);
                var nextBlock = (idx >= 0 && idx + 1 < Document.Blocks.Count) ? Document.Blocks[idx + 1] : null;
                if (nextBlock is ImageBlock || nextBlock is TableBlock || nextBlock is DividerBlock)
                {
                    // Caret at end of paragraph, next block is an image/table/divider -> delete it.
                    Document.Blocks.RemoveAt(idx + 1);
                }
                else if (nextBlock is Paragraph next)
                {
                    foreach (var inline in next.Inlines)
                    {
                        inline.Parent = _caretPosition.Paragraph;
                        _caretPosition.Paragraph.Inlines.Add(inline);
                    }
                    Document.Blocks.Remove(next);
                }
            }
            _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            ResetCaretBlink(); e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            _caretPosition = GetPositionFromPoint(new Point(0, _lastCaretPoint.Y));
            if (!shift) { _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); }
            else _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            ResetCaretBlink(); e.Handled = true; return;
        }
        else if (e.Key == Key.End)
        {
            _caretPosition = GetPositionFromPoint(new Point(Bounds.Width, _lastCaretPoint.Y));
            if (!shift) { _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset); }
            else _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            ResetCaretBlink(); e.Handled = true; return;
        }
        else if (e.Key == Key.Enter)
        {
            if (_caretPosition.Paragraph != null)
            {
                var p = _caretPosition.Paragraph;
                // Enter splits the paragraph at the caret into a new paragraph. On an empty list item it
                // exits the list instead. Inside a table cell (not a top-level block) it keeps "\n in the
                // run" since a cell can't host sibling paragraphs.
                if (Document != null && Document.Blocks.Contains(p))
                {
                    PushUndo();
                    if (_selectionStart != _selectionEnd) DeleteSelection();
                    p = _caretPosition.Paragraph!;
                    if (p.ListType != ListKind.None && GetParagraphLength(p) == 0)
                        p.ListType = ListKind.None; // empty list item -> leave the list, stay put
                    else
                        SplitParagraphAtCaret();
                    UpdateParents(Document);
                    ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
                }
                TryInsertTextCore(_caretPosition.Paragraph, "\n", _caretPosition.Offset);
                _caretPosition.Offset++;
                _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
                _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
                ResetCaretBlink(); e.Handled = true; return;
            }
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
        _typingRun = false; // any caret move / selection change breaks the typing-undo coalescing run
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

    // True while a run of consecutive typed characters shares one undo checkpoint (see PushUndoTyping).
    private bool _typingRun;

    // Records an undo checkpoint and flags a text change. Single choke point for discrete (non-typing)
    // document mutations; also ends any in-progress typing run so the next keystroke checkpoints afresh.
    private void PushUndo()
    {
        if (Document != null) _undoManager.PushState(Document, _caretPosition);
        _textChangedPending = true;
        _typingRun = false;
    }

    // Undo checkpoint for typed text: coalesces a run of consecutive keystrokes into a single
    // checkpoint (one full-document clone per typing run, not per character). The run ends on any
    // caret move, selection change, or discrete edit (see ResetCaretBlink / PushUndo).
    private void PushUndoTyping()
    {
        if (!_typingRun)
        {
            if (Document != null) _undoManager.PushState(Document, _caretPosition);
            _typingRun = true;
        }
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

    private void OnTextInputMethodClientRequested(object? sender, Avalonia.Input.TextInput.TextInputMethodClientRequestedEventArgs e)
    {
        // Don't advertise an input-method client in ReadOnly mode — a viewer never composes text.
        e.Client = IsReadOnly ? null : _imClient;
    }

    // Caret position in this control's coordinate space, used to place the IME candidate window.
    private Rect GetCaretRectangle() => new Rect(_lastCaretPoint.X, _lastCaretPoint.Y, 1, _lastCaretHeight);

    // Caret bar height from the font size at the caret (not the line height), so the caret stays
    // glyph-sized and baseline-anchored next to tall inline content. 1.4 ≈ line height per em.
    private double CaretTextHeight(Paragraph p, int offset)
    {
        var run = RunAtOffset(p, offset > 0 ? offset - 1 : 0);
        double fs = run != null && run.FontSize > 0 ? run.FontSize : DefaultFontSize;
        return Math.Ceiling(fs * 1.4);
    }

    private void SetPreedit(string? text)
    {
        _preeditText = text;
        InvalidateVisual();
    }

    private sealed class RtbInputMethodClient : Avalonia.Input.TextInput.TextInputMethodClient
    {
        private readonly RichEditor _owner;
        public RtbInputMethodClient(RichEditor owner) => _owner = owner;

        public override Visual TextViewVisual => _owner;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => false;
        public override string SurroundingText => string.Empty;
        public override Rect CursorRectangle => _owner.GetCaretRectangle();
        public override Avalonia.Input.TextInput.TextSelection Selection { get; set; }

        public override void SetPreeditText(string? preeditText) => _owner.SetPreedit(preeditText);

        public void NotifyCaretChanged()
        {
            RaiseCursorRectangleChanged();
        }
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

    // Places the text caret at the end of the paragraph before `blk` (before=true) or the start of the
    // paragraph after it (before=false), skipping non-paragraph blocks. Used when leaving a block caret.
    private void MoveCaretToBlockNeighbor(Block blk, bool before)
    {
        if (Document == null) return;
        int idx = Document.Blocks.IndexOf(blk);
        if (idx < 0) return;
        Paragraph? target = null;
        if (before) { for (int i = idx - 1; i >= 0 && target == null; i--) target = Document.Blocks[i] as Paragraph; }
        else { for (int i = idx + 1; i < Document.Blocks.Count && target == null; i++) target = Document.Blocks[i] as Paragraph; }
        if (target == null) foreach (var b in Document.Blocks) { if (b is Paragraph pp) { target = pp; break; } }
        if (target == null) return;
        int off = before ? GetParagraphLength(target) : 0;
        _caretPosition = new TextPointer(target, off);
        _selectionStart = new TextPointer(target, off);
        _selectionEnd = new TextPointer(target, off);
    }

    private void SetCaretCollapsed(Paragraph p, int off)
    {
        _caretPosition = new TextPointer(p, off);
        _selectionStart = new TextPointer(p, off);
        _selectionEnd = new TextPointer(p, off);
    }

    // Steps a block caret: before -> (table cells | image after) -> after -> next text, and the reverse.
    private void HandleBlockCaretArrow(bool forward)
    {
        var blk = _caretBlock!;
        if (forward)
        {
            if (!_caretBlockAfter)
            {
                if (blk is TableBlock tb && tb.LogicalCells().Any())
                { _caretBlock = null; var c = tb.LogicalCells().First().cell; SetCaretCollapsed(c, 0); }
                else _caretBlockAfter = true; // image: before -> after
            }
            else { _caretBlock = null; MoveCaretToBlockNeighbor(blk, before: false); }
        }
        else
        {
            if (_caretBlockAfter)
            {
                if (blk is TableBlock tb && tb.LogicalCells().Any())
                { _caretBlock = null; var c = tb.LogicalCells().Last().cell; SetCaretCollapsed(c, GetParagraphLength(c)); }
                else _caretBlockAfter = false; // image: after -> before
            }
            else { _caretBlock = null; MoveCaretToBlockNeighbor(blk, before: true); }
        }
    }

    // True when the text caret currently sits inside a cell of the given block (a table).
    private bool CaretInBlock(Block b) => b is TableBlock tb && _caretPosition.Paragraph != null && IsCellOf(tb, _caretPosition.Paragraph);

    // The image/table block immediately before/after the caret's top-level paragraph, or null.
    private Block? AdjacentBlock(bool before)
    {
        if (Document == null || _caretPosition.Paragraph == null) return null;
        int idx = Document.Blocks.IndexOf(_caretPosition.Paragraph);
        if (idx < 0) return null;
        int j = before ? idx - 1 : idx + 1;
        if (j < 0 || j >= Document.Blocks.Count) return null;
        var b = Document.Blocks[j];
        return (b is ImageBlock || b is TableBlock) ? b : null;
    }

    // The image/table block whose rendered vertical span contains y, or null (used for Up/Down into a block).
    private Block? BlockAtY(double y)
    {
        if (Document == null) return null;
        double yOffset = 0, maxWidth = Bounds.Width;
        foreach (var block in Document.Blocks)
        {
            double top = yOffset;
            if (block is TableBlock tb)
            {
                double h = LayoutTable(tb, 10 + tb.Indent, yOffset).TotalHeight;
                if (y >= top && y <= top + h) return tb;
                yOffset += h + 10;
            }
            else if (block is ImageBlock img)
            {
                double h = img.Height > 0 ? img.Height : 200;
                if (y >= top && y <= top + h) return img;
                yOffset += h + 10;
            }
            else if (block is Paragraph paragraph)
            {
                if (BuildPlain(paragraph) == "")
                    yOffset += paragraph.MarginBottom + (!double.IsNaN(paragraph.LineHeight) ? paragraph.LineHeight : 20);
                else
                    yOffset += BuildTextLayout(paragraph, Math.Max(10, maxWidth - 20 - ParaLeft(paragraph))).Height + paragraph.MarginBottom;
            }
            else if (block is DividerBlock) yOffset += DividerHeight;
        }
        return null;
    }

    private void MoveCaretRight()
    {
        if (_caretPosition.Paragraph == null) return;
        int pLen = GetParagraphLength(_caretPosition.Paragraph);
        if (_caretPosition.Offset < pLen)
        {
            _caretPosition.Offset++;
        }
        else
        {
            Paragraph? next = GetNextParagraph(_caretPosition.Paragraph);
            if (next != null)
            {
                _caretPosition.Paragraph = next;
                _caretPosition.Offset = 0;
            }
        }
    }

    private void MoveCaretLeft()
    {
        if (_caretPosition.Paragraph == null) return;
        if (_caretPosition.Offset > 0)
        {
            _caretPosition.Offset--;
        }
        else
        {
            Paragraph? prev = GetPreviousParagraph(_caretPosition.Paragraph);
            if (prev != null)
            {
                _caretPosition.Paragraph = prev;
                _caretPosition.Offset = GetParagraphLength(prev);
            }
        }
    }

    private Paragraph? GetNextParagraph(Paragraph current)
    {
        if (Document == null) return null;
        bool found = false;
        foreach (var block in Document.Blocks)
        {
            if (block is Paragraph p) { if (found) return p; if (p == current) found = true; }
            else if (block is TableBlock tb)
            {
                foreach (var (_, _, cell) in tb.LogicalCells())
                {
                    if (found) return cell;
                    if (cell == current) found = true;
                }
            }
        }
        return null;
    }

    private Paragraph? GetPreviousParagraph(Paragraph current)
    {
        if (Document == null) return null;
        Paragraph? prev = null;
        foreach (var block in Document.Blocks)
        {
            if (block is Paragraph p) { if (p == current) return prev; prev = p; }
            else if (block is TableBlock tb)
            {
                foreach (var (_, _, cell) in tb.LogicalCells())
                {
                    if (cell == current) return prev;
                    prev = cell;
                }
            }
        }
        return null;
    }

    /// <summary>Inserts plain text at the caret, replacing any current selection.
    /// Triggers smart-list detection when <paramref name="text"/> is a space
    /// following a list-prefix pattern at the start of a line.</summary>
    public void InsertText(string text)
    {
        if (Document == null || _caretPosition.Paragraph == null) return;
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

    private void DeleteLocalText(Paragraph? p, int index, int length)
    {
        if (p == null) return;
        int currentIndex = 0;
        for (int i = 0; i < p.Inlines.Count; i++)
        {
            int len = InlineLen(p.Inlines[i]);
            if (p.Inlines[i] is Run run && index >= currentIndex && index < currentIndex + len)
            {
                int localOffset = index - currentIndex;
                int deleteLen = Math.Min(length, len - localOffset);
                run.Text = run.Text!.Remove(localOffset, deleteLen);
                if (string.IsNullOrEmpty(run.Text)) p.Inlines.RemoveAt(i);
                return;
            }
            if (p.Inlines[i] is InlineImage && index == currentIndex)
            {
                // The single position occupied by an inline image -> remove the image.
                p.Inlines.RemoveAt(i);
                return;
            }
            currentIndex += len;
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
                    Mix(img.Image?.GetHashCode() ?? 0);
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

    private int HitTestIndex(Avalonia.Media.TextFormatting.TextLayout layout, Point localPoint)
    {
        var hit = layout.HitTestPoint(localPoint);
        return hit.TextPosition + (hit.IsTrailing ? 1 : 0);
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

    private TextPointer GetPositionFromPoint(Point p)
    {
        if (Document == null || Document.Blocks.Count == 0)
            return new TextPointer(null, 0);

        double yOffset = 0;
        double maxWidth = Bounds.Width;
        double bestDistY = double.MaxValue;
        Paragraph? bestPara = null;
        int bestLocalIndex = 0;

        foreach (var block in Document.Blocks)
        {
            if (block is TableBlock tb)
            {
                var tl = LayoutTable(tb, 10 + tb.Indent, yOffset);
                foreach (var (r, c, rect) in tl.AnchorRects)
                {
                    var cell = tb.Cells[r][c];
                    var (cs, _) = tb.SpanOf(r, c);
                    bool lastCol = c + cs >= tb.Columns;
                    bool xInside = (p.X >= rect.X && p.X <= rect.Right) || (lastCol && p.X > rect.Right);
                    if (xInside && p.Y >= rect.Y && p.Y <= rect.Bottom)
                    {
                        var cft = BuildTextLayout(cell, Math.Max(10, rect.Width - 10));
                        int idx = HitTestIndex(cft, new Point(p.X - (rect.X + 5), p.Y - (rect.Y + 5)));
                        return new TextPointer(cell, idx);
                    }
                }
                // Outside any cell: remember the nearest anchor (by vertical distance) as a fallback.
                foreach (var (r, c, rect) in tl.AnchorRects)
                {
                    double distY = p.Y < rect.Y ? rect.Y - p.Y : (p.Y > rect.Bottom ? p.Y - rect.Bottom : 0);
                    if (distY < bestDistY)
                    {
                        bestDistY = distY;
                        bestPara = tb.Cells[r][c];
                        bestLocalIndex = GetParagraphLength(bestPara);
                    }
                }
                yOffset += tl.TotalHeight + 10;
            }
            else if (block is Paragraph paragraph)
            {
                string fullText = BuildPlain(paragraph);

                if (fullText == "")
                {
                    double lh = !double.IsNaN(paragraph.LineHeight) ? paragraph.LineHeight : 20;
                    double dY = p.Y < yOffset ? yOffset - p.Y : (p.Y > yOffset + lh ? p.Y - (yOffset + lh) : 0);
                    if (dY < bestDistY) { bestDistY = dY; bestPara = paragraph; bestLocalIndex = 0; }
                    yOffset += paragraph.MarginBottom + lh;
                    continue;
                }

                double ppos = ParaLeft(paragraph);
                var ft = BuildTextLayout(paragraph, Math.Max(10, maxWidth - 20 - ppos));
                double height = ft.Height;

                double distY2 = p.Y < yOffset ? yOffset - p.Y : (p.Y > yOffset + height ? p.Y - (yOffset + height) : 0);
                if (distY2 < bestDistY)
                {
                    bestDistY = distY2;
                    bestPara = paragraph;
                    bestLocalIndex = HitTestIndex(ft, new Point(p.X - ppos, p.Y - yOffset));
                }
                yOffset += height + paragraph.MarginBottom;
            }
            else if (block is ImageBlock img)
            {
                double height = img.Height > 0 ? img.Height : 200;
                yOffset += height + 10;
            }
            else if (block is DividerBlock)
            {
                yOffset += DividerHeight;
            }
        }
        return bestPara != null ? new TextPointer(bestPara, bestLocalIndex) : new TextPointer(Document.Blocks[0] as Paragraph, 0);
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
            await clipboard.SetTextAsync(text);
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
