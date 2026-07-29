using System.Collections.Generic;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

internal struct UndoState
{
    public FlowDocument Document { get; }
    public int CaretGlobalIndex { get; }
    public int CaretOffset { get; }

    public UndoState(FlowDocument document, int caretGlobalIndex, int caretOffset)
    {
        Document = document;
        CaretGlobalIndex = caretGlobalIndex;
        CaretOffset = caretOffset;
    }
}

internal class UndoManager
{
    private readonly Stack<UndoState> _undoStack = new();
    private readonly Stack<UndoState> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    // Drops all history (e.g. when switching into ReadOnly mode, where no edits can occur).
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    // Limit stack size to prevent memory leaks in our MVP
    private const int MaxStackSize = 50;

    public void PushState(FlowDocument currentDoc, TextPointer currentCaret)
    {
        if (currentDoc == null || currentCaret == null || currentCaret.Paragraph == null) return;

        int caretGlobal = GetGlobalIndex(currentDoc, currentCaret);
        var clonedDoc = currentDoc.Clone();

        _undoStack.Push(new UndoState(clonedDoc, caretGlobal, currentCaret.Offset));

        if (_undoStack.Count > MaxStackSize)
        {
            var arr = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = MaxStackSize - 1; i >= 0; i--) _undoStack.Push(arr[i]);
        }

        _redoStack.Clear();
    }

    public UndoState? Undo(FlowDocument currentDoc, TextPointer currentCaret)
    {
        if (_undoStack.Count == 0) return null;

        int caretGlobal = GetGlobalIndex(currentDoc, currentCaret);
        _redoStack.Push(new UndoState(currentDoc.Clone(), caretGlobal, currentCaret.Offset));

        return _undoStack.Pop();
    }

    public UndoState? Redo(FlowDocument currentDoc, TextPointer currentCaret)
    {
        if (_redoStack.Count == 0) return null;

        int caretGlobal = GetGlobalIndex(currentDoc, currentCaret);
        _undoStack.Push(new UndoState(currentDoc.Clone(), caretGlobal, currentCaret.Offset));

        return _redoStack.Pop();
    }

    // The caret's index in document-paragraph order, and its inverse. Both walks must number the
    // SAME positions, and must reach every paragraph the rest of the engine does: a cell's 2nd+ block
    // (P3), a nested table's cells (P4-2b) and an inline table's cells (milestone B). The old flat walk
    // stopped at each cell's first paragraph, so a caret anywhere deeper was never numbered — undo then
    // fell back to index 0 and dropped the caret at the start of the document. Mirrors
    // TextPointer.CompareTo / RichEditor.ParagraphsInBlocks (anchor cells only; each table and each
    // non-paragraph block consumes one index of its own).
    public TextPointer GetPointerFromGlobalIndex(FlowDocument doc, int index)
    {
        if (doc.Blocks.Count == 0) return new TextPointer(null, 0);

        int currentIndex = 0;
        Paragraph? lastPara = null;
        Paragraph? hit = null;

        void TraverseBlocks(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                if (hit != null) return;
                if (block is Paragraph p)
                {
                    lastPara = p;
                    if (currentIndex == index) { hit = p; return; }
                    currentIndex++;
                    // An inline table's cells are numbered right after their host paragraph.
                    foreach (var inl in p.Inlines)
                        if (inl is InlineTable it)
                            foreach (var (_, _, cell) in it.Table.LogicalCells())
                            {
                                TraverseBlocks(cell.Blocks);
                                if (hit != null) return;
                            }
                }
                else if (block is TableBlock tb)
                {
                    currentIndex++; // the table itself occupies one index
                    foreach (var (_, _, cell) in tb.LogicalCells())
                    {
                        TraverseBlocks(cell.Blocks);
                        if (hit != null) return;
                    }
                }
                else
                {
                    currentIndex++;
                }
            }
        }

        TraverseBlocks(doc.Blocks);
        // Found: that paragraph. Not found (index past the end): the last one seen, as before.
        return new TextPointer(hit ?? lastPara, 0);
    }

    private int GetGlobalIndex(FlowDocument doc, TextPointer pointer)
    {
        int index = 0;
        bool found = false;

        void TraverseBlocks(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                if (found) return;

                if (block is Paragraph p)
                {
                    if (ReferenceEquals(p, pointer.Paragraph)) { found = true; return; }
                    index++;
                    foreach (var inl in p.Inlines)
                        if (inl is InlineTable it)
                            foreach (var (_, _, cell) in it.Table.LogicalCells())
                            {
                                TraverseBlocks(cell.Blocks);
                                if (found) return;
                            }
                }
                else if (block is TableBlock tb)
                {
                    index++;
                    foreach (var (_, _, cell) in tb.LogicalCells())
                    {
                        TraverseBlocks(cell.Blocks);
                        if (found) return;
                    }
                }
                else
                {
                    index++;
                }
            }
        }

        TraverseBlocks(doc.Blocks);
        return found ? index : 0;
    }
}
