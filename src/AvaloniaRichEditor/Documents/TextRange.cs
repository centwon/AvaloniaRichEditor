using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Media;

namespace AvaloniaRichEditor.Documents;

/// <summary>A logical range between two <see cref="TextPointer"/> positions in a <see cref="FlowDocument"/>.
/// Supports text extraction, deletion, and property (formatting) application across paragraph boundaries.</summary>
public class TextRange
{
    private TextPointer _start;
    private TextPointer _end;

    /// <summary>The start (earlier) position of the range.</summary>
    public TextPointer Start => _start;
    /// <summary>The end (later) position of the range.</summary>
    public TextPointer End => _end;

    /// <summary>Creates a range from two positions (order does not matter; the earlier becomes <see cref="Start"/>).</summary>
    public TextRange(TextPointer position1, TextPointer position2)
    {
        if (position1.CompareTo(position2) <= 0)
        {
            _start = position1;
            _end = position2;
        }
        else
        {
            _start = position2;
            _end = position1;
        }
    }

    /// <summary><see langword="true"/> when Start and End are at the same position (nothing selected).</summary>
    public bool IsEmpty => _start.CompareTo(_end) == 0;

    // ---- what the range covers ----------------------------------------------------------------------------------
    // Every operation below goes by one rule (PositionOrder.Span): a paragraph's covered part is the offsets between
    // the endpoints in POSITION order, where an inline table's cells sit inside their host at the table's place.
    // Each operation used to walk the paragraph list from the start's to the end's with its own trimming at the two
    // ends; three of those trimmings were fixed one path at a time (round 35, 2026-10-15, the 1.3.0 audit), and a
    // selection from after an inline table back into it was still read inside out — one Shift+Left and Delete
    // emptied every other cell of the table (WinUI port's pre-publish audit, 2026-10-18; measured here too).

    internal readonly record struct CoveredPart(Paragraph Paragraph, int From, int To, bool InsideCoveredObject);

    // The paragraphs the range touches, in document order, each with the part it covers (From < To), plus the start
    // and end paragraphs even where they cover nothing (a copied paragraph break). InsideCoveredObject marks a
    // paragraph in an inline table that a covered part of its host takes whole: that table is one object to delete
    // or copy, not a range of cells.
    internal List<CoveredPart> Covered()
    {
        var result = new List<CoveredPart>();
        var sp = _start.Paragraph;
        var ep = _end.Paragraph;
        if (sp == null || ep == null) return result;
        var doc = GetFlowDocument(sp);
        if (doc == null)
        {
            if (ReferenceEquals(sp, ep)) result.Add(new CoveredPart(sp, _start.Offset, Math.Max(_start.Offset, _end.Offset), false));
            return result;
        }

        var startTop = TopLevelBlockOf(doc, sp);
        var endTop = TopLevelBlockOf(doc, ep);
        int si = startTop != null ? doc.Blocks.IndexOf(startTop) : -1;
        int ei = endTop != null ? doc.Blocks.IndexOf(endTop) : -1;
        if (si < 0 || ei < si) return result;

        // Only the top-level blocks from the start's to the end's can hold a covered paragraph.
        var order = new Dictionary<Paragraph, int>();
        var paras = new List<Paragraph>();
        for (int b = si; b <= ei; b++)
        {
            var inBlock = new List<Paragraph>();
            CollectParagraphs(new[] { doc.Blocks[b] }, inBlock);
            foreach (var p in inBlock) { order[p] = order.Count; paras.Add(p); }
        }
        int Index(Paragraph p) => order.TryGetValue(p, out int i) ? i : -1;

        var spans = new Dictionary<Paragraph, (int From, int To)>();
        foreach (var p in paras)
        {
            var span = PositionOrder.Span(p, GetParagraphLength(p), _start, _end, Index);
            // Every paragraph with a position in the range, an EMPTY one included: it covers no character but is a line
            // of the selection — dropped, a blank line between two selected paragraphs vanished from the copied text
            // (the AOT sweep's read-back came back one line short, 2026-10-18).
            if (span.From >= 0)
                spans[p] = (span.From, Math.Max(span.From, span.To));
        }
        (int, int) SpanOf(Paragraph h) => spans.TryGetValue(h, out var s) ? s : (0, 0);
        foreach (var p in paras)
            if (spans.TryGetValue(p, out var s))
                result.Add(new CoveredPart(p, s.From, s.To, PositionOrder.InsideCoveredObject(p, SpanOf)));
        return result;
    }

    // The covered parts in POSITION order (an inline table's cells where the table is), for what is read in
    // sequence: the text and the copied runs.
    private List<CoveredPart> CoveredInPositionOrder(bool skipCoveredObjects)
    {
        var parts = Covered();
        if (skipCoveredObjects) parts.RemoveAll(c => c.InsideCoveredObject);
        parts.Sort((a, b) => ReferenceEquals(a.Paragraph, b.Paragraph) ? 0
            : new TextPointer(a.Paragraph, a.From).CompareTo(new TextPointer(b.Paragraph, b.From)));
        return parts;
    }

    private static bool HasInlineTable(Paragraph p)
    {
        foreach (var i in p.Inlines) if (i is InlineTable) return true;
        return false;
    }

    // One paragraph with no inline table in it: the range is that paragraph's [start, end) and nothing else.
    private bool IsSimple => ReferenceEquals(_start.Paragraph, _end.Paragraph) && _start.Paragraph is { } p && !HasInlineTable(p);

    /// <summary>Deletes all content covered by this range (across paragraph boundaries).
    /// After deletion, <see cref="Start"/> and <see cref="End"/> both point to the same position.</summary>
    public void Delete()
    {
        if (IsEmpty) return;
        var sp = _start.Paragraph;
        var ep = _end.Paragraph;
        if (sp == null || ep == null) return;

        if (IsSimple)
        {
            DeleteInParagraph(sp, _start.Offset, _end.Offset);
            _end = new TextPointer(sp, _start.Offset);
            return;
        }

        var parts = Covered(); // before anything moves
        var doc = GetFlowDocument(sp);
        if (doc == null)
        {
            // A paragraph in no document (a host's own, before it is added): only a range inside it has a part.
            foreach (var c in parts) DeleteInParagraph(c.Paragraph, c.From, c.To);
            _end = new TextPointer(sp, _start.Offset);
            return;
        }

        var container = ContainerOf(sp);
        bool siblings = !ReferenceEquals(sp, ep) && container != null && ReferenceEquals(container, ContainerOf(ep));
        if (siblings)
        {
            // Both endpoints are siblings in ONE block list — the document's top level, or a single
            // table cell (cells host sibling paragraphs since milestone A/P3). Remove the blocks
            // strictly between them (a spanned image/table/divider goes too); the two halves join below.
            // The old test was "is it a top-level block?", which classified two paragraphs of the SAME
            // cell as "crosses the grid" and skipped the merge, leaving a stray paragraph break behind.
            int si = container!.IndexOf(sp), ei = container.IndexOf(ep);
            if (si >= 0 && ei > si)
                for (int i = ei - 1; i > si; i--) container.RemoveAt(i);
            else siblings = false;
        }
        else
        {
            // The selection really does cross a cell boundary. Merging would move text across the
            // grid (the end cell's remainder landing in the start cell), so keep the structure:
            // remove the whole top-level blocks between the two endpoints' top-level blocks; the
            // covered paragraphs that remain are cleared below.
            var startTop = TopLevelBlockOf(doc, sp);
            var endTop = TopLevelBlockOf(doc, ep);
            int si = startTop != null ? doc.Blocks.IndexOf(startTop) : -1;
            int ei = endTop != null ? doc.Blocks.IndexOf(endTop) : -1;
            if (si >= 0 && ei > si)
                for (int i = ei - 1; i > si; i--) doc.Blocks.RemoveAt(i);
        }

        // Each paragraph's own part. The offsets are each paragraph's own, so the order does not matter, and a host
        // whose part takes an inline table removes the table with its cells, whatever was deleted in them first.
        foreach (var c in parts) DeleteInParagraph(c.Paragraph, c.From, c.To);

        if (siblings) MergeParagraphs(sp, ep, container!);

        _end = new TextPointer(sp, _start.Offset);
    }

    /// <summary>Returns the plain text content of this range (paragraphs joined with newlines;
    /// inline image placeholders are dropped).</summary>
    public string GetText()
    {
        if (IsEmpty) return "";
        var sp = _start.Paragraph;
        var ep = _end.Paragraph;
        if (sp == null || ep == null) return "";

        if (IsSimple)
        {
            return GetParagraphText(sp, _start.Offset, _end.Offset);
        }

        var parts = CoveredInPositionOrder(skipCoveredObjects: false);
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(GetParagraphText(parts[i].Paragraph, parts[i].From, parts[i].To));
        }
        return sb.ToString();
    }

    /// <summary>Returns cloned <see cref="Run"/>s covering the range with formatting preserved.
    /// Paragraph breaks are represented as <see cref="Run"/>s with <c>Text = "\n"</c>.</summary>
    public List<Run> GetRichRuns()
    {
        var result = new List<Run>();
        if (IsEmpty) return result;
        var sp = _start.Paragraph;
        var ep = _end.Paragraph;
        if (sp == null || ep == null) return result;

        if (IsSimple)
        {
            result.AddRange(GetParagraphRuns(sp, _start.Offset, _end.Offset));
            return result;
        }

        bool first = true;
        foreach (var c in CoveredInPositionOrder(skipCoveredObjects: false))
        {
            if (!first) result.Add(new Run { Text = "\n" });
            first = false;
            result.AddRange(GetParagraphRuns(c.Paragraph, c.From, c.To));
        }
        return result;
    }

    private List<Run> GetParagraphRuns(Paragraph p, int startOffset, int endOffset)
    {
        var result = new List<Run>();
        int idx = 0;
        foreach (var inline in p.Inlines)
        {
            int len = InlineLen(inline);
            if (inline is Run run)
            {
                int rs = Math.Max(startOffset, idx);
                int re = Math.Min(endOffset, idx + len);
                if (re > rs)
                {
                    var clone = (Run)run.Clone();
                    clone.Text = run.Text!.Substring(rs - idx, re - rs);
                    result.Add(clone);
                }
            }
            idx += len; // inline images advance the offset but aren't captured in run-based copy
        }
        return result;
    }

    /// <summary>Like <see cref="GetRichRuns"/> but also captures atomic object inlines
    /// (<see cref="InlineImage"/>, <see cref="InlineTable"/>), so an in-app copy/paste preserves them
    /// (the run-only version drops them). Paragraph breaks are <see cref="Run"/>s with <c>Text = "\n"</c>.</summary>
    public List<Inline> GetRichInlines()
    {
        var result = new List<Inline>();
        if (IsEmpty) return result;
        var sp = _start.Paragraph;
        var ep = _end.Paragraph;
        if (sp == null || ep == null) return result;

        if (IsSimple)
        {
            result.AddRange(GetParagraphInlines(sp, _start.Offset, _end.Offset));
            return result;
        }

        // An inline table the range takes whole comes as one object (GetParagraphInlines clones it), not cell by cell.
        bool first = true;
        foreach (var c in CoveredInPositionOrder(skipCoveredObjects: true))
        {
            if (!first) result.Add(new Run { Text = "\n" });
            first = false;
            result.AddRange(GetParagraphInlines(c.Paragraph, c.From, c.To));
        }
        return result;
    }

    private List<Inline> GetParagraphInlines(Paragraph p, int startOffset, int endOffset)
    {
        var result = new List<Inline>();
        int idx = 0;
        foreach (var inline in p.Inlines)
        {
            int len = InlineLen(inline);
            int segStart = idx, segEnd = idx + len;
            idx = segEnd;
            if (len == 0 || segEnd <= startOffset || segStart >= endOffset) continue;
            if (inline is Run run)
            {
                int rs = Math.Max(startOffset, segStart) - segStart;
                int re = Math.Min(endOffset, segEnd) - segStart;
                if (re > rs) { var c = (Run)run.Clone(); c.Text = run.Text!.Substring(rs, re - rs); result.Add(c); }
            }
            else if (inline is not Run && segStart >= startOffset && segEnd <= endOffset)
            {
                // Atomic object inline (image, table): capture it whole when fully inside the range.
                result.Add((Inline)inline.Clone());
            }
        }
        return result;
    }

    /// <summary>Applies <paramref name="styleAction"/> to every <see cref="Run"/> within the range,
    /// splitting runs at boundaries as needed.</summary>
    public void ApplyPropertyValue(Action<Run> styleAction)
    {
        if (IsEmpty) return;
        var sp = _start.Paragraph;
        var ep = _end.Paragraph;
        if (sp == null || ep == null) return;

        if (IsSimple)
        {
            ApplyStyleToParagraph(sp, _start.Offset, _end.Offset, styleAction);
            return;
        }
        // Every covered paragraph, the cells of an inline table the range takes whole included: their text is selected.
        foreach (var c in Covered()) ApplyStyleToParagraph(c.Paragraph, c.From, c.To, styleAction);
    }

    private string GetParagraphText(Paragraph p, int startOffset, int endOffset)
    {
        var sb = new StringBuilder();
        foreach (var inline in p.Inlines)
        {
            if (inline is Run r && r.Text != null) sb.Append(r.Text);
            else if (inline is not Run) sb.Append(ObjChar);
        }
        string fullText = sb.ToString();

        if (startOffset >= fullText.Length) return "";
        int len = Math.Min(endOffset - startOffset, fullText.Length - startOffset);
        if (len <= 0) return "";
        // Drop image placeholders from copied plain text.
        return fullText.Substring(startOffset, len).Replace(ObjChar.ToString(), "");
    }

    private void DeleteInParagraph(Paragraph p, int startOffset, int endOffset)
    {
        if (startOffset >= endOffset) return;

        SplitRunAtOffset(p, endOffset);
        SplitRunAtOffset(p, startOffset);

        int currentLeft = 0;
        var toRemove = new List<Inline>();
        foreach (var inline in p.Inlines)
        {
            int len = InlineLen(inline);
            if (len > 0 && currentLeft >= startOffset && currentLeft + len <= endOffset)
            {
                toRemove.Add(inline); // removes runs and inline images that fall inside the range
            }
            currentLeft += len;
        }
        foreach (var item in toRemove) p.Inlines.Remove(item);
        // A paragraph always holds at least one Run — the whole offset model (BuildPlain, caret placement,
        // formatting at an empty caret) assumes it. The editor's own delete paths restore it afterwards,
        // but TextRange.Delete() is public API: called directly on a range covering everything, it left a
        // paragraph with no inlines at all.
        if (p.Inlines.Count == 0) p.Inlines.Add(new Run { Text = "", Parent = p });
        CoalesceRuns(p); // removing a run can leave its former neighbours adjacent with equal formatting
    }

    // Joins `source` into `target` — both siblings of `container` — and drops it from that list.
    private void MergeParagraphs(Paragraph target, Paragraph source, IList<Block> container)
    {
        foreach (var inline in source.Inlines)
        {
            inline.Parent = target;
            target.Inlines.Add(inline);
        }
        source.Inlines.Clear();

        container.Remove(source);
        CoalesceRuns(target); // the boundary runs may now be adjacent with identical formatting
    }

    // The block list that directly holds `p`: the document's top level, or an enclosing table cell's
    // blocks (at any nesting depth). Same container switch the editor's split/insert paths use.
    private static IList<Block>? ContainerOf(Paragraph p) => p.Parent switch
    {
        FlowDocument d => d.Blocks,
        TableCell tc => tc.Blocks,
        _ => null
    };

    // Merges adjacent <see cref="Run"/>s that share all formatting into one, so an edit (paragraph
    // merge, mid-paragraph delete) doesn't leave the run list fragmented. Text and logical offsets are
    // unchanged — only the run count shrinks — so caret/selection positions stay valid. Inline images
    // act as boundaries (never merged). Shared by the editor's manual merge paths too.
    internal static void CoalesceRuns(Paragraph p)
    {
        for (int i = 0; i < p.Inlines.Count - 1; )
        {
            if (p.Inlines[i] is Run a && p.Inlines[i + 1] is Run b && RunFormatEquals(a, b))
            {
                a.Text = (a.Text ?? "") + (b.Text ?? "");
                p.Inlines.RemoveAt(i + 1); // stay at i to fold a whole run of identical neighbours
            }
            else i++;
        }
    }

    // CoalesceRuns for every paragraph in a document, at any depth. An importer builds one run per source
    // node, so the same line can arrive as one run or as several depending on how the markup happened to
    // be split — and re-exporting it welds those nodes together, so a second import produced a DIFFERENT
    // run list from the same content. Text and offsets are untouched; only the run count changes.
    internal static void CoalesceAll(FlowDocument doc)
    {
        var all = new List<Paragraph>();
        CollectParagraphs(doc.Blocks, all);
        foreach (var p in all) CoalesceRuns(p);
    }

    private static bool RunFormatEquals(Run a, Run b)
        => a.FontWeight == b.FontWeight
        && a.FontStyle == b.FontStyle
        && a.FontSize.Equals(b.FontSize)
        && a.FontFamily == b.FontFamily
        && a.NavigateUri == b.NavigateUri
        && BrushEquals(a.Foreground, b.Foreground)
        && BrushEquals(a.Background, b.Background)
        && DecorationEquals(a.TextDecorations, b.TextDecorations);

    // Equal brushes (so re-instantiated same-colour brushes still coalesce) without forcing every
    // brush type to be value-comparable: same reference, or two solid brushes of the same colour.
    private static bool BrushEquals(IBrush? a, IBrush? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is ISolidColorBrush sa && b is ISolidColorBrush sb) return sa.Color == sb.Color;
        return false;
    }

    private static bool DecorationEquals(TextDecorationCollection? a, TextDecorationCollection? b)
        => HasLocation(a, TextDecorationLocation.Underline) == HasLocation(b, TextDecorationLocation.Underline)
        && HasLocation(a, TextDecorationLocation.Strikethrough) == HasLocation(b, TextDecorationLocation.Strikethrough);

    private static bool HasLocation(TextDecorationCollection? decos, TextDecorationLocation loc)
    {
        if (decos == null) return false;
        foreach (var d in decos) if (d.Location == loc) return true;
        return false;
    }

    /// <summary>The top-level <see cref="Block"/> of <paramref name="doc"/> that contains
    /// <paramref name="p"/>: the paragraph itself when it is a document block, otherwise the outermost
    /// block it lives in.</summary>
    // Walks the Parent chain (Paragraph -> TableCell -> TableBlock -> [InlineTable -> host Paragraph]
    // -> ... -> FlowDocument), so it resolves paragraphs at ANY depth. The previous one-level scan of
    // every cell's block list returned null for a paragraph inside a nested or inline table, and the
    // callers then silently skipped the blocks between the selection endpoints. Shared with the editor
    // so there is a single walker (like CoalesceRuns).
    internal static Block? TopLevelBlockOf(FlowDocument doc, Paragraph p)
    {
        object? current = p;
        while (current is TextElement te)
        {
            if (te is Block b && ReferenceEquals(te.Parent, doc)) return b;
            current = te.Parent;
        }
        return null;
    }

    private FlowDocument? GetFlowDocument(TextElement element)
    {
        object? current = element;
        while (current != null)
        {
            if (current is FlowDocument doc) return doc;
            if (current is TextElement te) current = te.Parent;
            else break;
        }
        return null;
    }

    private void ApplyStyleToParagraph(Paragraph p, int startOffset, int endOffset, Action<Run> styleAction)
    {
        if (startOffset >= endOffset) return;

        SplitRunAtOffset(p, endOffset);
        SplitRunAtOffset(p, startOffset);

        int currentIndex = 0;
        foreach (var inline in p.Inlines)
        {
            int len = InlineLen(inline);
            if (inline is Run run && currentIndex >= startOffset && currentIndex + len <= endOffset)
            {
                styleAction(run);
            }
            currentIndex += len;
        }
        CoalesceRuns(p); // toggling a style back off can make the split runs match their neighbours again
    }

    private void SplitRunAtOffset(Paragraph p, int offset)
    {
        if (offset <= 0) return;

        int currentIndex = 0;
        for (int i = 0; i < p.Inlines.Count; i++)
        {
            int len = InlineLen(p.Inlines[i]);
            if (p.Inlines[i] is Run run)
            {
                int runLen = len;
                if (offset > currentIndex && offset < currentIndex + runLen)
                {
                    int localSplit = offset - currentIndex;

                    string text1 = run.Text!.Substring(0, localSplit);
                    string text2 = run.Text!.Substring(localSplit);

                    run.Text = text1;

                    // Clone() copies every formatting field (a hand-rolled copy once dropped
                    // FontFamily/Background, losing them on the split tail).
                    var newRun = (Run)run.Clone();
                    newRun.Text = text2;
                    newRun.Parent = p;

                    p.Inlines.Insert(i + 1, newRun);
                    return;
                }
            }
            currentIndex += len;
        }
    }

    // An atomic object inline (image, table) counts as a single character position (U+FFFC), matching
    // the editor's logical offset model so split/delete/style ranges stay aligned with the rendered layout.
    private const char ObjChar = '￼';
    private static int InlineLen(Inline i) => i is Run r ? (r.Text?.Length ?? 0) : 1;

    private int GetParagraphLength(Paragraph p)
    {
        int len = 0;
        foreach (var inline in p.Inlines) len += InlineLen(inline);
        return len;
    }

    private List<Paragraph> GetAllParagraphsInOrder(FlowDocument doc)
    {
        var result = new List<Paragraph>();
        CollectParagraphs(doc.Blocks, result);
        return result;
    }

    // Mirror of the control's ParagraphsInBlocks: fully recursive through table cells (nested tables) and
    // through inline tables hanging off a paragraph's inlines, using logical (anchor) cells only so the
    // index-based range loops agree with the control's order on merged tables.
    internal static void CollectParagraphs(IEnumerable<Block> blocks, List<Paragraph> result)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph p)
            {
                result.Add(p);
                foreach (var inl in p.Inlines)
                    if (inl is InlineTable it)
                        foreach (var (_, _, cell) in it.Table.LogicalCells())
                            CollectParagraphs(cell.Blocks, result);
            }
            else if (block is TableBlock tb)
                foreach (var (_, _, cell) in tb.LogicalCells())
                    CollectParagraphs(cell.Blocks, result);
        }
    }
}
