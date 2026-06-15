using System;
using System.Collections.Generic;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Find / replace (the Ctrl+F bar): linear search over the paragraph order with wrap-around,
// anchored at the selection. Part of RichEditor (split out of the main file for readability).
public partial class RichEditor
{
    /// <summary>Selects the next occurrence of <paramref name="query"/> after the caret, wrapping around.
    /// Returns <see langword="true"/> if a match was found.</summary>
    public bool FindNext(string query, bool matchCase)
    {
        if (!AllowFindReplace || Document == null || string.IsNullOrEmpty(query)) return false;
        var paras = GetAllParagraphsInOrder();
        int pi = _selectionEnd.Paragraph != null ? paras.IndexOf(_selectionEnd.Paragraph) : -1;
        return FindCore(query, matchCase, backwards: false, wrap: true, fromPi: pi, fromOff: _selectionEnd.Offset);
    }

    /// <summary>Selects the previous occurrence of <paramref name="query"/> before the caret, wrapping around.
    /// Returns <see langword="true"/> if a match was found.</summary>
    public bool FindPrev(string query, bool matchCase)
    {
        if (!AllowFindReplace || Document == null || string.IsNullOrEmpty(query)) return false;
        var paras = GetAllParagraphsInOrder();
        int pi = _selectionStart.Paragraph != null ? paras.IndexOf(_selectionStart.Paragraph) : -1;
        return FindCore(query, matchCase, backwards: true, wrap: true, fromPi: pi, fromOff: _selectionStart.Offset);
    }

    /// <summary>Replaces the current selection if it matches <paramref name="query"/>, then advances
    /// to the next match. Returns <see langword="true"/> if a further match exists.</summary>
    public bool ReplaceNext(string query, string replacement, bool matchCase)
    {
        if (!AllowFindReplace || Document == null || string.IsNullOrEmpty(query)) return false;
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        bool selMatches = _selectionStart.Paragraph != null && _selectionStart.CompareTo(_selectionEnd) != 0
            && string.Equals(new TextRange(_selectionStart, _selectionEnd).GetText(), query, cmp);
        if (selMatches)
        {
            PushUndo();
            ReplaceSelectionText(replacement);
            InvalidateVisual();
        }
        return FindNext(query, matchCase);
    }

    /// <summary>Replaces every occurrence of <paramref name="query"/> in the document.
    /// Returns the number of replacements made.</summary>
    public int ReplaceAll(string query, string replacement, bool matchCase)
    {
        if (!AllowFindReplace || Document == null || string.IsNullOrEmpty(query)) return 0;
        var paras = GetAllParagraphsInOrder();
        if (paras.Count == 0) return 0;
        PushUndo();
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

        // The last match start in `text` that is < limit (limit = text.Length means "any"). Scans
        // forward keeping the highest qualifying index — cheap, and avoids LastIndexOf's start-index
        // pitfalls. Returns -1 when none.
        int LastMatchBefore(string text, int limit)
        {
            int best = -1, from = 0;
            while (from <= text.Length)
            {
                int idx = text.IndexOf(query, from, cmp);
                if (idx < 0 || idx >= limit) break;
                best = idx;
                from = idx + 1;
            }
            return best;
        }

        if (!backwards)
        {
            // First match at-or-after (fromPi, fromOff), scanning forward; stops at the first hit.
            for (int pi = Math.Max(0, fromPi); pi < paras.Count; pi++)
            {
                string text = BuildPlain(paras[pi]);
                int start = pi == fromPi ? Math.Max(0, fromOff) : 0;
                if (start > text.Length) continue;
                int idx = text.IndexOf(query, start, cmp);
                if (idx >= 0) { SelectMatch(paras[pi], idx, query.Length); return true; }
            }
            if (wrap) // first match overall
                for (int pi = 0; pi < paras.Count; pi++)
                {
                    int idx = BuildPlain(paras[pi]).IndexOf(query, cmp);
                    if (idx >= 0) { SelectMatch(paras[pi], idx, query.Length); return true; }
                }
        }
        else
        {
            // Last match strictly before (fromPi, fromOff), scanning backward; stops at the first hit.
            for (int pi = Math.Min(fromPi, paras.Count - 1); pi >= 0; pi--)
            {
                string text = BuildPlain(paras[pi]);
                int idx = LastMatchBefore(text, pi == fromPi ? Math.Min(fromOff, text.Length + 1) : text.Length + 1);
                if (idx >= 0) { SelectMatch(paras[pi], idx, query.Length); return true; }
            }
            if (wrap) // last match overall
                for (int pi = paras.Count - 1; pi >= 0; pi--)
                {
                    int idx = LastMatchBefore(BuildPlain(paras[pi]), int.MaxValue);
                    if (idx >= 0) { SelectMatch(paras[pi], idx, query.Length); return true; }
                }
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
}
