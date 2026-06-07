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
using Avalonia.Threading;
using AvaloniaRichTextBoxPort.Documents;

namespace AvaloniaRichTextBoxPort.Controls;

using System.Threading.Tasks;
using System.Collections.Generic;

public class CustomRichTextBox : Control
{

    private DispatcherTimer _caretTimer;
    private UndoManager _undoManager = new UndoManager();
    private bool _isCaretVisible;
    private TextPointer _caretPosition = new TextPointer(null, 0);
    private TextPointer _selectionStart = new TextPointer(null, 0);
    private TextPointer _selectionEnd = new TextPointer(null, 0);
    private bool _isSelecting = false;
    private Point _lastCaretPoint;
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

    public static readonly StyledProperty<FlowDocument?> DocumentProperty =
        AvaloniaProperty.Register<CustomRichTextBox, FlowDocument?>(nameof(Document));

    public FlowDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<CustomRichTextBox, bool>(nameof(IsReadOnly));

    // When true: text input, edits, paste, and resizing are blocked; selection/copy still work and
    // the caret is hidden. Used by NativeEditor's ReadOnly mode.
    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    static CustomRichTextBox()
    {
        AffectsRender<CustomRichTextBox>(DocumentProperty);
    }

    private readonly RtbInputMethodClient _imClient;
    private string? _preeditText; // IME composition text shown inline at the caret while composing.

    public CustomRichTextBox()
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty && Document != null)
        {
            UpdateParents(Document);
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
                if (Document != null) _undoManager.PushState(Document, _caretPosition);
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
        foreach (var b in _columnBoundaries)
        {
            if (b.rect.Contains(point))
            {
                if (Document != null) _undoManager.PushState(Document, _caretPosition);
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
                if (Document != null) _undoManager.PushState(Document, _caretPosition);
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
        if (Document != null) _undoManager.PushState(Document, _caretPosition);
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

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        
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
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (IsReadOnly) return;
        if (string.IsNullOrEmpty(e.Text)) return;
        _selectedBlock = null;
        _caretBlock = null;
        if (Document != null) _undoManager.PushState(Document, _caretPosition);
        InsertText(e.Text);
        e.Handled = true;
    }

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
                if (Document != null) _undoManager.PushState(Document, _caretPosition);
                _caretBlock.Indent = Math.Min(_caretBlock.Indent + 20, 600);
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            if ((e.Key == Key.Tab && shift) || (e.Key == Key.Back && _caretBlock.Indent > 0))
            {
                if (Document != null) _undoManager.PushState(Document, _caretPosition);
                _caretBlock.Indent = Math.Max(0, _caretBlock.Indent - 20);
                ResetCaretBlink(); InvalidateVisual(); e.Handled = true; return;
            }
            if ((e.Key == Key.Back || e.Key == Key.Delete) && Document != null)
            {
                _undoManager.PushState(Document, _caretPosition);
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

        // Any key other than the deletion keys cancels a block-image selection.
        if (_selectedBlock != null && e.Key != Key.Back && e.Key != Key.Delete)
        {
            _selectedBlock = null;
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

        if (e.Key == Key.C && ctrl)
        {
            CopySelectionToClipboard();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.X && ctrl)
        {
            if (Document != null) _undoManager.PushState(Document, _caretPosition);
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
            if (Document != null) _undoManager.PushState(Document, _caretPosition);
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
                    _undoManager.PushState(Document, _caretPosition);
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
        InvalidateVisual();
        NotifyStatus();
    }

    private bool _bringCaretIntoView;

    // Raised whenever the caret moves or the text changes, so a host (status bar/toolbar) can refresh.
    public event EventHandler? StatusChanged;
    private void NotifyStatus()
    {
        InvalidateMeasure(); // content height may have changed -> let the ScrollViewer update its extent
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    // Snapshot of the formatting at the caret, for a host toolbar to reflect (active B/I/U, size, etc.).
    public readonly record struct CaretFormat(bool Bold, bool Italic, bool Underline, bool Strike,
        double FontSize, string? FontFamily, TextAlignment Align, ListKind List, int Heading);

    private static bool HasDeco(Run? r, TextDecorationLocation loc)
    {
        if (r == null) return false;
        if (r.TextDecorations == null)
            return loc == TextDecorationLocation.Underline && !string.IsNullOrEmpty(r.NavigateUri);
        foreach (var d in r.TextDecorations) if (d.Location == loc) return true;
        return false;
    }

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
            p?.HeadingLevel ?? 0);
    }

    // Document character count, word count, and the caret's 1-based line/column (treating each paragraph
    // break and embedded "\n" as a line break). Inline images count as one character.
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
        e.Client = _imClient;
    }

    // --- Start in the native IME mode (e.g. Hangul on Korean Windows) -------------------------------
    // Windows-only: flip the window's IME to native (Hangul) conversion mode the first time we get focus.
    private bool _imeInitDone;
    private const int IME_CMODE_NATIVE = 0x0001;

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmGetConversionStatus(IntPtr hIMC, out int conversion, out int sentence);
    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmSetConversionStatus(IntPtr hIMC, int conversion, int sentence);

    private void EnsureNativeImeMode()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var hwnd = (TopLevel.GetTopLevel(this) as Window)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;
            var hImc = ImmGetContext(hwnd);
            if (hImc == IntPtr.Zero) return;
            if (ImmGetConversionStatus(hImc, out int conv, out int sentence))
                ImmSetConversionStatus(hImc, conv | IME_CMODE_NATIVE, sentence);
            ImmReleaseContext(hwnd, hImc);
        }
        catch { }
    }

    private void InitNativeImeOnce()
    {
        if (_imeInitDone || IsReadOnly) return;
        _imeInitDone = true;
        // Posted so it runs after Avalonia has set up the focus/IME context for this window.
        Avalonia.Threading.Dispatcher.UIThread.Post(EnsureNativeImeMode);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InitNativeImeOnce();
    }

    // Caret position in this control's coordinate space, used to place the IME candidate window.
    private Rect GetCaretRectangle() => new Rect(_lastCaretPoint.X, _lastCaretPoint.Y, 1, 20);

    private void SetPreedit(string? text)
    {
        _preeditText = text;
        InvalidateVisual();
    }

    private sealed class RtbInputMethodClient : Avalonia.Input.TextInput.TextInputMethodClient
    {
        private readonly CustomRichTextBox _owner;
        public RtbInputMethodClient(CustomRichTextBox owner) => _owner = owner;

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
        foreach(var block in doc.Blocks)
        {
            block.Parent = doc;
            if (block is Paragraph p)
            {
                foreach(var inline in p.Inlines) inline.Parent = p;
            }
            else if (block is TableBlock tb)
            {
                for(int r=0; r<tb.Rows; r++)
                for(int c=0; c<tb.Columns; c++)
                {
                    var cell = tb.Cells[r][c];
                    cell.Parent = tb;
                    foreach(var inline in cell.Inlines) inline.Parent = cell;
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

        if (Document != null) _undoManager.PushState(Document, _caretPosition);
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

    private Avalonia.Media.TextFormatting.TextLayout BuildTextLayout(Paragraph p, double maxWidth,
        int preeditOffset = -1, string? preeditText = null)
    {
        var defaultProps = new Avalonia.Media.TextFormatting.GenericTextRunProperties(
            Typeface.Default, 14, null, Brushes.Black);

        var segs = new List<LayoutSeg>();
        foreach (var inline in p.Inlines)
        {
            if (inline is Run r && !string.IsNullOrEmpty(r.Text))
            {
                var family = string.IsNullOrEmpty(r.FontFamily) ? FontFamily.Default : new FontFamily(r.FontFamily);
                var typeface = new Typeface(family, r.FontStyle, r.FontWeight);
                TextDecorationCollection? decos = r.TextDecorations;
                if (decos == null && !string.IsNullOrEmpty(r.NavigateUri)) decos = TextDecorations.Underline;
                var props = new Avalonia.Media.TextFormatting.GenericTextRunProperties(
                    typeface,
                    r.FontSize <= 0 ? 14 : r.FontSize,
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

        return new Avalonia.Media.TextFormatting.TextLayout(
            new ParagraphTextSource(segs), paraProps, null, Math.Max(1, maxWidth));
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

    // ---- HTML interop (used by NativeEditor / external hosts) ----

    public string GetHtml() => Document != null ? Formatters.HtmlDocumentFormatter.ToHtml(Document) : "";

    public void SetHtml(string? html)
    {
        var doc = string.IsNullOrEmpty(html) ? new FlowDocument() : Formatters.HtmlDocumentFormatter.ParseHtml(html);
        Document = doc; // OnPropertyChanged runs UpdateParents/NormalizeBlocks
        var first = GetAllParagraphsInOrder().FirstOrDefault();
        _caretPosition = new TextPointer(first, 0);
        CollapseSelectionToCaret();
        _undoManager = new UndoManager();
        InvalidateVisual();
    }

    public void InsertHtml(string html)
    {
        if (Document == null || string.IsNullOrEmpty(html)) return;
        var parsed = Formatters.HtmlDocumentFormatter.ParseHtml(html);
        if (parsed.Blocks.Count == 0) return;
        _undoManager.PushState(Document, _caretPosition);
        InsertParsedDocument(parsed);
        InvalidateVisual();
    }

    public async Task PasteFromClipboardAsync()
    {
        if (Document == null) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        string? text = await clipboard.TryGetTextAsync();

        // 1. Internal rich clipboard: if the system text still matches what we last copied
        //    in-app, paste the formatted version (blocks/tables when available, else runs).
        if (_internalClipboardText != null && text == _internalClipboardText &&
            (_internalClipboardBlocks != null || _internalClipboard != null))
        {
            _undoManager.PushState(Document, _caretPosition);
            if (_internalClipboardBlocks != null)
            {
                InsertBlocks(_internalClipboardBlocks);
                InvalidateVisual();
            }
            else
            {
                InsertRuns(_internalClipboard!);
            }
            return;
        }

        // 2. External HTML (from browsers, Word, etc.): parse and insert with formatting.
        string? html = await TryGetHtmlAsync(clipboard);
        if (!string.IsNullOrEmpty(html))
        {
            // Malformed/exotic HTML can make the parser throw; fall through to the plain-text
            // fallback below instead of letting the exception escape this fire-and-forget task.
            try
            {
                string fragment = ExtractHtmlFragment(html!);
                var parsed = Formatters.HtmlDocumentFormatter.ParseHtml(fragment);
                if (parsed.Blocks.Count > 0)
                {
                    _undoManager.PushState(Document, _caretPosition);
                    InsertParsedDocument(parsed);
                    InvalidateVisual();
                    return;
                }
            }
            catch { /* fall back to plain text */ }
        }

        // 3. Bitmap image on the clipboard (e.g. a screenshot or copied picture).
        var clipImage = await TryGetImageAsync(clipboard);
        if (clipImage != null)
        {
            InsertImage(Downscale(clipImage));
            return;
        }

        // 4. Tab-separated text (e.g. Excel/HWP cells copied without HTML) -> rebuild as a table.
        if (!string.IsNullOrEmpty(text) && LooksTabular(text))
        {
            _undoManager.PushState(Document, _caretPosition);
            InsertTableFromTsv(text);
            InvalidateVisual();
            return;
        }

        // 5. Plain text fallback.
        if (!string.IsNullOrEmpty(text))
        {
            _undoManager.PushState(Document, _caretPosition);
            InsertText(text);
        }
    }

    // Heuristic: a tab plus at least one row that splits into 2+ columns => tabular paste.
    private static bool LooksTabular(string text)
    {
        if (!text.Contains('\t')) return false;
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            if (line.Split('\t').Length >= 2) return true;
        return false;
    }

    private void InsertTableFromTsv(string text)
    {
        if (Document == null) return;
        var lines = new List<string>(text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        while (lines.Count > 1 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        int cols = 1;
        foreach (var l in lines) cols = Math.Max(cols, l.Split('\t').Length);

        var tb = new TableBlock(lines.Count, cols);
        tb.Cells.Clear();
        tb.ColumnWidths.Clear();
        for (int c = 0; c < cols; c++) tb.ColumnWidths.Add(100);
        foreach (var l in lines)
        {
            var parts = l.Split('\t');
            var row = new List<Paragraph>();
            for (int c = 0; c < cols; c++)
            {
                var pp = new Paragraph();
                pp.Inlines.Add(new Run { Text = c < parts.Length ? parts[c] : "" });
                row.Add(pp);
            }
            tb.Cells.Add(row);
        }
        tb.Rows = lines.Count;
        tb.Columns = cols;
        InsertBlockAtCaret(tb);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = (!IsReadOnly && e.DataTransfer.Contains(DataFormat.File)) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (IsReadOnly) return;
        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;
        foreach (var f in files)
        {
            var path = f.Path?.LocalPath;
            if (string.IsNullOrEmpty(path)) continue;
            try
            {
                using var st = System.IO.File.OpenRead(path);
                var bmp = new Avalonia.Media.Imaging.Bitmap(st);
                InsertImage(Downscale(bmp));
            }
            catch { }
        }
    }

    // Scales an image down to fit within maxW x maxH (keeping aspect), matching Jodit's image cap.
    private static Avalonia.Media.Imaging.Bitmap Downscale(Avalonia.Media.Imaging.Bitmap bmp, int maxW = 1920, int maxH = 1080)
    {
        var ps = bmp.PixelSize;
        if (ps.Width <= maxW && ps.Height <= maxH) return bmp;
        double ratio = Math.Min((double)maxW / ps.Width, (double)maxH / ps.Height);
        var size = new PixelSize(Math.Max(1, (int)(ps.Width * ratio)), Math.Max(1, (int)(ps.Height * ratio)));
        try { return bmp.CreateScaledBitmap(size, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality); }
        catch { return bmp; }
    }

    private static async Task<Avalonia.Media.Imaging.Bitmap?> TryGetImageAsync(IClipboard clipboard)
    {
        Avalonia.Input.IAsyncDataTransfer? dt;
        try { dt = await clipboard.TryGetDataAsync(); }
        catch { return null; }
        if (dt == null) return null;

        try
        {
            foreach (var item in dt.Items)
            {
                foreach (var fmt in item.Formats)
                {
                    var id = fmt.Identifier ?? fmt.ToString() ?? "";
                    bool looksImage = id.IndexOf("png", StringComparison.OrdinalIgnoreCase) >= 0
                        || id.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0
                        || id.IndexOf("bitmap", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!looksImage) continue;

                    object? raw;
                    try { raw = await item.TryGetRawAsync(fmt); }
                    catch { continue; }

                    if (raw is Avalonia.Media.Imaging.Bitmap bm) return bm;
                    byte[]? bytes = raw as byte[];
                    if (bytes == null && raw is System.IO.Stream s)
                    {
                        using var ms = new System.IO.MemoryStream();
                        s.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                    if (bytes is { Length: > 0 })
                    {
                        try { using var ms = new System.IO.MemoryStream(bytes); return new Avalonia.Media.Imaging.Bitmap(ms); }
                        catch { }
                    }
                }
            }
        }
        finally { (dt as IDisposable)?.Dispose(); }
        return null;
    }

    private static async Task<string?> TryGetHtmlAsync(IClipboard clipboard)
    {
        Avalonia.Input.IAsyncDataTransfer? dt;
        try { dt = await clipboard.TryGetDataAsync(); }
        catch { return null; }
        if (dt == null) return null;

        try
        {
            foreach (var item in dt.Items)
            {
                foreach (var fmt in item.Formats)
                {
                    var id = fmt.Identifier ?? fmt.ToString() ?? "";
                    if (id.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        object? raw;
                        try { raw = await item.TryGetRawAsync(fmt); }
                        catch { continue; }
                        if (raw is string s && !string.IsNullOrWhiteSpace(s)) return s;
                        if (raw is byte[] b) return System.Text.Encoding.UTF8.GetString(b);
                    }
                }
            }
        }
        finally
        {
            (dt as IDisposable)?.Dispose();
        }
        return null;
    }

    // Strips the Windows CF_HTML ("HTML Format") header/fragment markers down to the markup.
    private static string ExtractHtmlFragment(string raw)
    {
        const string startTag = "<!--StartFragment-->";
        const string endTag = "<!--EndFragment-->";
        int sf = raw.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        int ef = raw.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);
        if (sf >= 0 && ef > sf)
            return raw.Substring(sf + startTag.Length, ef - (sf + startTag.Length));

        if (raw.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
        {
            int lt = raw.IndexOf('<');
            if (lt >= 0) return raw.Substring(lt);
        }
        return raw;
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

    public void InsertImage(Avalonia.Media.Imaging.Bitmap image)
    {
        if (Document == null) return;
        _undoManager.PushState(Document, _caretPosition);
        var ib = new ImageBlock { Image = image, Width = image.Size.Width, Height = image.Size.Height };
        InsertBlockAtCaret(ib);
        InvalidateVisual();
    }

    public void InsertTable(int rows, int cols)
    {
        if (Document == null) return;
        _undoManager.PushState(Document, _caretPosition);
        // The (rows, cols) constructor builds Cells, ColumnWidths and the span grids together, all
        // consistent. (An object initializer that rebuilt Cells alone would desync the span grids.)
        var tb = new TableBlock(rows, cols);
        InsertBlockAtCaret(tb);
        InvalidateVisual();
    }

    public void ToggleBold() { ApplyStyleToSelection(r => r.FontWeight = r.FontWeight == FontWeight.Bold ? FontWeight.Normal : FontWeight.Bold); }
    public void ToggleItalic() { ApplyStyleToSelection(r => r.FontStyle = r.FontStyle == FontStyle.Italic ? FontStyle.Normal : FontStyle.Italic); }
    public void SetFontSize(double size) { ApplyStyleToSelection(r => r.FontSize = size); }
    public void SetForeground(IBrush brush) { ApplyStyleToSelection(r => r.Foreground = brush); }
    public void SetFontFamily(string family) { ApplyStyleToSelection(r => r.FontFamily = family); }
    public void SetHighlight(IBrush? brush) { ApplyStyleToSelection(r => r.Background = brush); }

    public void Indent(double delta)
    {
        if (_caretPosition.Paragraph == null) return;
        if (Document != null) _undoManager.PushState(Document, _caretPosition);
        var p = _caretPosition.Paragraph;
        p.Indent = Math.Clamp(p.Indent + delta, 0, 400);
        InvalidateVisual();
    }
    public void SetTextAlignment(TextAlignment align) { if (_caretPosition.Paragraph != null) { if (Document != null) _undoManager.PushState(Document, _caretPosition); _caretPosition.Paragraph.TextAlignment = align; InvalidateVisual(); } }
    public void SetLineHeight(double height) { if (_caretPosition.Paragraph != null) { if (Document != null) _undoManager.PushState(Document, _caretPosition); _caretPosition.Paragraph.LineHeight = height; InvalidateVisual(); } }
    public void ToggleBullet() { SetListType(ListKind.Bullet); }
    public void ToggleNumbering() { SetListType(ListKind.Ordered); }

    private void SetListType(ListKind kind)
    {
        if (_caretPosition.Paragraph == null || Document == null) return;
        _undoManager.PushState(Document, _caretPosition);
        bool turningOff = _caretPosition.Paragraph.ListType == kind;

        // Apply to every selected top-level paragraph (just the caret's when there's no selection).
        var targets = SelectedTopLevelParagraphs();
        if (targets.Count == 0)
        {
            // Caret in a table cell etc. -> just flag that paragraph.
            _caretPosition.Paragraph.ListType = turningOff ? ListKind.None : kind;
            InvalidateVisual();
            return;
        }
        if (turningOff)
        {
            foreach (var tp in targets) tp.ListType = ListKind.None;
            UpdateParents(Document);
            InvalidateVisual();
            return;
        }
        // Turning a list on: split each target's hard lines (\n) into independent list-item paragraphs.
        // Process from the bottom up so earlier block indices stay valid while we splice. The selection
        // anchors and caret are re-mapped onto the split items so the highlight (and caret) are kept.
        var ssP = _selectionStart.Paragraph; int ssO = _selectionStart.Offset;
        var seP = _selectionEnd.Paragraph; int seO = _selectionEnd.Offset;
        var cpP = _caretPosition.Paragraph; int cpO = _caretPosition.Offset;
        TextPointer? nSs = null, nSe = null, nCp = null;

        // Maps an (offset within a multi-line paragraph) onto the matching split item + local offset.
        (Paragraph, int) MapInto(List<Paragraph> items, Paragraph tp, int off)
        {
            string plain = BuildPlain(tp);
            int line = 0, lineStart = 0, lim = Math.Min(off, plain.Length);
            for (int i = 0; i < lim; i++) if (plain[i] == '\n') { line++; lineStart = i + 1; }
            var it = items[Math.Min(line, items.Count - 1)];
            return (it, Math.Min(off - lineStart, GetParagraphLength(it)));
        }

        foreach (var tp in targets.OrderByDescending(t => Document.Blocks.IndexOf(t)))
        {
            int idx = Document.Blocks.IndexOf(tp);
            if (idx < 0) { tp.ListType = kind; continue; }
            var items = SplitByNewlines(tp);
            foreach (var it in items) { it.ListType = kind; it.Parent = Document; }
            Document.Blocks.RemoveAt(idx);
            for (int k = 0; k < items.Count; k++) Document.Blocks.Insert(idx + k, items[k]);
            if (tp == ssP) { var (p2, o2) = MapInto(items, tp, ssO); nSs = new TextPointer(p2, o2); }
            if (tp == seP) { var (p2, o2) = MapInto(items, tp, seO); nSe = new TextPointer(p2, o2); }
            if (tp == cpP) { var (p2, o2) = MapInto(items, tp, cpO); nCp = new TextPointer(p2, o2); }
        }
        if (nSs != null) _selectionStart = nSs;
        if (nSe != null) _selectionEnd = nSe;
        if (nCp != null) _caretPosition = nCp;
        UpdateParents(Document);
        InvalidateVisual();
    }

    // Top-level paragraphs touched by the current selection (or just the caret's when collapsed).
    private List<Paragraph> SelectedTopLevelParagraphs()
    {
        var result = new List<Paragraph>();
        if (Document == null) return result;
        var all = GetAllParagraphsInOrder();
        int si = _selectionStart.Paragraph != null ? all.IndexOf(_selectionStart.Paragraph) : -1;
        int ei = _selectionEnd.Paragraph != null ? all.IndexOf(_selectionEnd.Paragraph) : -1;
        if (si < 0 || ei < 0)
        {
            if (_caretPosition.Paragraph != null && Document.Blocks.Contains(_caretPosition.Paragraph))
                result.Add(_caretPosition.Paragraph);
            return result;
        }
        if (si > ei) (si, ei) = (ei, si);
        for (int i = si; i <= ei; i++)
            if (Document.Blocks.Contains(all[i])) result.Add(all[i]);
        return result;
    }

    // Splits a paragraph into one paragraph per hard line (\n), preserving inline formatting and the
    // paragraph's list/indent/alignment/background. Newlines are dropped (each becomes a paragraph break).
    private List<Paragraph> SplitByNewlines(Paragraph p)
    {
        var result = new List<Paragraph>();
        Paragraph NewPara() => new Paragraph
        {
            ListType = p.ListType, ListLevel = p.ListLevel, Indent = p.Indent,
            TextAlignment = p.TextAlignment, Background = p.Background
        };
        var cur = NewPara();
        foreach (var inl in p.Inlines)
        {
            if (inl is Run run && run.Text != null && run.Text.Contains('\n'))
            {
                var parts = run.Text.Split('\n');
                for (int k = 0; k < parts.Length; k++)
                {
                    if (k > 0) { result.Add(cur); cur = NewPara(); }
                    if (parts[k].Length > 0)
                    {
                        var nr = (Run)run.Clone();
                        nr.Text = parts[k];
                        nr.Parent = cur;
                        cur.Inlines.Add(nr);
                    }
                }
            }
            else
            {
                var c = (Inline)inl.Clone();
                c.Parent = cur;
                cur.Inlines.Add(c);
            }
        }
        result.Add(cur);
        foreach (var pp in result)
            if (pp.Inlines.Count == 0) pp.Inlines.Add(new Run { Text = "" });
        return result;
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
            ListType = p.ListType, ListLevel = p.ListLevel, Indent = p.Indent,
            TextAlignment = p.TextAlignment, Background = p.Background
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

    // Applies a heading level (0 = body) to the caret's paragraph and resizes its runs so it
    // renders in-editor; the level is also kept for HTML round-trip (<h1>..<h6>).
    public void SetHeading(int level)
    {
        if (_caretPosition.Paragraph == null) return;
        if (Document != null) _undoManager.PushState(Document, _caretPosition);
        var p = _caretPosition.Paragraph;
        p.HeadingLevel = level;
        double size = level switch { 1 => 24, 2 => 20, 3 => 16, 4 => 14, 5 => 13, 6 => 12, _ => 14 };
        var weight = level is >= 1 and <= 6 ? FontWeight.Bold : FontWeight.Normal;
        foreach (var inl in p.Inlines) if (inl is Run r) { r.FontSize = size; r.FontWeight = weight; }
        InvalidateVisual();
    }

    public void ToggleStrikethrough() { ApplyStyleToSelection(r => r.TextDecorations = ToggleDecoration(r.TextDecorations, TextDecorationLocation.Strikethrough)); }
    public void ToggleUnderline() { ApplyStyleToSelection(r => r.TextDecorations = ToggleDecoration(r.TextDecorations, TextDecorationLocation.Underline)); }

    // Toggles a single decoration (underline/strikethrough) while preserving the other, so the two
    // can coexist on the same run instead of overwriting each other.
    private static TextDecorationCollection? ToggleDecoration(TextDecorationCollection? current, TextDecorationLocation loc)
    {
        var result = new TextDecorationCollection();
        bool had = false;
        if (current != null)
            foreach (var d in current)
            {
                if (d.Location == loc) { had = true; continue; }
                result.Add(d);
            }
        if (!had) result.Add(new TextDecoration { Location = loc });
        return result.Count > 0 ? result : null;
    }

    private void ApplyStyleToSelection(Action<Run> styleAction)
    {
        if (Document != null) _undoManager.PushState(Document, _caretPosition);
        if (_selectionStart != null && _selectionEnd != null && _selectionStart.CompareTo(_selectionEnd) != 0)
        {
            var range = new TextRange(_selectionStart, _selectionEnd);
            range.ApplyPropertyValue(styleAction);
        }
        else if (_caretPosition.Paragraph != null)
        {
            foreach (var inline in _caretPosition.Paragraph.Inlines)
                if (inline is Run r) styleAction(r);
        }
        InvalidateVisual();
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
        var brush = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215));
        foreach (var rect in layout.HitTestTextRange(startOffset, endOffset - startOffset))
            context.FillRectangle(brush, new Rect(originX + rect.X, originY + rect.Y, rect.Width, rect.Height));
    }

    // ---------------- Find / Replace ----------------

    public bool FindNext(string query, bool matchCase)
    {
        if (Document == null || string.IsNullOrEmpty(query)) return false;
        var paras = GetAllParagraphsInOrder();
        int pi = _selectionEnd.Paragraph != null ? paras.IndexOf(_selectionEnd.Paragraph) : -1;
        return FindCore(query, matchCase, backwards: false, wrap: true, fromPi: pi, fromOff: _selectionEnd.Offset);
    }

    public bool FindPrev(string query, bool matchCase)
    {
        if (Document == null || string.IsNullOrEmpty(query)) return false;
        var paras = GetAllParagraphsInOrder();
        int pi = _selectionStart.Paragraph != null ? paras.IndexOf(_selectionStart.Paragraph) : -1;
        return FindCore(query, matchCase, backwards: true, wrap: true, fromPi: pi, fromOff: _selectionStart.Offset);
    }

    // Replaces the current selection if it equals the query, then advances to the next match.
    public bool ReplaceNext(string query, string replacement, bool matchCase)
    {
        if (Document == null || string.IsNullOrEmpty(query)) return false;
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        bool selMatches = _selectionStart.Paragraph != null && _selectionStart.CompareTo(_selectionEnd) != 0
            && string.Equals(new TextRange(_selectionStart, _selectionEnd).GetText(), query, cmp);
        if (selMatches)
        {
            _undoManager.PushState(Document, _caretPosition);
            ReplaceSelectionText(replacement);
            InvalidateVisual();
        }
        return FindNext(query, matchCase);
    }

    public int ReplaceAll(string query, string replacement, bool matchCase)
    {
        if (Document == null || string.IsNullOrEmpty(query)) return 0;
        var paras = GetAllParagraphsInOrder();
        if (paras.Count == 0) return 0;
        _undoManager.PushState(Document, _caretPosition);
        _caretPosition = new TextPointer(paras[0], 0);
        CollapseSelectionToCaret();
        int count = 0;
        while (count <= 1_000_000)
        {
            var cur = GetAllParagraphsInOrder();
            int pi = _caretPosition.Paragraph != null ? cur.IndexOf(_caretPosition.Paragraph) : -1;
            if (!FindCore(query, matchCase, backwards: false, wrap: false, fromPi: pi, fromOff: _caretPosition.Offset)) break;
            ReplaceSelectionText(replacement);
            count++;
        }
        InvalidateVisual();
        return count;
    }

    private bool FindCore(string query, bool matchCase, bool backwards, bool wrap, int fromPi, int fromOff)
    {
        var paras = GetAllParagraphsInOrder();
        if (paras.Count == 0) return false;
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var matches = new List<(int pi, int idx, Paragraph p)>();
        for (int pi = 0; pi < paras.Count; pi++)
        {
            string text = BuildPlain(paras[pi]);
            int from = 0;
            while (from <= text.Length)
            {
                int idx = text.IndexOf(query, from, cmp);
                if (idx < 0) break;
                matches.Add((pi, idx, paras[pi]));
                from = idx + 1;
            }
        }
        if (matches.Count == 0) return false;

        if (!backwards)
        {
            foreach (var m in matches)
                if (m.pi > fromPi || (m.pi == fromPi && m.idx >= fromOff)) { SelectMatch(m.p, m.idx, query.Length); return true; }
            if (wrap) { SelectMatch(matches[0].p, matches[0].idx, query.Length); return true; }
        }
        else
        {
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var m = matches[i];
                if (m.pi < fromPi || (m.pi == fromPi && m.idx < fromOff)) { SelectMatch(m.p, m.idx, query.Length); return true; }
            }
            if (wrap) { var m = matches[^1]; SelectMatch(m.p, m.idx, query.Length); return true; }
        }
        return false;
    }

    private void SelectMatch(Paragraph p, int start, int length)
    {
        _selectionStart = new TextPointer(p, start);
        _selectionEnd = new TextPointer(p, start + length);
        _caretPosition = new TextPointer(p, start + length);
        ResetCaretBlink();
        InvalidateVisual();
    }

    private void ReplaceSelectionText(string replacement)
    {
        DeleteSelection();
        if (!string.IsNullOrEmpty(replacement) && _caretPosition.Paragraph != null)
        {
            TryInsertTextCore(_caretPosition.Paragraph, replacement, _caretPosition.Offset);
            _caretPosition.Offset += replacement.Length;
        }
        CollapseSelectionToCaret();
    }

    // ---------------- Context menu (right-click) ----------------

    private static MenuItem Mi(string header, Action action, bool enabled = true)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
        mi.Click += (_, _) => action();
        return mi;
    }

    private static MenuItem Sub(string header, params Control[] children)
    {
        var mi = new MenuItem { Header = header };
        mi.ItemsSource = children;
        return mi;
    }

    private void CollapseSelectionToCaret()
    {
        _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
    }

    private void ShowContextMenu(Point point)
    {
        if (Document == null) return;

        bool hasSelection = _selectionStart.Paragraph != null && _selectionEnd.Paragraph != null
            && _selectionStart.CompareTo(_selectionEnd) != 0;

        if (IsReadOnly)
        {
            var roItems = new List<Control> { Mi("복사", CopySelectionToClipboard, hasSelection), Mi("모두 선택", SelectAll) };
            var roMenu = new ContextMenu { Placement = PlacementMode.Pointer };
            roMenu.ItemsSource = roItems;
            roMenu.Open(this);
            return;
        }

        var items = new List<Control>();
        var block = GetBlockAtPoint(point);

        if (block is ImageBlock ib)
        {
            _selectedBlock = ib;
            CollapseSelectionToCaret();
            BuildImageMenu(items, ib);
        }
        else if (block is TableBlock tbk)
        {
            _selectedBlock = null;
            var tp = GetPositionFromPoint(point);
            _caretPosition = tp;
            if (!hasSelection) CollapseSelectionToCaret();
            BuildTableMenu(items, tbk, tp.Paragraph, hasSelection);
        }
        else
        {
            _selectedBlock = null;
            if (!hasSelection)
            {
                _caretPosition = GetPositionFromPoint(point);
                CollapseSelectionToCaret();
            }
            var link = GetLinkRunAtPoint(point);
            BuildTextMenu(items, hasSelection, link);
        }

        ResetCaretBlink();
        InvalidateVisual();

        if (items.Count == 0) return;
        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        menu.ItemsSource = items;
        menu.Open(this);
    }

    private void AddClipboardItems(List<Control> items, bool hasSelection)
    {
        items.Add(Mi("잘라내기", () =>
        {
            if (Document != null) _undoManager.PushState(Document, _caretPosition);
            CopySelectionToClipboard();
            DeleteSelection();
            InvalidateVisual();
        }, hasSelection));
        items.Add(Mi("복사", CopySelectionToClipboard, hasSelection));
        items.Add(Mi("붙여넣기", () => { _ = PasteFromClipboardAsync(); }));
        items.Add(Mi("삭제", () =>
        {
            if (Document != null) _undoManager.PushState(Document, _caretPosition);
            DeleteSelection();
            InvalidateVisual();
        }, hasSelection));
    }

    private void AddFormatItems(List<Control> items, bool hasSelection)
    {
        items.Add(Mi("굵게", ToggleBold, hasSelection));
        items.Add(Mi("기울임", ToggleItalic, hasSelection));
        items.Add(Mi("밑줄", ToggleUnderline, hasSelection));
        items.Add(Mi("취소선", ToggleStrikethrough, hasSelection));
        items.Add(Sub("글자 크기",
            Mi("10", () => SetFontSize(10)), Mi("12", () => SetFontSize(12)), Mi("14", () => SetFontSize(14)),
            Mi("18", () => SetFontSize(18)), Mi("24", () => SetFontSize(24)), Mi("36", () => SetFontSize(36))));
        items.Add(Sub("글자 색",
            Mi("검정", () => SetForeground(Brushes.Black)), Mi("빨강", () => SetForeground(Brushes.Red)),
            Mi("파랑", () => SetForeground(Brushes.Blue)), Mi("초록", () => SetForeground(Brushes.Green)),
            Mi("회색", () => SetForeground(Brushes.Gray))));
        items.Add(Sub("정렬",
            Mi("왼쪽", () => SetTextAlignment(TextAlignment.Left)),
            Mi("가운데", () => SetTextAlignment(TextAlignment.Center)),
            Mi("오른쪽", () => SetTextAlignment(TextAlignment.Right))));
        items.Add(Mi("서식 지우기", ClearFormatting, hasSelection));
        items.Add(Sub("목록",
            Mi("글머리표", ToggleBullet),
            Mi("번호 매기기", ToggleNumbering)));
        items.Add(Sub("제목",
            Mi("제목 1", () => SetHeading(1)),
            Mi("제목 2", () => SetHeading(2)),
            Mi("제목 3", () => SetHeading(3)),
            Mi("본문", () => SetHeading(0))));
        items.Add(Sub("글꼴",
            Mi("맑은 고딕", () => SetFontFamily("Malgun Gothic"), hasSelection),
            Mi("굴림", () => SetFontFamily("Gulim"), hasSelection),
            Mi("바탕", () => SetFontFamily("Batang"), hasSelection),
            Mi("돋움", () => SetFontFamily("Dotum"), hasSelection),
            Mi("Arial", () => SetFontFamily("Arial"), hasSelection),
            Mi("Times New Roman", () => SetFontFamily("Times New Roman"), hasSelection)));
        items.Add(Sub("형광펜",
            Mi("노랑", () => SetHighlight(Brushes.Yellow), hasSelection),
            Mi("연두", () => SetHighlight(Brushes.LightGreen), hasSelection),
            Mi("분홍", () => SetHighlight(Brushes.Pink), hasSelection),
            Mi("하늘", () => SetHighlight(Brushes.LightBlue), hasSelection),
            Mi("없음", () => SetHighlight(null), hasSelection)));
        items.Add(Sub("들여쓰기",
            Mi("들여쓰기 +", () => Indent(20)),
            Mi("내어쓰기 -", () => Indent(-20))));
    }

    public void InsertDivider()
    {
        if (Document == null) return;
        _undoManager.PushState(Document, _caretPosition);
        InsertBlockAtCaret(new DividerBlock());
        InvalidateVisual();
    }

    private void BuildTextMenu(List<Control> items, bool hasSelection, Run? link)
    {
        AddClipboardItems(items, hasSelection);
        items.Add(new Separator());
        AddFormatItems(items, hasSelection);
        items.Add(new Separator());
        if (link != null && !string.IsNullOrEmpty(link.NavigateUri))
        {
            items.Add(Mi("링크 열기", () => OpenUrl(link.NavigateUri!)));
            items.Add(Mi("링크 편집...", () => { _ = EditHyperlinkAsync(link.NavigateUri, link); }));
            items.Add(Mi("링크 제거", () => SetHyperlink(null, link)));
        }
        else
        {
            items.Add(Mi("링크 삽입...", () => { _ = EditHyperlinkAsync(null, null); }, hasSelection));
        }
        items.Add(new Separator());
        items.Add(Mi("모두 선택", SelectAll));
        items.Add(Mi("실행 취소", DoUndo));
        items.Add(Mi("다시 실행", DoRedo));
        items.Add(new Separator());
        items.Add(Mi("표 삽입 (2x2)", () => InsertTable(2, 2)));
        items.Add(Mi("이미지 삽입...", () => { _ = InsertImageFromFileAsync(); }));
        items.Add(Mi("구분선 삽입", InsertDivider));
    }

    private void BuildImageMenu(List<Control> items, ImageBlock img)
    {
        items.Add(Mi("삭제", () => DeleteBlock(img)));
        items.Add(new Separator());
        items.Add(Mi("원본 크기로", () => ResetImageSize(img), img.Image != null));
        items.Add(Mi("이미지 교체...", () => { _ = ReplaceImageAsync(img); }));
        items.Add(Mi("다른 이름으로 저장...", () => { _ = SaveImageAsync(img); }, img.Image != null));
    }

    private void BuildTableMenu(List<Control> items, TableBlock tb, Paragraph? cell, bool hasSelection)
    {
        AddClipboardItems(items, hasSelection);
        items.Add(new Separator());
        var loc = cell != null ? FindCell(cell) : null;
        int r = loc?.r ?? -1;
        int c = loc?.c ?? -1;
        items.Add(Mi("위에 행 삽입", () => TableInsertRow(tb, r), r >= 0));
        items.Add(Mi("아래에 행 삽입", () => TableInsertRow(tb, r + 1), r >= 0));
        items.Add(Mi("행 삭제", () => TableDeleteRow(tb, r), r >= 0 && tb.Rows > 1));
        items.Add(new Separator());
        items.Add(Mi("왼쪽에 열 삽입", () => TableInsertColumn(tb, c), c >= 0));
        items.Add(Mi("오른쪽에 열 삽입", () => TableInsertColumn(tb, c + 1), c >= 0));
        items.Add(Mi("열 삭제", () => TableDeleteColumn(tb, c), c >= 0 && tb.Columns > 1));
        items.Add(new Separator());
        var range = SelectedCellRange(tb);
        bool canMerge = range is { } rg && IsCleanRect(tb, rg.r0, rg.c0, rg.r1, rg.c1);
        items.Add(Mi("셀 병합", () =>
        {
            if (range is not { } g) return;
            if (Document != null) _undoManager.PushState(Document, _caretPosition);
            tb.MergeCells(g.r0, g.c0, g.r1, g.c1);
            if (Document != null) UpdateParents(Document);
            FocusCell(tb.Cells[g.r0][g.c0]);
            InvalidateVisual();
        }, canMerge));
        bool canUnmerge = r >= 0 && c >= 0 && (tb.SpanOf(r, c).cs > 1 || tb.SpanOf(r, c).rs > 1);
        items.Add(Mi("셀 병합 해제", () =>
        {
            if (Document != null) _undoManager.PushState(Document, _caretPosition);
            tb.UnmergeCell(r, c);
            if (Document != null) UpdateParents(Document);
            FocusCell(tb.Cells[r][c]);
            InvalidateVisual();
        }, canUnmerge));
        items.Add(new Separator());
        AddFormatItems(items, hasSelection);
        items.Add(new Separator());
        items.Add(Mi("표 삭제", () => DeleteBlock(tb)));
    }

    private (TableBlock tb, int r, int c)? FindCell(Paragraph p)
    {
        if (Document == null) return null;
        foreach (var b in Document.Blocks)
            if (b is TableBlock tb)
                for (int r = 0; r < tb.Rows; r++)
                    for (int c = 0; c < tb.Columns; c++)
                        if (tb.Cells[r][c] == p) return (tb, r, c);
        return null;
    }

    // Rectangular cell block (inclusive, span-aware) defined by the two selection *endpoints* — the
    // cell the drag started in and the cell it ended in. Using the endpoints (not every cell the linear
    // text selection passes through) makes a vertical drag select a vertical block, so up/down cells can
    // be merged. Returns null unless both endpoints are cells of `tb` and they differ.
    private (int r0, int c0, int r1, int c1)? SelectedCellRange(TableBlock tb)
    {
        if (_selectionStart.Paragraph == null || _selectionEnd.Paragraph == null) return null;
        if (FindCell(_selectionStart.Paragraph) is not { } s || s.tb != tb) return null;
        if (FindCell(_selectionEnd.Paragraph) is not { } e || e.tb != tb) return null;
        // Both endpoints in the same cell = a caret/text selection inside one cell, not a cell block.
        // (Must compare the cells directly: a merged cell spans rows/cols, so a span-expanded bounding
        // box would otherwise look multi-cell even for a single merged cell.)
        if (s.r == e.r && s.c == e.c) return null;
        var (scs, srs) = tb.SpanOf(s.r, s.c);
        var (ecs, ers) = tb.SpanOf(e.r, e.c);
        int r0 = Math.Min(s.r, e.r), c0 = Math.Min(s.c, e.c);
        int r1 = Math.Max(s.r + srs - 1, e.r + ers - 1), c1 = Math.Max(s.c + scs - 1, e.c + ecs - 1);
        return (r0, c0, r1, c1);
    }

    // True when the box is a mergeable rectangle: spans more than one cell and no anchor inside it
    // reaches outside the box (no partial overlap with an existing merge).
    private static bool IsCleanRect(TableBlock tb, int r0, int c0, int r1, int c1)
    {
        if (r0 < 0 || c0 < 0 || r1 >= tb.Rows || c1 >= tb.Columns) return false;
        if (r0 == r1 && c0 == c1) return false;
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                var (ar, ac) = tb.AnchorOf(r, c);
                if (ar < r0 || ac < c0) return false;
                var (cs, rs) = tb.SpanOf(ar, ac);
                if (ar + rs - 1 > r1 || ac + cs - 1 > c1) return false;
            }
        return true;
    }

    // Tab moves to the next table cell (Shift+Tab to the previous); Tab in the last cell appends a
    // new row. Outside a table it inserts spaces so focus doesn't leave the editor.
    private void HandleTab(bool shift)
    {
        var loc = _caretPosition.Paragraph != null ? FindCell(_caretPosition.Paragraph) : null;
        if (loc == null)
        {
            if (Document != null) _undoManager.PushState(Document, _caretPosition);
            InsertText("    ");
            return;
        }

        var (tb, r, c) = loc.Value;
        // Navigate over logical (anchor) cells so merged areas count once and covered cells are skipped.
        var anchors = tb.LogicalCells().Select(x => x.cell).ToList();
        int idx = anchors.IndexOf(_caretPosition.Paragraph!);
        if (idx < 0) { var (ar, ac) = tb.AnchorOf(r, c); idx = anchors.IndexOf(tb.Cells[ar][ac]); }
        if (shift)
        {
            if (idx > 0) FocusCell(anchors[idx - 1]);
        }
        else if (idx >= 0 && idx + 1 < anchors.Count)
        {
            FocusCell(anchors[idx + 1]);
        }
        else
        {
            if (Document != null) _undoManager.PushState(Document, _caretPosition);
            tb.InsertRow(tb.Rows);
            if (Document != null) UpdateParents(Document);
            FocusCell(tb.Cells[tb.Rows - 1][0]);
        }
    }

    private void FocusCell(Paragraph cell)
    {
        // If handed a covered cell, redirect the caret to its merge anchor.
        if (FindCell(cell) is { } loc && loc.tb.IsCovered(loc.r, loc.c))
        {
            var (ar, ac) = loc.tb.AnchorOf(loc.r, loc.c);
            cell = loc.tb.Cells[ar][ac];
        }
        int len = GetParagraphLength(cell);
        _caretPosition = new TextPointer(cell, len);
        _selectionStart = new TextPointer(cell, 0);
        _selectionEnd = new TextPointer(cell, len);
        ResetCaretBlink();
        InvalidateVisual();
    }

    private void DeleteBlock(Block b)
    {
        if (Document == null) return;
        _undoManager.PushState(Document, _caretPosition);
        Document.Blocks.Remove(b);
        _selectedBlock = null;
        NormalizeBlocks(Document);
        InvalidateVisual();
    }

    private void TableInsertRow(TableBlock tb, int at)
    {
        if (Document == null || at < 0) return;
        _undoManager.PushState(Document, _caretPosition);
        tb.InsertRow(at);
        UpdateParents(Document);
        int ar = Math.Clamp(at, 0, tb.Rows - 1);
        _caretPosition = new TextPointer(tb.Cells[ar][0], 0);
        CollapseSelectionToCaret();
        InvalidateVisual();
    }

    private void TableDeleteRow(TableBlock tb, int at)
    {
        if (Document == null || tb.Rows <= 1 || at < 0) return;
        _undoManager.PushState(Document, _caretPosition);
        tb.DeleteRow(at);
        UpdateParents(Document);
        int nr = Math.Clamp(at, 0, tb.Rows - 1);
        _caretPosition = new TextPointer(tb.Cells[nr][0], 0);
        CollapseSelectionToCaret();
        InvalidateVisual();
    }

    private void TableInsertColumn(TableBlock tb, int at)
    {
        if (Document == null || at < 0) return;
        _undoManager.PushState(Document, _caretPosition);
        tb.InsertColumn(at);
        UpdateParents(Document);
        int ac = Math.Clamp(at, 0, tb.Columns - 1);
        _caretPosition = new TextPointer(tb.Cells[0][ac], 0);
        CollapseSelectionToCaret();
        InvalidateVisual();
    }

    private void TableDeleteColumn(TableBlock tb, int at)
    {
        if (Document == null || tb.Columns <= 1 || at < 0) return;
        _undoManager.PushState(Document, _caretPosition);
        tb.DeleteColumn(at);
        UpdateParents(Document);
        int nc = Math.Clamp(at, 0, tb.Columns - 1);
        _caretPosition = new TextPointer(tb.Cells[0][nc], 0);
        CollapseSelectionToCaret();
        InvalidateVisual();
    }

    private void ClearFormatting()
    {
        ApplyStyleToSelection(r =>
        {
            r.FontWeight = FontWeight.Normal;
            r.FontStyle = FontStyle.Normal;
            r.FontSize = 14;
            r.Foreground = Brushes.Black;
            r.TextDecorations = null;
            r.NavigateUri = null;
        });
    }

    // Applies (or clears, when url is null) a hyperlink. Uses the selection if there is one;
    // otherwise falls back to the single run that was right-clicked.
    private void SetHyperlink(string? url, Run? targetRun)
    {
        if (Document == null) return;
        _undoManager.PushState(Document, _caretPosition);
        if (_selectionStart.CompareTo(_selectionEnd) != 0)
        {
            var range = new TextRange(_selectionStart, _selectionEnd);
            range.ApplyPropertyValue(r => r.NavigateUri = url);
        }
        else if (targetRun != null)
        {
            targetRun.NavigateUri = url;
        }
        InvalidateVisual();
    }

    private async Task EditHyperlinkAsync(string? current, Run? targetRun)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        string? url = await InputDialog.ShowAsync(owner, "하이퍼링크", current ?? "https://");
        if (string.IsNullOrWhiteSpace(url)) return;
        SetHyperlink(url, targetRun);
    }

    private void ResetImageSize(ImageBlock img)
    {
        if (Document == null || img.Image == null) return;
        _undoManager.PushState(Document, _caretPosition);
        img.Width = img.Image.Size.Width;
        img.Height = img.Image.Size.Height;
        InvalidateVisual();
    }

    private async Task ReplaceImageAsync(ImageBlock img)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "이미지 선택",
            AllowMultiple = false,
            FileTypeFilter = new[] { Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll }
        });
        if (files.Count == 0) return;
        try
        {
            await using var s = await files[0].OpenReadAsync();
            var bmp = new Avalonia.Media.Imaging.Bitmap(s);
            if (Document != null) _undoManager.PushState(Document, _caretPosition);
            img.Image = bmp;
            InvalidateVisual();
        }
        catch { }
    }

    private async Task SaveImageAsync(ImageBlock img)
    {
        if (img.Image == null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "이미지 저장",
            DefaultExtension = "png",
            SuggestedFileName = "image.png"
        });
        if (file == null) return;
        try
        {
            await using var s = await file.OpenWriteAsync();
            img.Image.Save(s);
        }
        catch { }
    }

    public void Undo() => DoUndo();
    public void Redo() => DoRedo();

    public async Task InsertImageFromFileAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "이미지 선택",
            AllowMultiple = false,
            FileTypeFilter = new[] { Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll }
        });
        if (files.Count == 0) return;
        try
        {
            await using var s = await files[0].OpenReadAsync();
            var bmp = new Avalonia.Media.Imaging.Bitmap(s);
            InsertImage(bmp);
        }
        catch { }
    }

    // Total rendered height of the document at the given content width (mirrors the render advancement).
    // Reported via MeasureOverride so the hosting ScrollViewer grows its scrollable extent with content.
    private double MeasureContentHeight(double width)
    {
        if (Document == null) return 0;
        double yOffset = 0;
        foreach (var block in Document.Blocks)
        {
            if (block is TableBlock tb) yOffset += LayoutTable(tb, 10 + tb.Indent, yOffset).TotalHeight + 10;
            else if (block is ImageBlock img) yOffset += (img.Height > 0 ? img.Height : 200) + 10;
            else if (block is DividerBlock) yOffset += DividerHeight;
            else if (block is Paragraph p)
            {
                if (BuildPlain(p) == "")
                    yOffset += p.MarginBottom + (!double.IsNaN(p.LineHeight) ? p.LineHeight : 20);
                else
                    yOffset += BuildTextLayout(p, Math.Max(10, width - 20 - ParaLeft(p))).Height + p.MarginBottom;
            }
        }
        return yOffset + 40; // a little breathing room at the bottom
    }

    private double _measuredHeight;

    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        double w = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0 ? 698 : availableSize.Width;
        _measuredHeight = Math.Max(MinHeight, MeasureContentHeight(w));
        return new Size(w, _measuredHeight);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(0, 0, Bounds.Width, 2000));
        if (Document == null) return;

        // Recomputed every render so resize handles track the current layout.
        _columnBoundaries.Clear();
        _rowBoundaries.Clear();
        _imageHandles.Clear();

        TextPointer? selStart = null, selEnd = null;
        HashSet<Paragraph>? selectedParagraphs = null;
        if (_selectionStart.Paragraph != null && _selectionEnd.Paragraph != null && _selectionStart.CompareTo(_selectionEnd) != 0)
        {
            if (_selectionStart.CompareTo(_selectionEnd) < 0) { selStart = _selectionStart; selEnd = _selectionEnd; }
            else { selStart = _selectionEnd; selEnd = _selectionStart; }
            var allParas = GetAllParagraphsInOrder();
            int si = allParas.IndexOf(selStart.Paragraph);
            int ei = allParas.IndexOf(selEnd.Paragraph);
            if (si >= 0 && ei >= 0)
            {
                selectedParagraphs = new HashSet<Paragraph>();
                for (int idx = si; idx <= ei; idx++)
                    selectedParagraphs.Add(allParas[idx]);
            }
        }

        double yOffset = 0;
        double maxWidth = Bounds.Width;
        double listIndent = 10;
        Point? caretPoint = null;
        Rect? blockCaretRect = null; // when a block caret is active, the image/table it sits in front of
        int orderedIndex = 0; // running counter for consecutive ordered-list paragraphs

        foreach (var block in Document.Blocks)
        {
            if (block is TableBlock tb)
            {
                orderedIndex = 0;
                double startX = 10 + tb.Indent;
                double tableTop = yOffset;
                var tl = LayoutTable(tb, startX, tableTop);
                if (ReferenceEquals(tb, _caretBlock))
                    blockCaretRect = new Rect(startX, tableTop, tl.TableWidth, tl.TotalHeight);
                // When the drag spans multiple cells of this table, highlight the rectangular block fully
                // (Excel/Word style) instead of the linear text run; otherwise fall back to text highlight.
                var cellBlock = SelectedCellRange(tb);

                foreach (var (r, c, rect) in tl.AnchorRects)
                {
                    var cell = tb.Cells[r][c];
                    double cellWidth = rect.Width;

                    if (cell.Background != null)
                        context.FillRectangle(cell.Background, rect);
                    context.DrawRectangle(null, new Pen(Brushes.Gray, 1), rect);

                    bool cellHasPreedit = _caretPosition != null && _caretPosition.Paragraph == cell && !string.IsNullOrEmpty(_preeditText);
                    var layout = cellHasPreedit
                        ? BuildTextLayout(cell, Math.Max(10, cellWidth - 10), _caretPosition!.Offset, _preeditText)
                        : BuildTextLayout(cell, Math.Max(10, cellWidth - 10));

                    // A cell is in "cell-selection mode" when it's part of a multi-cell drag block, or its
                    // whole content is selected (Tab focus / triple-click). Such cells show a fill and NO
                    // caret. A bare caret (collapsed selection) means "editing text" and shows no fill —
                    // so the caret's presence vs. the fill cleanly distinguishes the two modes.
                    bool inBlock = cellBlock is { } cb && r >= cb.r0 && r <= cb.r1 && c >= cb.c0 && c <= cb.c1;
                    bool fullCell = false;
                    if (inBlock)
                    {
                        context.FillRectangle(new SolidColorBrush(Color.FromArgb(80, 0, 120, 215)), rect);
                    }
                    else if (cellBlock == null && selectedParagraphs?.Contains(cell) == true)
                    {
                        int cellLen = GetParagraphLength(cell);
                        int hlStart = (cell == selStart!.Paragraph) ? selStart.Offset : 0;
                        int hlEnd = (cell == selEnd!.Paragraph) ? selEnd.Offset : cellLen;
                        fullCell = hlStart <= 0 && hlEnd >= cellLen;
                        if (fullCell)
                        {
                            // Fill the whole cell so fully-selected (incl. empty) cells are visibly selected.
                            var cellBrush = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215));
                            context.FillRectangle(cellBrush, rect);
                        }
                        else if (hlEnd > hlStart)
                        {
                            DrawSelectionHighlight(context, layout, hlStart, hlEnd, rect.X + 5, rect.Y + 5);
                        }
                    }

                    bool cellSelected = inBlock || fullCell;
                    if (_caretPosition != null && _caretPosition.Paragraph == cell && (!cellSelected || cellHasPreedit))
                    {
                        int caretDisp = _caretPosition.Offset + (cellHasPreedit ? _preeditText!.Length : 0);
                        var cr = layout.HitTestTextPosition(caretDisp);
                        caretPoint = new Point(rect.X + 5 + cr.X, rect.Y + 5 + cr.Y);
                        _lastCaretPoint = caretPoint.Value;
                    }

                    layout.Draw(context, new Point(rect.X + 5, rect.Y + 5));
                }

                // Resize handles live on the physical grid lines (independent of merges). Internal column
                // edges redistribute width with the next column; the outer-right edge grows the last column.
                for (int r = 0; r < tb.Rows; r++)
                    _rowBoundaries.Add((new Rect(startX, tl.RowY[r + 1] - 3, tl.TableWidth, 6), tb, r, tl.RowY[r + 1] - tl.RowY[r]));
                for (int c = 0; c < tb.Columns; c++)
                    _columnBoundaries.Add((new Rect(tl.ColX[c + 1] - 3, tableTop, 6, tl.TotalHeight), tb, c));

                yOffset = tableTop + tl.TotalHeight;
                if (ReferenceEquals(tb, _selectedBlock))
                {
                    var tableRect = new Rect(startX, tableTop, tl.TableWidth, tl.TotalHeight);
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(50, 0, 120, 215)), tableRect);
                    context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)), 2), tableRect);
                }
                yOffset += 10;
            }
            else if (block is Paragraph paragraph)
            {
                string fullText = BuildPlain(paragraph);

                bool hasPreedit = _caretPosition != null && _caretPosition.Paragraph == paragraph && !string.IsNullOrEmpty(_preeditText);

                double px = ParaLeft(paragraph);

                // Ordered numbering runs continuously across consecutive ordered paragraphs; reset otherwise.
                if (paragraph.ListType != ListKind.Ordered) orderedIndex = 0;

                if (fullText == "" && !hasPreedit)
                {
                    if (paragraph.ListType != ListKind.None)
                        DrawListMarker(context, paragraph, paragraph.ListType == ListKind.Ordered ? ++orderedIndex : 0, px, yOffset);
                    if (_caretPosition != null && _caretPosition.Paragraph == paragraph)
                    {
                        caretPoint = new Point(px, yOffset);
                        _lastCaretPoint = caretPoint.Value;
                    }
                    yOffset += paragraph.MarginBottom + (!double.IsNaN(paragraph.LineHeight) ? paragraph.LineHeight : 20);
                    continue;
                }

                double pWidth = Math.Max(10, maxWidth - 20 - px);
                var layout = hasPreedit
                    ? BuildTextLayout(paragraph, pWidth, _caretPosition!.Offset, _preeditText)
                    : BuildTextLayout(paragraph, pWidth);

                // One marker per hard line (\n): each line of a list paragraph is an item. Ordered lists
                // number each line; this is what makes "press Enter -> next bullet/number" work given the
                // editor's "Enter inserts \n in a Run" model (lines aren't separate paragraphs).
                if (paragraph.ListType != ListKind.None)
                {
                    int segStart = 0;
                    for (int i = 0; i <= fullText.Length; i++)
                    {
                        if (i == fullText.Length || fullText[i] == '\n')
                        {
                            var lcr = layout.HitTestTextPosition(Math.Min(segStart, fullText.Length));
                            DrawListMarker(context, paragraph, paragraph.ListType == ListKind.Ordered ? ++orderedIndex : 0, px, yOffset + lcr.Y);
                            segStart = i + 1;
                        }
                    }
                }

                if (paragraph.Background != null)
                    context.FillRectangle(paragraph.Background, new Rect(px, yOffset, pWidth, layout.Height));

                if (paragraph.IsQuote)
                    context.FillRectangle(Brushes.Silver, new Rect(Math.Max(0, px - 10), yOffset, 3, layout.Height));

                if (selectedParagraphs?.Contains(paragraph) == true)
                {
                    int hlStart = (paragraph == selStart!.Paragraph) ? selStart.Offset : 0;
                    int hlEnd = (paragraph == selEnd!.Paragraph) ? selEnd.Offset : fullText.Length;
                    if (hlEnd > hlStart)
                        DrawSelectionHighlight(context, layout, hlStart, hlEnd, px, yOffset);
                }

                if (_caretPosition != null && _caretPosition.Paragraph == paragraph)
                {
                    int caretDisp = _caretPosition.Offset + (hasPreedit ? _preeditText!.Length : 0);
                    var cr = layout.HitTestTextPosition(caretDisp);
                    caretPoint = new Point(px + cr.X, yOffset + cr.Y);
                    _lastCaretPoint = caretPoint.Value;
                }

                layout.Draw(context, new Point(px, yOffset));
                yOffset += layout.Height + paragraph.MarginBottom;
            }
            else if (block is ImageBlock img)
            {
                orderedIndex = 0;
                if (img.Image != null)
                {
                    double width = img.Width > 0 ? img.Width : 200;
                    double height = img.Height > 0 ? img.Height : 200;
                    double imgX = listIndent + img.Indent;
                    var imgRect = new Rect(imgX, yOffset, width, height);
                    context.DrawImage(img.Image, imgRect);
                    if (ReferenceEquals(img, _caretBlock)) blockCaretRect = imgRect;

                    bool imgSelected = ReferenceEquals(img, _selectedBlock);
                    if (imgSelected)
                    {
                        // Selection: translucent overlay + bold border.
                        context.FillRectangle(new SolidColorBrush(Color.FromArgb(60, 0, 120, 215)), imgRect);
                        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)), 2), imgRect);
                    }
                    // Thin border + bottom-right resize handle.
                    context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 120, 215)), 1), imgRect);
                    var handle = new Rect(imgX + width - 6, yOffset + height - 6, 12, 12);
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(230, 0, 120, 215)), handle);
                    // Slightly larger hit area than the visual handle for easier grabbing.
                    _imageHandles.Add((new Rect(imgX + width - 9, yOffset + height - 9, 18, 18), img));

                    yOffset += height + 10;
                }
            }
            else if (block is DividerBlock)
            {
                orderedIndex = 0;
                double y = yOffset + DividerHeight / 2;
                context.DrawLine(new Pen(Brushes.Gray, 1), new Point(listIndent, y), new Point(Math.Max(listIndent + 1, maxWidth - 10), y));
                yOffset += DividerHeight;
            }
        }

        if (blockCaretRect.HasValue)
        {
            // Blinking bar at the block's left edge: top = caret before the block, bottom = caret after.
            if (_isCaretVisible && IsFocused && !IsReadOnly)
            {
                var r = blockCaretRect.Value;
                double cx = r.X - 3;
                double cy1 = _caretBlockAfter ? Math.Max(r.Y, r.Bottom - 20) : r.Y;
                double cy2 = _caretBlockAfter ? r.Bottom : Math.Min(r.Bottom, r.Y + 20);
                context.DrawLine(new Pen(Brushes.Black, 2), new Point(cx, cy1), new Point(cx, cy2));
            }
        }
        else if (caretPoint.HasValue)
        {
            _lastCaretPoint = caretPoint.Value;
            if (_isCaretVisible && IsFocused && !IsReadOnly)
            {
                context.DrawLine(new Pen(Brushes.Black, 1.5), caretPoint.Value, new Point(caretPoint.Value.X, caretPoint.Value.Y + 20));
            }
        }

        // After the caret position is known, scroll it into view if it moved off-screen. Posted (not
        // called inline) so it runs after this layout/render pass rather than re-entering it.
        if (_bringCaretIntoView)
        {
            _bringCaretIntoView = false;
            // Include some margin above/below the caret so scrolling leaves it comfortably inside the
            // viewport rather than flush against (or just past) an edge.
            const double m = 40;
            Rect target = blockCaretRect is { } br
                ? new Rect(br.X, Math.Max(0, br.Y - m), 2, br.Height + 2 * m)
                : new Rect(_lastCaretPoint.X, Math.Max(0, _lastCaretPoint.Y - m), 2, 20 + 2 * m);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { try { this.BringIntoView(target); } catch { } });
        }
    }
}

