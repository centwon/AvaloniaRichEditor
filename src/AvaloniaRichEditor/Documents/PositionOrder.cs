using System;
using System.Collections.Generic;

namespace AvaloniaRichEditor.Documents;

// The order of two positions, and which part of a paragraph a range covers — the one rule every selection path
// (TextRange's delete, text and format, the editor's copy, highlight and comparisons) goes by.
//
// The document's paragraph order (TextRange.CollectParagraphs) lists an inline table's cells right AFTER the paragraph
// holding it. As an order of POSITIONS that was wrong for the host's text after the table: "ab[table]cd" put every
// cell position after "cd", so a selection from just after the table back into its last cell (one Shift+Left) covered
// every OTHER cell instead, and Delete emptied them (WinUI port's pre-publish audit, 2026-10-18; measured here too).
// Here a position inside an inline table sits, for its host, between the table's offset k and k + 1 — where the table
// is on the line — at any depth of nesting. Paragraphs with no host in common keep the document order.
internal static class PositionOrder
{
    // A position seen from each paragraph that holds it: (the pointer's paragraph, 2·offset), then for every inline
    // table around it (its host paragraph, 2·k + 1). Even ranks are positions, odd ones "inside the table at k".
    private static List<(Paragraph P, int Rank)> Chain(Paragraph p, int offset)
    {
        var chain = new List<(Paragraph, int)> { (p, 2 * offset) };
        for (object? cur = p.Parent; cur is TextElement te; cur = te.Parent)
            if (te is InlineTable it && it.Parent is Paragraph host && OffsetOf(host, it) is int k)
                chain.Add((host, 2 * k + 1));
        return chain;
    }

    private static int? OffsetOf(Paragraph host, Inline target)
    {
        int off = 0;
        foreach (var inl in host.Inlines)
        {
            if (ReferenceEquals(inl, target)) return off;
            off += inl is Run r ? (r.Text?.Length ?? 0) : 1;
        }
        return null;
    }

    /// <summary>Orders (pa, oa) against (pb, ob). <paramref name="index"/> gives a paragraph's place in the document
    /// order (TextRange.CollectParagraphs); it is asked only about paragraphs with no host in common.</summary>
    internal static int Compare(Paragraph? pa, int oa, Paragraph? pb, int ob, Func<Paragraph, int> index)
    {
        if (ReferenceEquals(pa, pb)) return oa.CompareTo(ob);
        if (pa == null || pb == null) return pa == null ? -1 : 1;
        var ca = Chain(pa, oa);
        var cb = Chain(pb, ob);
        // The innermost paragraph both lie in (one may be that paragraph itself).
        for (int i = 0; i < ca.Count; i++)
            for (int j = 0; j < cb.Count; j++)
            {
                if (!ReferenceEquals(ca[i].P, cb[j].P)) continue;
                int c = ca[i].Rank.CompareTo(cb[j].Rank);
                if (c != 0) return c;
                // Both inside the same inline table of that paragraph (equal odd ranks; an even rank is the pointer's
                // own paragraph, which differs): the table's own paragraph order decides, one level down.
                return index(ca[i - 1].P).CompareTo(index(cb[j - 1].P));
            }
        return index(ca[^1].P).CompareTo(index(cb[^1].P));
    }

    /// <summary>The paragraphs <see cref="Compare"/> could ask its index function about for these two positions:
    /// the outermost paragraph of each, and the ones just inside a shared inline table.</summary>
    internal static void IndexedParagraphs(Paragraph pa, Paragraph pb, HashSet<Paragraph> into)
    {
        foreach (var (p, _) in Chain(pa, 0)) into.Add(p);
        foreach (var (p, _) in Chain(pb, 0)) into.Add(p);
    }

    /// <summary>The part [from, to) of paragraph <paramref name="p"/> (length <paramref name="len"/>) lying between
    /// <paramref name="start"/> and <paramref name="end"/> (start ≤ end): the offsets o with start ≤ (p, o) and
    /// (p, o) ≤ end. Empty (from ≥ to) when it covers no character. Positions inside a paragraph are ordered, so
    /// both bounds are a binary search.</summary>
    internal static (int From, int To) Span(Paragraph p, int len, TextPointer start, TextPointer end, Func<Paragraph, int> index)
    {
        int Cmp(int o, TextPointer t) => Compare(p, o, t.Paragraph, t.Offset, index);
        if (Cmp(len, start) < 0 || Cmp(0, end) > 0) return (0, 0);
        // Most paragraphs of a long selection lie wholly inside it; the highlight asks this for each one it draws.
        if (Cmp(0, start) >= 0 && Cmp(len, end) <= 0) return (0, len);
        int lo = 0, hi = len; // first o with (p, o) >= start
        while (lo < hi) { int mid = (lo + hi) / 2; if (Cmp(mid, start) >= 0) hi = mid; else lo = mid + 1; }
        int from = lo;
        lo = 0; hi = len;     // last o with (p, o) <= end
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (Cmp(mid, end) <= 0) lo = mid; else hi = mid - 1; }
        return (from, lo);
    }

    /// <summary>Whether <paramref name="p"/> lies in an inline table whose placeholder a covered span of its host takes
    /// whole — the table goes (or is copied) as one object, so its cells are not ranges of their own.</summary>
    internal static bool InsideCoveredObject(Paragraph p, Func<Paragraph, (int From, int To)> span)
    {
        for (object? cur = p.Parent; cur is TextElement te; cur = te.Parent)
            if (te is InlineTable it && it.Parent is Paragraph host && OffsetOf(host, it) is int k)
            {
                var (f, t) = span(host);
                if (f <= k && k < t) return true;
            }
        return false;
    }
}
