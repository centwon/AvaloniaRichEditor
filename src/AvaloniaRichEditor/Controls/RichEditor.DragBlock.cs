using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Drag & drop of a whole object within the editor (from the WinUI port, 2026-09-15): a table grabbed by its
// left/top border band — where the move cursor already promised a move and a press only selected — a block
// image, a picture in a cell, an inline image, a nested or an inline table. The press does what it always did
// (selects the object, or places the table's block caret) and arms; past the drag slop a grey drop caret
// follows the pointer; the release moves the object there, or copies it when Ctrl is down at the drop (a "+"
// beside the caret says so beforehand). A press that never becomes a drag changes nothing. One undo step.
public partial class RichEditor
{
    private const double DragSlop = 6;  // the port's MultiClickSlop, same Manhattan measure
    private static Cursor NoDropCursor => Cur(StandardCursorType.No);

    private object? _dragObject;        // ImageBlock / TableBlock / InlineImage / InlineTable armed by the press
    private bool _dragObjectActive;     // past the slop: the drop preview is live
    private bool _dragCopy;             // Ctrl as last seen (pointer modifiers, Ctrl down/up) — draws the "+"
    private Point _dragObjectStart;     // press point, document space
    private Point _dragObjectLast;      // last pointer point — re-evaluated when Ctrl changes mid-drag
    private TextPointer? _dropPreview;  // where the release would drop (drawn by DrawDropPreview)
    private Pen? _dropCaretPen;

    // A press that has just selected an object: arm its drag and take the pointer.
    private void ArmObjectDrag(object? obj, Point docPt, PointerPressedEventArgs e)
    {
        if (ArmObjectDragAt(obj, docPt)) e.Pointer.Capture(this);
    }

    // The press minus its pointer capture. Editing only: a viewer's press selects the object for Copy.
    internal bool ArmObjectDragAt(object? obj, Point docPt)
    {
        if (IsReadOnly || Document == null || obj is not (ImageBlock or TableBlock or InlineImage or InlineTable)) return false;
        _dragObject = obj;
        _dragObjectActive = false;
        _dragObjectStart = _dragObjectLast = docPt;
        _dropPreview = null;
        return true;
    }

    // Pointer move while armed: past the slop the drag goes live and the drop caret follows the pointer. A
    // point the object may not go to (a move into its own cells) shows no caret and the no-drop cursor.
    internal void DragObjectMoved(Point docPt, bool copy)
    {
        if (_dragObject == null) return;
        _dragObjectLast = docPt;
        _dragCopy = copy;
        if (!_dragObjectActive)
        {
            if (Math.Abs(docPt.X - _dragObjectStart.X) + Math.Abs(docPt.Y - _dragObjectStart.Y) < DragSlop) return;
            _dragObjectActive = true;
        }
        TextPointer tp;
        _trustLayoutCache = true; // hit-testing only — never mutates, as on the drag-select path
        try { tp = GetPositionFromPoint(docPt); }
        finally { _trustLayoutCache = false; }
        _dropPreview = CanDropObject(_dragObject, tp, copy) ? tp : null;
        Cursor = _dropPreview != null ? ArrowCursor : NoDropCursor;
        InvalidateVisual();
    }

    // The release minus its pointer capture. Ctrl is read at the DROP. Returns whether the document changed;
    // an unmoved press changes nothing (the object keeps what the press gave it).
    internal bool FinishObjectDrag(bool copy)
    {
        var obj = _dragObject;
        bool wasDrag = _dragObjectActive;
        var drop = _dropPreview;
        CancelObjectDrag();
        if (!wasDrag || obj == null || drop == null) return false;
        return DropObject(obj, drop, copy);
    }

    // Ends an object drag WITHOUT dropping: a lost capture is not a drop, and after a document swap (load,
    // undo) the object belongs to the document that was replaced.
    private void CancelObjectDrag()
    {
        if (_dragObject == null) return;
        bool wasActive = _dragObjectActive;
        _dragObject = null;
        _dragObjectActive = false;
        _dropPreview = null;
        if (wasActive) { Cursor = IbeamCursor; InvalidateVisual(); }
    }

    // Ctrl pressed or let go mid-drag: the "+" follows it, and so does whether the point is a valid drop at
    // all (a copy may go into its own cells, a move may not).
    private void OnDragModifierChanged(bool copy)
    {
        if (_dragObjectActive) DragObjectMoved(_dragObjectLast, copy);
    }

    private void OnDragKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl && _dragObjectActive) OnDragModifierChanged(false);
    }

    /// <summary>Whether <paramref name="obj"/> may be dropped at <paramref name="at"/>. A MOVE may not land
    /// inside itself — a table dropped into one of its own cells, at any depth, would have to become its own
    /// descendant, and the walks over the tree would never end. A COPY may: what lands there is a clone.</summary>
    internal static bool CanDropObject(object obj, TextPointer at, bool copy)
    {
        if (at.Paragraph is not { } p) return false;
        if (copy) return true;
        var table = obj switch { TableBlock tb => tb, InlineTable it => it.Table, _ => null };
        if (table == null) return true;
        for (object? n = p.Parent; n is TextElement te; n = te.Parent)
            if (ReferenceEquals(te, table)) return false;
        return true;
    }

    // Moves (or copies) obj to `at`. Returns whether the document changed: a drop where the object already
    // is, or one CanDropObject refuses, is no edit at all — no undo step, not "modified".
    internal bool DropObject(object obj, TextPointer at, bool copy)
    {
        if (Document == null || at.Paragraph is not { } p || !CanDropObject(obj, at, copy)) return false;
        return obj switch
        {
            ImageBlock or TableBlock => DropBlock((Block)obj, p, at.Offset, copy),
            InlineImage or InlineTable => DropInline((Inline)obj, p, at.Offset, copy),
            _ => false,
        };
    }

    private static IList<Block>? BlockListOf(TextElement e) => e.Parent switch
    {
        FlowDocument d => d.Blocks,
        TableCell tc => tc.Blocks,
        _ => null,
    };

    // A block goes BEFORE the drop paragraph when dropped at its start, AFTER it when dropped at its end, and
    // splits it only in between. Always splitting would leave an empty head paragraph at every start-of-line
    // drop — dragging a table down and back would grow the document by a blank line per trip.
    private bool DropBlock(Block blk, Paragraph p, int offset, bool copy)
    {
        var source = BlockListOf(blk);
        var target = BlockListOf(p);
        if (source == null || target == null || !source.Contains(blk) || !target.Contains(p)) return false;
        int len = GetParagraphLength(p);
        int off = Math.Clamp(offset, 0, len);
        if (!copy && ReferenceEquals(source, target))
        {
            int bi = source.IndexOf(blk), pi = target.IndexOf(p);
            if ((off == 0 && pi == bi + 1) || (off == len && pi == bi - 1)) return false; // already there
        }

        PushUndo();
        var moving = copy ? (Block)blk.Clone() : blk;
        if (!copy) source.Remove(blk);
        int at = target.IndexOf(p);
        if (off > 0 && off < len)
        {
            _caretPosition = new TextPointer(p, off);
            SplitParagraphAtCaret(); // head stays p, its tail right after it
            // The tail continues p: Enter's "a heading's next line is body text" is a typing rule.
            if (target[at + 1] is Paragraph tail) tail.HeadingLevel = p.HeadingLevel;
            at++;
        }
        else if (off == len) at++;
        moving.Parent = p.Parent;
        target.Insert(at, moving);
        UpdateParents(Document!); // re-normalizes the list the block left, too
        SelectDropped(moving);
        return true;
    }

    // An inline object is one character (rule #2). Moved later within its own paragraph, the drop offset
    // shifts back by one once the object is taken out ahead of it.
    private bool DropInline(Inline obj, Paragraph p, int offset, bool copy)
    {
        if (obj.Parent is not Paragraph host || host.Inlines.IndexOf(obj) is not (int idx and >= 0)) return false;
        int from = 0;
        for (int i = 0; i < idx; i++) from += InlineLen(host.Inlines[i]);
        int off = Math.Clamp(offset, 0, GetParagraphLength(p));
        if (!copy && ReferenceEquals(host, p) && (off == from || off == from + 1)) return false; // already there

        PushUndo();
        var moving = copy ? (Inline)obj.Clone() : obj;
        if (!copy)
        {
            host.Inlines.RemoveAt(idx);
            if (host.Inlines.Count == 0) host.Inlines.Add(new Run { Text = "", Parent = host });
            if (ReferenceEquals(host, p) && off > from) off--;
        }
        int at = SplitInlinesAt(p, off);
        moving.Parent = p;
        p.Inlines.Insert(at, moving);
        TextRange.CoalesceRuns(p);
        if (!ReferenceEquals(host, p)) TextRange.CoalesceRuns(host);
        UpdateParents(Document!);
        SelectDropped(moving);
        return true;
    }

    // The dropped object ends up as a click on it would leave it: a picture selected, a top-level table under
    // the block caret, a nested or inline table selected whole. The caret goes just after it.
    private void SelectDropped(object obj)
    {
        _selectedBlock = null;
        _selectedInline = null;
        _caretBlock = null;
        _caretBlockAfter = false;
        if (obj is Block b && BlockListOf(b) is { } list && list.IndexOf(b) is int bi and >= 0
            && bi + 1 < list.Count && list[bi + 1] is Paragraph next) // NormalizeBlocks: one always follows
            _caretPosition = new TextPointer(next, 0);
        else if (obj is Inline inl && inl.Parent is Paragraph host)
        {
            int off = 0;
            foreach (var i in host.Inlines) { off += InlineLen(i); if (ReferenceEquals(i, inl)) break; }
            _caretPosition = new TextPointer(host, off);
        }
        CollapseSelectionToCaret();
        switch (obj)
        {
            case ImageBlock ib: _selectedBlock = ib; break;
            case TableBlock tb when tb.Parent is FlowDocument: _caretBlock = tb; break;
            case TableBlock tb: SelectWholeTableForCopy(tb); break;
            case InlineTable it: SelectWholeTableForCopy(it.Table); break;
            case InlineImage ii when ii.Parent is Paragraph ip: _selectedInline = (ip, ii); break;
        }
        InvalidateMeasure();
        ResetCaretBlink();
        InvalidateVisual();
    }

    // Grey caret at the pending drop position, with a "+" while Ctrl makes it a copy. Drawn in the paragraph
    // walks (top level and cells), where the paragraph's layout and origin are at hand.
    private void DrawDropPreview(DrawingContext context, Paragraph p, Avalonia.Media.TextFormatting.TextLayout layout,
        double px, double py)
    {
        if (!_dragObjectActive || _dropPreview is not { } dp || !ReferenceEquals(dp.Paragraph, p)) return;
        int off = Math.Clamp(dp.Offset, 0, GetParagraphLength(p));
        var cr = CaretRectIn(layout, p, off, off, dp.AtLineEnd);
        var pen = _dropCaretPen ??= new Pen(Brushes.Gray, 2);
        double x = px + cr.X, top = py + cr.Y, h = cr.Height > 0 ? cr.Height : 16;
        context.DrawLine(pen, new Point(x, top), new Point(x, top + h));
        if (_dragCopy)
        {
            context.DrawLine(pen, new Point(x + 4, top + 4), new Point(x + 12, top + 4));
            context.DrawLine(pen, new Point(x + 8, top), new Point(x + 8, top + 8));
        }
    }
}
