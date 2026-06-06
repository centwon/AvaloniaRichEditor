using System.Collections.Generic;
using AvaloniaRichTextBoxPort.Documents;

namespace AvaloniaRichTextBoxPort.Controls;

public struct UndoState
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

public class UndoManager
{
    private readonly Stack<UndoState> _undoStack = new();
    private readonly Stack<UndoState> _redoStack = new();

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
            for(int i = MaxStackSize - 1; i >= 0; i--) _undoStack.Push(arr[i]);
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

    public TextPointer GetPointerFromGlobalIndex(FlowDocument doc, int index)
    {
        if (doc.Blocks.Count == 0) return new TextPointer(null, 0);

        int currentIndex = 0;
        Paragraph? lastPara = null;

        void TraverseBlocks(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                if (block is Paragraph p)
                {
                    lastPara = p;
                    if (currentIndex == index) return;
                    currentIndex++;
                }
                else if (block is TableBlock tb)
                {
                    currentIndex++;
                    for (int r = 0; r < tb.Rows; r++)
                    {
                        for (int c = 0; c < tb.Columns; c++)
                        {
                            lastPara = tb.Cells[r][c];
                            if (currentIndex == index) return;
                            currentIndex++;
                        }
                    }
                }
                else
                {
                    currentIndex++;
                }
            }
        }

        TraverseBlocks(doc.Blocks);
        return new TextPointer(lastPara, 0);
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
                    if (p == pointer.Paragraph) { found = true; return; }
                    index++;
                }
                else if (block is TableBlock tb)
                {
                    index++;
                    for (int r = 0; r < tb.Rows; r++)
                    {
                        for (int c = 0; c < tb.Columns; c++)
                        {
                            var cell = tb.Cells[r][c];
                            if (cell == pointer.Paragraph) { found = true; return; }
                            index++;
                        }
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
