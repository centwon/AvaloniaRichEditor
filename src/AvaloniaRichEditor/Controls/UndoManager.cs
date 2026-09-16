using System.Collections.Generic;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

internal struct UndoState
{
    public FlowDocument Document { get; }
    public int CaretGlobalIndex { get; }
    public int CaretOffset { get; }
    /// <summary>Approximate retained size of this snapshot's structure: its element count times a measured
    /// per-element cost (the text is shared with the live document, so it is not charged). Pictures are
    /// charged separately when trimming — see <see cref="Pictures"/>. Used to bound total undo memory.</summary>
    public int ApproxBytes { get; }
    /// <summary>The snapshot's picture elements (<see cref="ImageBlock"/>/<see cref="InlineImage"/>). Their
    /// bytes and decoded bitmaps are shared with the live document while the picture is in it; once an edit
    /// removes it, the history is what keeps them alive.</summary>
    public TextElement[] Pictures { get; }

    public UndoState(FlowDocument document, int caretGlobalIndex, int caretOffset, int approxBytes, TextElement[] pictures)
    {
        Document = document;
        CaretGlobalIndex = caretGlobalIndex;
        CaretOffset = caretOffset;
        ApproxBytes = approxBytes;
        Pictures = pictures;
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

    // History is bounded by BOTH a step count and a memory budget. Every step is a full deep clone, so
    // a count alone bounds nothing that matters: measured here, a 20000-paragraph document retained
    // 12.1 MB per checkpoint, and fifty of those is 592 MB of history behind an editor showing one
    // document. Large documents are trimmed sooner, while always keeping at least MinSteps so undo
    // stays useful.
    //
    // The budget counts ELEMENTS, not characters. Clone shares the strings, so the text costs a
    // snapshot nothing: the same document at 2 and at 60 characters per paragraph retained byte-for-
    // byte the same. What it costs is the object graph, and that came out at a stable ~310 bytes per
    // block/inline across every shape measured (274 when inlines dominate). UndoBudgetProbeTests is
    // that measurement; re-run it if the model classes or the runtime change, because the constant is
    // an observation rather than a rule. (The WinUI peer measures ~155 on its own model — same law,
    // different platform, and it had the same character-counting mistake until this was measured.)
    private const int MaxStackSize = 50;
    private const long DefaultMaxBytes = 64L * 1024 * 1024;
    private const int MinSteps = 3;
    private const int BytesPerElement = 310;

    private readonly long _maxBytes;

    /// <summary>Creates a history bounded by the default memory budget.</summary>
    public UndoManager() : this(DefaultMaxBytes) { }

    /// <summary>Creates a history with an explicit byte budget. The parameter exists for tests: the real
    /// budget needs a document of roughly 200,000 elements to fill, which is not something to build in
    /// a unit test — without the seam the trimming policy could not be tested at all.</summary>
    public UndoManager(long maxBytes) => _maxBytes = maxBytes > 0 ? maxBytes : DefaultMaxBytes;

    // Blocks and inlines at any depth, and the picture elements among them — one walk for both, since every
    // checkpoint pays it. A cell counts as an element itself, and an inline table's cells
    // hold real content that is deep-cloned with the snapshot — charging a flat placeholder for one
    // would make a document whose content lives in inline tables look tiny, which is the exact case the
    // budget exists for.
    internal static (int elements, TextElement[] pictures) Scan(FlowDocument doc)
    {
        int n = 0;
        List<TextElement>? pictures = null;
        void Walk(IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
            {
                n++;
                if (b is Paragraph p)
                {
                    n += p.Inlines.Count;
                    foreach (var inl in p.Inlines)
                        if (inl is InlineImage ii) (pictures ??= new()).Add(ii);
                        else if (inl is InlineTable it)
                            foreach (var row in it.Table.Cells)
                                foreach (var cell in row)
                                { n++; Walk(cell.Blocks); }
                }
                else if (b is ImageBlock ib) (pictures ??= new()).Add(ib);
                else if (b is TableBlock tb)
                {
                    foreach (var row in tb.Cells)
                        foreach (var cell in row)
                        { n++; Walk(cell.Blocks); }
                }
            }
        }
        Walk(doc.Blocks);
        return (n, pictures?.ToArray() ?? []);
    }

    internal static int ElementCount(FlowDocument doc) => Scan(doc).elements;

    internal static int EstimateBytes(FlowDocument doc) // internal: covered directly by the test suite
        => (int)System.Math.Min((long)ElementCount(doc) * BytesPerElement, int.MaxValue);

    private static UndoState Snapshot(FlowDocument doc, int caretGlobal, int caretOffset)
    {
        var clone = doc.Clone();
        var (elements, pictures) = Scan(clone);
        return new UndoState(clone, caretGlobal, caretOffset,
            (int)System.Math.Min((long)elements * BytesPerElement, int.MaxValue), pictures);
    }

    // What a picture holds: its encoded bytes, and its decoded bitmap once one exists (read without
    // decoding). Each is its own object with its own lifetime, so each is tracked on its own.
    private static (byte[]? bytes, Bitmap? bitmap) Parts(TextElement picture) => picture switch
    {
        ImageBlock ib => (ib.RawBytes, ib.CachedBitmap),
        InlineImage ii => (ii.RawBytes, ii.CachedBitmap),
        _ => (null, null),
    };

    // Keeps the newest states within both budgets (but never fewer than MinSteps).
    //
    // A state is charged its elements plus the picture memory that ONLY the history keeps alive: encoded
    // bytes and decoded bitmaps the live document doesn't reference, each charged once — to the newest state
    // holding it, since dropping older states can't free what a newer one still points at. Pictures used to
    // be excluded outright, on the grounds that Clone shares them with the live document. That holds only
    // while the picture is IN the document: once an edit removes it, the snapshots are its sole owners, and
    // here a snapshot shares the DECODED bitmap too (4 bytes a pixel — 46 MB for a 12 MP photo). The WinUI
    // peer measured the bytes alone: 36 photos inserted and deleted held 133 MB behind a 64 MB budget.
    // Objects are compared by reference, which can only overcharge (equal pictures loaded separately),
    // never let memory through uncounted.
    private void Trim(Stack<UndoState> stack, TextElement[] livePictures)
    {
        if (stack.Count <= MinSteps) return;
        var live = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var pic in livePictures)
        {
            var (b, bmp) = Parts(pic);
            if (b != null) live.Add(b);
            if (bmp != null) live.Add(bmp);
        }
        var charged = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var arr = stack.ToArray(); // index 0 = newest (top)
        int keep = 0;
        long bytes = 0;
        for (int i = 0; i < arr.Length; i++)
        {
            bytes += arr[i].ApproxBytes;
            foreach (var pic in arr[i].Pictures)
            {
                var (b, bmp) = Parts(pic);
                if (b != null && !live.Contains(b) && charged.Add(b)) bytes += b.Length;
                if (bmp != null && !live.Contains(bmp) && charged.Add(bmp))
                    bytes += (long)bmp.PixelSize.Width * bmp.PixelSize.Height * 4;
            }
            bool withinBudget = keep < MaxStackSize && (bytes <= _maxBytes || keep < MinSteps);
            if (!withinBudget) break;
            keep++;
        }
        if (keep >= arr.Length) return; // nothing to drop
        stack.Clear();
        for (int i = keep - 1; i >= 0; i--) stack.Push(arr[i]); // re-push oldest-kept first
    }

    public void PushState(FlowDocument currentDoc, TextPointer currentCaret)
    {
        // A null caret paragraph is not a reason to skip the checkpoint: nothing places the caret until
        // the first click, so a drag-resize right after a load pushed no state and could not be undone.
        // GetGlobalIndex already answers 0 for it, and Undo() never guarded on it either.
        if (currentDoc == null || currentCaret == null) return;

        int caretGlobal = GetGlobalIndex(currentDoc, currentCaret);
        var state = Snapshot(currentDoc, caretGlobal, currentCaret.Offset);

        _undoStack.Push(state);
        // The snapshot was just cloned from the live document, so its pictures ARE the live ones. A picture
        // the coming edit removes is charged from the next checkpoint on — one step late, never missed.
        Trim(_undoStack, state.Pictures);
        _redoStack.Clear();
    }

    public UndoState? Undo(FlowDocument currentDoc, TextPointer currentCaret)
    {
        if (_undoStack.Count == 0) return null;

        int caretGlobal = GetGlobalIndex(currentDoc, currentCaret);
        _redoStack.Push(Snapshot(currentDoc, caretGlobal, currentCaret.Offset));
        var restored = _undoStack.Pop();
        // The restored snapshot is the live document from here on. Only the stack that grew is trimmed: the
        // other one just shrank, and it is trimmed again, against the live document then, whenever it grows.
        Trim(_redoStack, restored.Pictures);
        return restored;
    }

    public UndoState? Redo(FlowDocument currentDoc, TextPointer currentCaret)
    {
        if (_redoStack.Count == 0) return null;

        int caretGlobal = GetGlobalIndex(currentDoc, currentCaret);
        _undoStack.Push(Snapshot(currentDoc, caretGlobal, currentCaret.Offset));
        var restored = _redoStack.Pop();
        Trim(_undoStack, restored.Pictures);
        return restored;
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
