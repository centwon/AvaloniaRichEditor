using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Drag & drop of selected text within the editor: pressing inside an existing text selection arms a text drag
// without collapsing the selection; moving past DragSlop shows the drop caret (and "+" when Ctrl is held);
// releasing moves (or with Ctrl copies) the selected range to the drop position in one undo step and leaves
// the dropped text selected. A click that never moves past DragSlop places the caret at the click point on release.
public partial class RichEditor
{
    private bool _dragTextArmed;
    private bool _dragTextActive;
    private Point _dragTextStart;
    private Point _dragTextLast;

    private (TextPointer Start, TextPointer End)? OrderedTextSelection()
    {
        if (_selectionStart.Paragraph == null || _selectionEnd.Paragraph == null) return null;
        int cmp = _selectionStart.CompareTo(_selectionEnd);
        if (cmp == 0) return null;
        return cmp < 0 ? (_selectionStart, _selectionEnd) : (_selectionEnd, _selectionStart);
    }

    internal bool CanArmTextDragAt(Point docPt, int clickCount, bool shift)
    {
        if (IsReadOnly || Document == null || clickCount != 1 || shift) return false;
        if (CellBlockTable() != null) return false;
        if (OrderedTextSelection() is not { } sel) return false;
        foreach (var ir in _inlineImageRects)
            if (ir.rect.Contains(docPt)) return false;

        TextPointer hit;
        _trustLayoutCache = true;
        try { hit = GetPositionFromPoint(docPt); }
        finally { _trustLayoutCache = false; }

        if (hit.Paragraph == null) return false;
        return sel.Start.CompareTo(hit) < 0 && hit.CompareTo(sel.End) < 0;
    }

    internal bool ArmTextDragAt(Point docPt)
    {
        if (IsReadOnly || Document == null || OrderedTextSelection() == null) return false;
        _dragTextArmed = true;
        _dragTextActive = false;
        _dragTextStart = _dragTextLast = docPt;
        _dropPreview = null;
        return true;
    }

    internal void DragTextMoved(Point docPt, bool copy)
    {
        if (!_dragTextArmed) return;
        _dragTextLast = docPt;
        _dragCopy = copy;
        if (!_dragTextActive)
        {
            if (Math.Abs(docPt.X - _dragTextStart.X) + Math.Abs(docPt.Y - _dragTextStart.Y) < DragSlop) return;
            _dragTextActive = true;
        }
        TextPointer tp;
        _trustLayoutCache = true;
        try { tp = GetPositionFromPoint(docPt); }
        finally { _trustLayoutCache = false; }

        _dropPreview = tp;
        Cursor = ArrowCursor;
        InvalidateVisual();
    }

    internal bool FinishTextDrag(Point docPt, bool copy)
    {
        if (!_dragTextArmed) return false;
        bool wasDrag = _dragTextActive;
        var drop = _dropPreview;
        CancelTextDrag();
        if (!wasDrag)
        {
            _caretPosition = GetPositionFromPoint(docPt);
            CollapseSelectionToCaret();
            ResetCaretBlink();
            InvalidateVisual();
            if (GetLinkRunAtPoint(docPt) is { NavigateUri: { Length: > 0 } link }) OpenUrl(link);
            return false;
        }
        if (drop == null) return false;
        return DropTextSelection(drop, copy);
    }

    private void CancelTextDrag()
    {
        if (!_dragTextArmed) return;
        bool wasActive = _dragTextActive;
        _dragTextArmed = false;
        _dragTextActive = false;
        _dropPreview = null;
        if (wasActive) { Cursor = IbeamCursor; InvalidateVisual(); }
    }

    internal bool CanDropText(TextPointer at, bool copy)
    {
        if (Document == null || at.Paragraph is not { } targetPara) return false;
        if (OrderedTextSelection() is not { } sel) return false;
        // Dropping inside (or on the boundary of) the selection itself is a no-op.
        if (sel.Start.CompareTo(at) <= 0 && at.CompareTo(sel.End) <= 0) return false;
        // A multi-cell selection within a table cannot be dragged as text unless it is inside a single cell.
        if (!ReferenceEquals(sel.Start.Paragraph, sel.End.Paragraph)
            && FindCell(sel.Start.Paragraph!) is { } sc && FindCell(sel.End.Paragraph!) is { } ec
            && !(sc.tb == ec.tb && sc.r == ec.r && sc.c == ec.c))
            return false;
        return true;
    }

    internal bool DropTextSelection(TextPointer at, bool copy)
    {
        if (!CanDropText(at, copy) || OrderedTextSelection() is not { } sel) return false;
        var s = sel.Start;
        var e = sel.End;

        // Single-paragraph selection: move/copy inlines directly within or across paragraphs.
        if (ReferenceEquals(s.Paragraph, e.Paragraph))
        {
            var srcPara = s.Paragraph!;
            var clonedPara = CloneParagraphRange(srcPara, s.Offset, e.Offset);
            var inlines = new List<Inline>();
            foreach (var inl in clonedPara.Inlines)
            {
                if (inl is Run r && string.IsNullOrEmpty(r.Text)) continue;
                if (copy && inl is InlineImage && !AllowImages) continue;
                if (copy && inl is InlineTable && !AllowTables) continue;
                if (!TableNesting.Fits(at.Paragraph, inl)) continue;
                inlines.Add(inl);
            }
            if (inlines.Count == 0) return false;

            var dstPara = at.Paragraph!;
            int dstOff = Math.Clamp(at.Offset, 0, GetParagraphLength(dstPara));
            PushUndo();
            if (!copy)
            {
                int delLen = e.Offset - s.Offset;
                if (ReferenceEquals(srcPara, dstPara) && dstOff > s.Offset)
                    dstOff -= delLen;
                DeleteLocalText(srcPara, s.Offset, delLen);
            }
            int insertAt = SplitInlinesAt(dstPara, dstOff);
            int added = 0;
            foreach (var inl in inlines)
            {
                inl.Parent = dstPara;
                dstPara.Inlines.Insert(insertAt++, inl);
                added += InlineLen(inl);
            }
            TextRange.CoalesceRuns(dstPara);
            if (!ReferenceEquals(srcPara, dstPara)) TextRange.CoalesceRuns(srcPara);
            UpdateParents(Document!);
            _selectionStart = new TextPointer(dstPara, dstOff);
            _selectionEnd = new TextPointer(dstPara, dstOff + added);
            _caretPosition = new TextPointer(dstPara, dstOff + added);
            MarkTextChanged();
            InvalidateMeasure();
            ResetCaretBlink();
            InvalidateVisual();
            NotifyStatus();
            return true;
        }

        // Multi-paragraph selection: capture blocks, resolve the post-delete drop target, and splice.
        var range = new TextRange(s, e);
        var blocks = CaptureBlockStructure(range) ?? BuildSelectionDocument()?.Blocks.ToList();
        if (blocks == null || blocks.Count == 0) return false;

        var targetPara = at.Paragraph!;
        int targetOff = Math.Clamp(at.Offset, 0, GetParagraphLength(targetPara));
        var sp = s.Paragraph!;
        var ep = e.Paragraph!;

        PushUndo();
        if (!copy)
        {
            bool after = at.CompareTo(e) > 0;
            bool joins = !ReferenceEquals(sp, ep) && BlockListOf(sp) is { } list
                && ReferenceEquals(list, BlockListOf(ep));
            range.Delete();
            if (after && ReferenceEquals(targetPara, ep))
            {
                if (joins) { targetPara = sp; targetOff = s.Offset + targetOff - e.Offset; }
                else targetOff -= e.Offset; // across a cell boundary: the last paragraph stays, cleared up to e
            }
            UpdateParents(Document!);
        }

        _caretPosition = new TextPointer(targetPara, Math.Clamp(targetOff, 0, GetParagraphLength(targetPara)));
        CollapseSelectionToCaret();
        InsertBlocksAtCaret(blocks);
        CollapseSelectionToCaret();
        MarkTextChanged();
        InvalidateMeasure();
        ResetCaretBlink();
        InvalidateVisual();
        NotifyStatus();
        return true;
    }
}
