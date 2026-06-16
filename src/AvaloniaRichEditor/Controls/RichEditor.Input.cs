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

// User input: pointer (caret placement, drag selection, resize handles, link clicks, block
// selection), keyboard (shortcuts, arrows, editing keys), IME composition, and caret navigation
// across blocks. Part of RichEditor (split out of the main file for readability).
public partial class RichEditor
{
    // Cursors are native resources; OnPointerMoved fires per mouse move, so allocate each shape once.
    // Cursors are created lazily on first use (the UI thread, platform up), not in the static
    // constructor — `new Cursor(...)` needs ICursorFactory, which isn't available if the type is
    // first touched before the (headless) platform initializes, and that would fault the whole type.
    private static readonly System.Collections.Generic.Dictionary<StandardCursorType, Cursor> _cursorCache = new();
    private static Cursor Cur(StandardCursorType t)
        => _cursorCache.TryGetValue(t, out var c) ? c : _cursorCache[t] = new Cursor(t);
    private static Cursor IbeamCursor => Cur(StandardCursorType.Ibeam);
    private static Cursor HandCursor => Cur(StandardCursorType.Hand);
    private static Cursor ArrowCursor => Cur(StandardCursorType.Arrow);
    private static Cursor CrossCursor => Cur(StandardCursorType.Cross);
    private static Cursor ColResizeCursor => Cur(StandardCursorType.SizeWestEast);
    private static Cursor RowResizeCursor => Cur(StandardCursorType.SizeNorthSouth);
    private static Cursor CornerResizeCursor => Cur(StandardCursorType.BottomRightCorner);
    private static Cursor MoveCursor => Cur(StandardCursorType.SizeAll);

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        // Page view: all downstream geometry (hit-tests, resize handles, caret placement) lives in
        // document space, so the pointer position is mapped once here. Identity when page view is off.
        var point = MapViewToDoc(e.GetPosition(this));

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

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = MapViewToDoc(e.GetPosition(this)); // document space, same as OnPointerPressed

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

            InvalidateMeasure(); // narrower columns reflow cell text taller -> total height changes
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
            InvalidateMeasure(); // the table's total height changed -> grow the scroll extent now,
            InvalidateVisual();  // not only on the next click (otherwise the resize hitches mid-drag)
            return;
        }

        if (_isSelecting)
        {
            // Drag-select hit-testing walks the document but never mutates it, so the layout cache
            // can be trusted — skips re-hashing every paragraph on every mouse move (mirrors the
            // hover path below).
            _trustLayoutCache = true;
            try { _selectionEnd = GetPositionFromPoint(point); }
            finally { _trustLayoutCache = false; }
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
                Cursor = CornerResizeCursor;
                return;
            }
        }

        foreach (var h in _inlineHandles)
        {
            if (h.rect.Contains(point))
            {
                Cursor = CornerResizeCursor;
                return;
            }
        }

        foreach (var b in _columnBoundaries)
        {
            if (b.rect.Contains(point))
            {
                Cursor = ColResizeCursor;
                return;
            }
        }

        foreach (var b in _rowBoundaries)
        {
            if (b.rect.Contains(point))
            {
                Cursor = RowResizeCursor;
                return;
            }
        }

        // Hover hit-testing walks the document but never mutates it (runs synchronously here), so the
        // layout cache can be trusted — skips re-hashing every paragraph on every mouse move.
        _trustLayoutCache = true;
        try
        {
            // Outer left/top table border selects the whole table on click -> a move cursor signals that
            // the border is grabbable (vs the I-beam over cell text).
            if (GetBlockAtPoint(point) is TableBlock ht && IsOnTableLeftOrTopBorder(ht, point))
            {
                Cursor = MoveCursor;
                return;
            }

            var hoverLink = GetLinkRunAtPoint(point);
            Cursor = (hoverLink != null && !string.IsNullOrEmpty(hoverLink.NavigateUri))
                ? HandCursor
                : IbeamCursor;
        }
        finally { _trustLayoutCache = false; }
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
                HandleBlockCaretArrow(forward: e.Key is Key.Right or Key.Down,
                                      vertical: e.Key is Key.Up or Key.Down);
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
            // Ctrl+Shift+V: paste as plain text, skipping the rich/HTML/image formats.
            _ = shift ? PastePlainTextAsync() : PasteFromClipboardAsync();
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
            ApplyCaretSelection(shift); e.Handled = true; return;
        }

        if (e.Key == Key.Tab)
        {
            HandleTab(shift);
            e.Handled = true;
            return;
        }

        // Push state before destructive keys. A plain single-character Backspace/Delete coalesces
        // into one checkpoint per run of the same key (like typing); structural cases — selection
        // delete, paragraph merge, selected block/inline image, Enter — stay one checkpoint each.
        if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Enter)
        {
            if (Document != null)
            {
                bool plainCharDelete = e.Key != Key.Enter
                    && _selectionStart == _selectionEnd
                    && _selectedBlock == null && _selectedInline == null
                    && _caretPosition.Paragraph != null
                    && (e.Key == Key.Back
                        ? _caretPosition.Offset > 0
                        : _caretPosition.Offset < GetParagraphLength(_caretPosition.Paragraph));
                if (plainCharDelete) PushUndoDeleting(e.Key == Key.Back);
                else PushUndo();
            }
        }

        if (e.Key == Key.Left)
        {
            // At a paragraph start, step onto an adjacent image/table as a block caret (from its right = "after").
            if (!shift && _caretPosition.Offset == 0 && AdjacentBlock(before: true) is { } lb)
            {
                _caretBlock = lb; _caretBlockAfter = true;
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            // Inside a table: ← at the very start of the first cell steps onto the table's "before"
            // caret (symmetric with → entering the first cell from that caret).
            if (!shift && _caretPosition.Offset == 0 && _caretPosition.Paragraph != null
                && FindCell(_caretPosition.Paragraph) is { } lc
                && ReferenceEquals(lc.tb.LogicalCells().First().cell, _caretPosition.Paragraph))
            {
                _caretBlock = lc.tb; _caretBlockAfter = false;
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            MoveCaretLeft();
            ApplyCaretSelection(shift);
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
            // Inside a table: → at the very end of the last cell steps onto the table's "after"
            // caret (mirrors ← from the following paragraph; it used to skip straight past it).
            if (!shift && _caretPosition.Paragraph != null
                && _caretPosition.Offset >= GetParagraphLength(_caretPosition.Paragraph)
                && FindCell(_caretPosition.Paragraph) is { } rc
                && ReferenceEquals(rc.tb.LogicalCells().Last().cell, _caretPosition.Paragraph))
            {
                _caretBlock = rc.tb; _caretBlockAfter = true;
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            MoveCaretRight();
            ApplyCaretSelection(shift);
            e.Handled = true;
            return;
        }
        else if (e.Key == Key.Up)
        {
            // First navigate the current paragraph/cell's own visual lines; only cross out when already
            // on its top line.
            if (TryMoveCaretByLineWithinParagraph(down: false))
            {
                ApplyCaretSelection(shift); e.Handled = true; return;
            }
            // Leaving a table cell upward from its first row -> the table's "before" caret.
            if (!shift && _caretPosition.Paragraph != null && FindCell(_caretPosition.Paragraph) is { } fc && fc.r == 0)
            {
                _caretBlock = fc.tb; _caretBlockAfter = false;
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            // In a (possibly tall) cell, step just above the cell's top edge so the move always clears
            // it into the row above; outside a cell, a small fixed step is enough.
            double ty = CaretCellRect() is { } upCell ? upCell.Top - 2 : Math.Max(0, _lastCaretPoint.Y - 20);
            if (!shift && BlockAtY(ty) is { } ub && !CaretInBlock(ub))
            {
                _caretBlock = ub; _caretBlockAfter = true; // entering a block from below
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            _caretPosition = GetPositionFromPoint(new Point(_lastCaretPoint.X, ty));
            ApplyCaretSelection(shift);
            e.Handled = true;
            return;
        }
        else if (e.Key == Key.Down)
        {
            // First navigate the current paragraph/cell's own visual lines; only cross out when already
            // on its bottom line.
            if (TryMoveCaretByLineWithinParagraph(down: true))
            {
                ApplyCaretSelection(shift); e.Handled = true; return;
            }
            // Leaving a table cell downward from its last row -> the table's "after" caret.
            if (!shift && _caretPosition.Paragraph != null && FindCell(_caretPosition.Paragraph) is { } fc
                && fc.r + fc.tb.SpanOf(fc.r, fc.c).rs - 1 >= fc.tb.Rows - 1)
            {
                _caretBlock = fc.tb; _caretBlockAfter = true;
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            // In a (possibly tall) cell, step just below the cell's bottom edge so the move always
            // clears it into the row below; outside a cell, a small fixed step is enough.
            double ty = CaretCellRect() is { } dnCell ? dnCell.Bottom + 2 : _lastCaretPoint.Y + 30;
            if (!shift && BlockAtY(ty) is { } db && !CaretInBlock(db))
            {
                _caretBlock = db; _caretBlockAfter = false; // entering a block from above
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            _caretPosition = GetPositionFromPoint(new Point(_lastCaretPoint.X, ty));
            ApplyCaretSelection(shift);
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
            else if (_caretPosition.Offset > 0 && _caretPosition.Paragraph is { } bp)
            {
                int nb = PrevCharBoundary(BuildPlain(bp), _caretPosition.Offset);
                DeleteLocalText(bp, nb, _caretPosition.Offset - nb);
                _caretPosition.Offset = nb;
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
                    TextRange.CoalesceRuns(prev); // the joined boundary runs may share formatting
                }
            }
            _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            ResetCaretBlink(); e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (_selectionStart != _selectionEnd) DeleteSelection();
            else if (_caretPosition.Paragraph is { } dp && _caretPosition.Offset < GetParagraphLength(dp))
                DeleteLocalText(dp, _caretPosition.Offset,
                    NextCharBoundary(BuildPlain(dp), _caretPosition.Offset) - _caretPosition.Offset);
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
                    TextRange.CoalesceRuns(_caretPosition.Paragraph); // joined boundary runs may share formatting
                }
            }
            _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
            ResetCaretBlink(); e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            MoveToVisualLineEdge(toEnd: false, shift);
            e.Handled = true; return;
        }
        else if (e.Key == Key.End)
        {
            MoveToVisualLineEdge(toEnd: true, shift);
            e.Handled = true; return;
        }
        else if (e.Key == Key.Enter)
        {
            if (_caretPosition.Paragraph != null)
            {
                // Shift+Enter: soft line break — a \n inside the paragraph (the model already
                // renders multi-line paragraphs) instead of a paragraph split.
                if (shift)
                {
                    if (_selectionStart != _selectionEnd) DeleteSelection();
                    TryInsertTextCore(_caretPosition.Paragraph!, "\n", _caretPosition.Offset);
                    _caretPosition.Offset++;
                    _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
                    _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
                    MarkTextChanged();
                    ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
                }
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

    private void OnTextInputMethodClientRequested(object? sender, Avalonia.Input.TextInput.TextInputMethodClientRequestedEventArgs e)
    {
        // Don't advertise an input-method client in ReadOnly mode — a viewer never composes text.
        e.Client = IsReadOnly ? null : _imClient;
    }

    // Caret position in this control's coordinate space, used to place the IME candidate window.
    // _lastCaretPoint is document-space; the IME needs view (control) coordinates, so map here.
    private Rect GetCaretRectangle()
    {
        var p = MapDocToView(_lastCaretPoint);
        return new Rect(p.X, p.Y, 1, _lastCaretHeight);
    }

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

    // Steps a block caret. Horizontal (←/→) walks through the block: before -> (table cells |
    // image after) -> after -> next text, and the reverse. Vertical (↑/↓) treats the block as one
    // opaque unit and skips straight to the neighboring paragraph — cells are entered with →,
    // Tab, or a click, not by arrowing down through the document.
    private void HandleBlockCaretArrow(bool forward, bool vertical = false)
    {
        var blk = _caretBlock!;
        if (forward)
        {
            if (!_caretBlockAfter && !vertical)
            {
                if (blk is TableBlock tb && tb.LogicalCells().Any())
                { _caretBlock = null; var c = tb.LogicalCells().First().cell; SetCaretCollapsed(c, 0); }
                else _caretBlockAfter = true; // image: before -> after
            }
            else { _caretBlock = null; MoveCaretToBlockNeighbor(blk, before: false); }
        }
        else
        {
            if (_caretBlockAfter && !vertical)
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

    // The caret paragraph's TextLayout at the SAME wrap width the renderer uses — cell-aware (a cell wraps
    // at its column width minus padding, a top-level paragraph at the content width minus indents).
    private Avalonia.Media.TextFormatting.TextLayout CaretLineLayout(Paragraph p)
    {
        if (FindCell(p) is { } loc)
        {
            var tb = loc.tb;
            var tl = LayoutTable(tb, 10 + tb.Indent, 0); // width (ColX) is top-independent
            var (cs, _) = tb.SpanOf(loc.r, loc.c);
            double cellWidth = tl.ColX[Math.Min(loc.c + cs, tb.Columns)] - tl.ColX[loc.c];
            return BuildTextLayout(p, Math.Max(10, cellWidth - 10));
        }
        return BuildTextLayout(p, Math.Max(10, ContentLayoutWidth - 20 - ParaLeft(p) - p.MarginRight));
    }

    // Home/End: move to the start/end of the caret's current VISUAL (wrapped) line, found from the
    // layout's line metrics rather than a far-right hit-test — which on a wrapped line returns the NEXT
    // line's leading position (trailing), overshooting onto the following line. For End the trailing
    // whitespace that caused the wrap is trimmed so the caret stays at the visible end of THIS line.
    private void MoveToVisualLineEdge(bool toEnd, bool shift)
    {
        var p = _caretPosition.Paragraph;
        if (p != null)
        {
            var layout = CaretLineLayout(p);
            string plain = BuildPlain(p);
            int len = plain.Length;
            int off = Math.Clamp(_caretPosition.Offset, 0, len);
            double caretY = layout.HitTestTextPosition(off).Y; // the caret's current visual line
            int target = toEnd ? len : 0;
            var lines = layout.TextLines;
            for (int i = 0; i < lines.Count; i++)
            {
                int s = lines[i].FirstTextSourceIndex;
                double nextY = i + 1 < lines.Count
                    ? layout.HitTestTextPosition(lines[i + 1].FirstTextSourceIndex).Y
                    : double.PositiveInfinity;
                if (caretY + 0.5 < nextY) // caretY belongs to line i
                {
                    if (!toEnd) target = s;
                    else
                    {
                        int e = Math.Min(s + lines[i].Length, len);
                        while (e > s && char.IsWhiteSpace(plain[e - 1])) e--; // soft-wrap/newline trailing ws
                        target = e;
                    }
                    break;
                }
            }
            _caretPosition = new TextPointer(p, Math.Clamp(target, 0, len));
        }
        ApplyCaretSelection(shift);
    }

    // Tries to move the caret up/down by ONE visual (wrapped) line within its current paragraph (a cell
    // or a top-level paragraph), keeping the caret's current X. Returns false when the caret is already
    // on the paragraph's first (up) / last (down) visual line — the caller then crosses to an adjacent
    // cell / paragraph or leaves the table. This makes ↑/↓ navigate a multi-line cell's own lines first
    // (the old fixed ±20/30 px step skipped lines shorter than the step and jumped straight to another
    // cell), matching Word/Excel.
    private bool TryMoveCaretByLineWithinParagraph(bool down)
    {
        var p = _caretPosition.Paragraph;
        if (p == null) return false;
        var layout = CaretLineLayout(p);
        var lines = layout.TextLines;
        if (lines.Count <= 1) return false; // single visual line: nothing to move within
        int len = BuildPlain(p).Length;
        int off = Math.Clamp(_caretPosition.Offset, 0, len);
        var cur = layout.HitTestTextPosition(off);
        // The caret's current visual line = first line whose following line starts below the caret.
        int li = lines.Count - 1;
        for (int i = 0; i < lines.Count; i++)
        {
            double nextY = i + 1 < lines.Count
                ? layout.HitTestTextPosition(lines[i + 1].FirstTextSourceIndex).Y
                : double.PositiveInfinity;
            if (cur.Y + 0.5 < nextY) { li = i; break; }
        }
        int targetLine = down ? li + 1 : li - 1;
        if (targetLine < 0 || targetLine >= lines.Count) return false; // already on the first/last line
        double targetY = layout.HitTestTextPosition(lines[targetLine].FirstTextSourceIndex).Y;
        var hit = layout.HitTestPoint(new Point(cur.X, targetY + 1)); // same X, one line over
        int idx = hit.TextPosition + (hit.IsTrailing ? 1 : 0);
        _caretPosition = new TextPointer(p, Math.Clamp(idx, 0, len));
        return true;
    }

    // The caret cell's rectangle in document space, or null when the caret isn't in a table cell. Used
    // by ↑/↓ to step just past the cell's top/bottom edge when crossing to an adjacent row — a fixed
    // pixel step doesn't clear a tall row (e.g. an empty cell beside a multi-line one), so the caret
    // stayed put ("no response").
    private Avalonia.Rect? CaretCellRect()
    {
        var p = _caretPosition.Paragraph;
        if (p == null || FindCell(p) is not { } fc || GetTableRect(fc.tb) is not { } tr) return null;
        var (ar, ac) = fc.tb.AnchorOf(fc.r, fc.c);
        foreach (var (r, c, rect) in tr.tl.AnchorRects)
            if (r == ar && c == ac) return rect;
        return null;
    }

    // The image/table block whose rendered vertical span contains y, or null (used for Up/Down into a block).
    private Block? BlockAtY(double y)
    {
        if (Document == null) return null;
        double yOffset = 0, maxWidth = ContentLayoutWidth;
        foreach (var block in Document.Blocks)
        {
            yOffset += block.MarginTop;
            double top = yOffset;
            double h = BlockExtent(block, maxWidth, top, out _, out _);
            // Only image/table blocks are "entered" by Up/Down arrow navigation.
            if ((block is TableBlock || block is ImageBlock) && y >= top && y <= top + h) return block;
            yOffset += h + block.MarginBottom;
        }
        return null;
    }

    // One caret/delete step treats a surrogate pair (e.g. an emoji) as a single character.
    // Splitting a pair leaves a lone half behind, which renders as a broken glyph.
    private static int PrevCharBoundary(string text, int offset)
    {
        if (offset >= 2 && offset <= text.Length
            && char.IsLowSurrogate(text[offset - 1]) && char.IsHighSurrogate(text[offset - 2]))
            return offset - 2;
        return offset - 1;
    }

    private static int NextCharBoundary(string text, int offset)
    {
        if (offset >= 0 && offset + 1 < text.Length
            && char.IsHighSurrogate(text[offset]) && char.IsLowSurrogate(text[offset + 1]))
            return offset + 2;
        return offset + 1;
    }

    private void MoveCaretRight()
    {
        if (_caretPosition.Paragraph == null) return;
        int pLen = GetParagraphLength(_caretPosition.Paragraph);
        if (_caretPosition.Offset < pLen)
        {
            _caretPosition.Offset = NextCharBoundary(BuildPlain(_caretPosition.Paragraph), _caretPosition.Offset);
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
            _caretPosition.Offset = PrevCharBoundary(BuildPlain(_caretPosition.Paragraph), _caretPosition.Offset);
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
}
