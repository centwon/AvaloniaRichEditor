using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Character/paragraph formatting commands (toolbar/context-menu surface), list toggling with
// hard-line splitting, hyperlinks, and the format painter. Part of RichEditor (split out of the
// main file for readability).
public partial class RichEditor
{
    // ---- Format painter ----
    // Snapshot of character formatting captured from the source selection; non-null while armed.
    private (FontWeight w, FontStyle st, TextDecorationCollection? dec, double size, string? family, IBrush? fg, IBrush? bg)? _painterFmt;

    /// <summary>True while the format painter is armed (the next selection receives the captured formatting).</summary>
    public bool IsFormatPainterActive => _painterFmt != null;

    /// <summary>
    /// Captures character formatting from the current caret/selection and arms the format painter: the
    /// next selection the user makes will receive that formatting. Calling again re-captures; if already
    /// armed, cancels. Bind to a toolbar toggle.
    /// </summary>
    public void StartFormatPainter()
    {
        if (_painterFmt != null) { CancelFormatPainter(); return; } // toggle off
        var p = _selectionStart.Paragraph ?? _caretPosition.Paragraph;
        if (p == null) return;
        int off = _selectionStart.Paragraph != null ? _selectionStart.Offset : _caretPosition.Offset;
        var src = RunAtOffset(p, off) ?? RunAtOffset(p, Math.Max(0, off - 1));
        if (src == null) return;
        _painterFmt = (src.FontWeight, src.FontStyle, src.TextDecorations,
            src.FontSize, src.FontFamily, src.Foreground, src.Background);
        Cursor = CrossCursor;
    }

    /// <summary>Disarms the format painter without applying.</summary>
    public void CancelFormatPainter()
    {
        _painterFmt = null;
        Cursor = IbeamCursor;
    }

    private void ApplyFormatPainterToSelection()
    {
        if (_painterFmt is not { } f) return;
        if (_selectionStart.Paragraph == null || _selectionStart.CompareTo(_selectionEnd) == 0) return;
        ApplyStyleToSelection(r =>
        {
            r.FontWeight = f.w; r.FontStyle = f.st; r.TextDecorations = f.dec;
            r.FontSize = f.size; r.FontFamily = f.family; r.Foreground = f.fg; r.Background = f.bg;
        });
        CancelFormatPainter();
    }

    /// <summary>Toggles bold on the current selection (or the caret run).</summary>
    public void ToggleBold() { ApplyStyleToSelection(r => r.FontWeight = r.FontWeight == FontWeight.Bold ? FontWeight.Normal : FontWeight.Bold); }
    /// <summary>Toggles italic on the current selection (or the caret run).</summary>
    public void ToggleItalic() { ApplyStyleToSelection(r => r.FontStyle = r.FontStyle == FontStyle.Italic ? FontStyle.Normal : FontStyle.Italic); }
    /// <summary>Sets the font size of the current selection (or the caret run).</summary>
    public void SetFontSize(double size) { ApplyStyleToSelection(r => r.FontSize = size); }

    // Standard point-size ladder for the 크게/작게 (larger/smaller) commands.
    private static readonly double[] FontSizeLadder =
        { 8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 48, 56, 72, 96 };

    /// <summary>Bumps the font size to the next larger step on the standard ladder (based on the caret size).</summary>
    public void IncreaseFontSize() => StepFontSize(+1);
    /// <summary>Drops the font size to the next smaller step on the standard ladder (based on the caret size).</summary>
    public void DecreaseFontSize() => StepFontSize(-1);

    private void StepFontSize(int dir)
    {
        double cur = GetCaretFormat().FontSize;
        double target;
        if (dir > 0)
        {
            target = FontSizeLadder[^1];
            foreach (var v in FontSizeLadder) if (v > cur + 0.01) { target = v; break; }
        }
        else
        {
            target = FontSizeLadder[0];
            for (int i = FontSizeLadder.Length - 1; i >= 0; i--) if (FontSizeLadder[i] < cur - 0.01) { target = FontSizeLadder[i]; break; }
        }
        SetFontSize(target);
    }
    /// <summary>Sets the foreground brush of the current selection (or the caret run).</summary>
    public void SetForeground(IBrush brush) { ApplyStyleToSelection(r => r.Foreground = brush); }
    /// <summary>Sets the font family of the current selection (or the caret run).</summary>
    public void SetFontFamily(string family) { ApplyStyleToSelection(r => r.FontFamily = family); }
    /// <summary>Sets the highlight (background) brush of the current selection; pass <see langword="null"/> to clear.</summary>
    public void SetHighlight(IBrush? brush) { ApplyStyleToSelection(r => r.Background = brush); }

    // Applies a paragraph-level change to EVERY paragraph the selection touches (just the caret's when
    // the selection is collapsed), at any depth — cell and inline-table paragraphs included. Single
    // choke point for the paragraph commands, which each used to poke `_caretPosition.Paragraph`
    // directly: selecting several paragraphs and clicking "center" only aligned the one the caret
    // happened to land on, while the list commands on the same toolbar already applied to the whole
    // selection. NotifyStatus because indent/spacing/heading all change block heights.
    private void ApplyToSelectedParagraphs(Action<Paragraph> action)
    {
        if (_caretPosition.Paragraph == null || IsReadOnly) return;
        if (Document != null) PushUndo();
        var targets = SelectedParagraphsInOrder();
        if (targets.Count == 0) targets = new List<Paragraph> { _caretPosition.Paragraph };
        foreach (var p in targets) action(p);
        InvalidateVisual();
        NotifyStatus();
    }

    /// <summary>Adjusts the indent of every selected paragraph by <paramref name="delta"/> pixels
    /// (each clamped 0–400); the caret paragraph alone when nothing is selected.</summary>
    public void Indent(double delta)
        => ApplyToSelectedParagraphs(p => p.Indent = Math.Clamp(p.Indent + delta, 0, 400));
    /// <summary>Sets the text alignment of every selected paragraph (the caret paragraph when nothing
    /// is selected).</summary>
    public void SetTextAlignment(TextAlignment align)
        => ApplyToSelectedParagraphs(p => p.TextAlignment = align);
    /// <summary>Sets the absolute line-box height (px) of every selected paragraph ("exactly" spacing).
    /// Prefer <see cref="SetLineSpacing"/> for proportional spacing that scales with font size.</summary>
    public void SetLineHeight(double height)
        => ApplyToSelectedParagraphs(p => p.LineHeight = height);
    /// <summary>Sets proportional line spacing on every selected paragraph as a multiple of the natural
    /// single-line height (1.0 = single, 1.5 = 1.5 lines; HWP % ÷ 100). <see cref="double.NaN"/> clears it.</summary>
    public void SetLineSpacing(double multiplier)
        => ApplyToSelectedParagraphs(p => p.LineSpacing = multiplier);
    /// <summary>Toggles a bullet list on the selected paragraphs.</summary>
    public void ToggleBullet() { SetListType(ListKind.Bullet); }
    /// <summary>Toggles a numbered list on the selected paragraphs.</summary>
    public void ToggleNumbering() { SetListType(ListKind.Ordered); }
    /// <summary>Applies a specific bullet/number marker style to the selected paragraphs, turning the
    /// list on (never a toggle). The style implies the list kind (bullets vs numbers).</summary>
    public void SetListStyle(ListMarkerStyle style) { SetListType(ListMarkerStyleKind(style), style); }

    /// <summary>Removes the list attribute (bullet/number, marker style, and nesting level) from the
    /// selected paragraphs entirely. Unlike the toggle, this always clears regardless of the current list
    /// kind, so it's discoverable as a "None" list-style pick.</summary>
    public void RemoveList()
    {
        if (_caretPosition.Paragraph == null || Document == null || IsReadOnly) return;
        PushUndo();
        // Any depth: clearing a list needs no block splicing, so cell paragraphs are cleared too.
        var targets = SelectedParagraphsInOrder();
        if (targets.Count == 0) targets = new List<Paragraph> { _caretPosition.Paragraph };
        foreach (var p in targets)
        {
            p.ListType = ListKind.None;
            p.ListMarker = ListMarkerStyle.Default;
            p.ListLevel = 0;
        }
        UpdateParents(Document);
        InvalidateVisual();
    }

    // The list kind a marker style belongs to (number formats -> Ordered, everything else -> Bullet).
    private static ListKind ListMarkerStyleKind(ListMarkerStyle s) => s switch
    {
        ListMarkerStyle.Decimal or ListMarkerStyle.DecimalParen or ListMarkerStyle.LowerAlpha
            or ListMarkerStyle.UpperAlpha or ListMarkerStyle.LowerRoman => ListKind.Ordered,
        _ => ListKind.Bullet,
    };

    private void SetListType(ListKind kind, ListMarkerStyle? marker = null)
    {
        if (_caretPosition.Paragraph == null || Document == null || IsReadOnly) return;
        PushUndo();
        // A style pick always turns the list on (never toggles off); a plain bullet/number button toggles.
        bool turningOff = marker == null && _caretPosition.Paragraph.ListType == kind;
        void ApplyMarker(Paragraph par) { if (marker.HasValue) par.ListMarker = marker.Value; }

        // Apply to every selected paragraph (just the caret's when there's no selection). Only
        // top-level ones can take the hard-line splitting path below — it splices Document.Blocks —
        // so paragraphs living in a table cell are toggled in place here, however many are selected.
        var targets = new List<Paragraph>();
        foreach (var p in SelectedParagraphsInOrder())
        {
            if (Document.Blocks.Contains(p)) { targets.Add(p); continue; }
            p.ListType = turningOff ? ListKind.None : kind;
            ApplyMarker(p);
        }
        if (targets.Count == 0)
        {
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
            if (idx < 0) { tp.ListType = kind; ApplyMarker(tp); continue; }
            var items = SplitByNewlines(tp);
            foreach (var it in items) { it.ListType = kind; ApplyMarker(it); it.Parent = Document; }
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

    // Every paragraph the current selection touches, in document order and at ANY depth — table cells
    // and nested/inline tables included (or just the caret's paragraph when collapsed). Paragraph-level
    // commands must reach cell paragraphs too; only the ones that splice Document.Blocks need the
    // top-level subset below.
    private List<Paragraph> SelectedParagraphsInOrder()
    {
        var result = new List<Paragraph>();
        if (Document == null) return result;
        // An active cell block IS the selection — the rectangle the user sees filled, not the linear
        // document-order run between its two corners (which misses the part of the first/last cell
        // outside the drag offsets and sweeps in cells outside the rectangle).
        if (SelectedCellsBlock() is { } cells) return CellBlockParagraphs(cells);
        var all = GetAllParagraphsInOrder();
        int si = _selectionStart.Paragraph != null ? all.IndexOf(_selectionStart.Paragraph) : -1;
        int ei = _selectionEnd.Paragraph != null ? all.IndexOf(_selectionEnd.Paragraph) : -1;
        if (si < 0 || ei < 0)
        {
            if (_caretPosition.Paragraph != null) result.Add(_caretPosition.Paragraph);
            return result;
        }
        if (si > ei) (si, ei) = (ei, si);
        for (int i = si; i <= ei; i++) result.Add(all[i]);
        return result;
    }

    // Top-level paragraphs touched by the current selection (or just the caret's when collapsed).
    private List<Paragraph> SelectedTopLevelParagraphs()
    {
        var result = new List<Paragraph>();
        if (Document == null) return result;
        foreach (var p in SelectedParagraphsInOrder())
            if (Document.Blocks.Contains(p)) result.Add(p);
        return result;
    }

    // Splits a paragraph into one paragraph per hard line (\n), preserving inline formatting and the
    // paragraph's list/indent/alignment/background. Newlines are dropped (each becomes a paragraph break).
    private List<Paragraph> SplitByNewlines(Paragraph p)
    {
        var result = new List<Paragraph>();
        // Each split line is the same paragraph continued, so it carries the source's full format
        // (heading level, line spacing, marker style, quote bar, margins) — a hand-picked subset
        // here used to drop everything but list/indent/alignment/background.
        Paragraph NewPara()
        {
            var np = new Paragraph();
            np.CopyFormatFrom(p);
            return np;
        }
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
                // MOVE, don't clone. Only a run straddling a '\n' has to become new objects; everything
                // else appears exactly once in the output, and the source paragraph is spliced out of the
                // document on return. Cloning an InlineImage or InlineTable here replaced it with a copy
                // and left the original — with its cell paragraphs — detached, so a caret inside an
                // inline table's cell was orphaned by toggling a bullet on its host paragraph: it pointed
                // into a subtree no longer in the document, and typing went nowhere visible. (Safe to
                // re-parent in place: the enumeration doesn't modify p.Inlines, and the undo checkpoint
                // was taken before any of this.)
                inl.Parent = cur;
                cur.Inlines.Add(inl);
            }
        }
        result.Add(cur);
        foreach (var pp in result)
            if (pp.Inlines.Count == 0) pp.Inlines.Add(new Run { Text = "" });
        return result;
    }

    /// <summary>Sets the heading level of every selected paragraph (1–6 = h1–h6, 0 = body); the caret
    /// paragraph alone when nothing is selected.
    /// The heading's larger, bold look is applied at layout time (to runs left at the body default),
    /// not baked into the runs — so toggling a heading on and back off never overwrites or loses a
    /// run's manually-set font size.</summary>
    public void SetHeading(int level)
        => ApplyToSelectedParagraphs(p => p.HeadingLevel = level);

    /// <summary>Toggles blockquote styling (indented, with a quote bar) on every selected paragraph
    /// (the caret paragraph when nothing is selected).</summary>
    public void ToggleQuote()
    {
        // The caret paragraph decides the direction and every selected paragraph follows it, so a mixed
        // selection ends up uniform rather than inverted item by item (same rule as the list toggle).
        if (_caretPosition.Paragraph is not { } cp) return;
        bool on = !cp.IsQuote;
        ApplyToSelectedParagraphs(p => p.IsQuote = on);
    }

    /// <summary>Toggles strikethrough on the current selection (or the caret run).</summary>
    public void ToggleStrikethrough() { ApplyStyleToSelection(r => r.TextDecorations = ToggleDecoration(r.TextDecorations, TextDecorationLocation.Strikethrough)); }
    /// <summary>Toggles underline on the current selection (or the caret run).</summary>
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
        // Keyboard shortcuts are blocked in OnKeyDown, but the public commands (ToggleBold etc.)
        // must not mutate a ReadOnly document either.
        if (IsReadOnly) return;
        // A cell block styles every run of every selected cell, whole cells at a time — the linear
        // range would style only from the drag's offset in the first cell to its offset in the last.
        if (SelectedCellsBlock() is { } cells)
        {
            if (Document != null) PushUndo();
            foreach (var p in CellBlockParagraphs(cells))
            {
                foreach (var inl in p.Inlines) if (inl is Run r) styleAction(r);
                TextRange.CoalesceRuns(p); // styling a whole paragraph can make its runs identical
            }
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }
        if (_selectionStart != null && _selectionEnd != null && _selectionStart.CompareTo(_selectionEnd) != 0)
        {
            if (Document != null) PushUndo();
            var range = new TextRange(_selectionStart, _selectionEnd);
            range.ApplyPropertyValue(styleAction);
        }
        else if (_caretPosition.Paragraph is { } p)
        {
            // No selection (Word behaviour): a caret inside a word styles that word; on a word
            // boundary / empty line the toggle becomes pending and applies to the next typed text.
            string plain = BuildPlain(p);
            int off = Math.Clamp(_caretPosition.Offset, 0, plain.Length);
            static bool IsWord(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
            bool inWord = (off < plain.Length && IsWord(plain[off])) || (off > 0 && IsWord(plain[off - 1]));
            if (inWord)
            {
                var (ws, we) = WordBoundsAt(plain, off);
                if (Document != null) PushUndo();
                new TextRange(new TextPointer(p, ws), new TextPointer(p, we)).ApplyPropertyValue(styleAction);
            }
            else
            {
                // No document change yet — the undo checkpoint comes with the typing that applies it.
                (_pendingCaretStyles ??= new List<Action<Run>>()).Add(styleAction);
            }
        }
        // Font size / family change a run's line height, so the measure (block heights + ScrollViewer
        // extent + the viewport used by Draw-culling) must be re-run, not just the paint — otherwise the
        // selection highlight is drawn against the previous size's layout for one frame.
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void ClearFormatting()
    {
        ApplyStyleToSelection(r =>
        {
            r.FontWeight = FontWeight.Normal;
            r.FontStyle = FontStyle.Normal;
            r.FontSize = DefaultFontSize;
            r.Foreground = Brushes.Black;
            r.Background = null;
            r.FontFamily = null;
            r.TextDecorations = null;
            r.NavigateUri = null;
        });
    }

    // Applies (or clears, when url is null) a hyperlink. Uses the selection if there is one;
    // otherwise falls back to the single run that was right-clicked.
    private void SetHyperlink(string? url, Run? targetRun)
    {
        if (Document == null || IsReadOnly) return;
        PushUndo();
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
        string? url = await InputDialog.ShowAsync(owner, Loc("Hyperlink"), current ?? "https://");
        if (string.IsNullOrWhiteSpace(url)) return;
        SetHyperlink(url, targetRun);
    }
}
