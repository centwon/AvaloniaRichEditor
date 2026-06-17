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
    /// <summary>Sets the foreground brush of the current selection (or the caret run).</summary>
    public void SetForeground(IBrush brush) { ApplyStyleToSelection(r => r.Foreground = brush); }
    /// <summary>Sets the font family of the current selection (or the caret run).</summary>
    public void SetFontFamily(string family) { ApplyStyleToSelection(r => r.FontFamily = family); }
    /// <summary>Sets the highlight (background) brush of the current selection; pass <see langword="null"/> to clear.</summary>
    public void SetHighlight(IBrush? brush) { ApplyStyleToSelection(r => r.Background = brush); }

    /// <summary>Adjusts the indent of the caret paragraph by <paramref name="delta"/> pixels (clamped 0–400).</summary>
    public void Indent(double delta)
    {
        if (_caretPosition.Paragraph == null || IsReadOnly) return;
        if (Document != null) PushUndo();
        var p = _caretPosition.Paragraph;
        p.Indent = Math.Clamp(p.Indent + delta, 0, 400);
        InvalidateVisual();
    }
    /// <summary>Sets the text alignment of the caret paragraph.</summary>
    public void SetTextAlignment(TextAlignment align) { if (_caretPosition.Paragraph != null && !IsReadOnly) { if (Document != null) PushUndo(); _caretPosition.Paragraph.TextAlignment = align; InvalidateVisual(); } }
    /// <summary>Sets the absolute line-box height (px) of the caret paragraph ("exactly" spacing).
    /// Prefer <see cref="SetLineSpacing"/> for proportional spacing that scales with font size.</summary>
    public void SetLineHeight(double height) { if (_caretPosition.Paragraph != null && !IsReadOnly) { if (Document != null) PushUndo(); _caretPosition.Paragraph.LineHeight = height; InvalidateVisual(); } }
    /// <summary>Sets proportional line spacing on the caret paragraph as a multiple of the natural
    /// single-line height (1.0 = single, 1.5 = 1.5 lines; HWP % ÷ 100). <see cref="double.NaN"/> clears it.</summary>
    public void SetLineSpacing(double multiplier) { if (_caretPosition.Paragraph != null && !IsReadOnly) { if (Document != null) PushUndo(); _caretPosition.Paragraph.LineSpacing = multiplier; InvalidateVisual(); } }
    /// <summary>Toggles a bullet list on the selected paragraphs.</summary>
    public void ToggleBullet() { SetListType(ListKind.Bullet); }
    /// <summary>Toggles a numbered list on the selected paragraphs.</summary>
    public void ToggleNumbering() { SetListType(ListKind.Ordered); }
    /// <summary>Applies a specific bullet/number marker style to the selected paragraphs, turning the
    /// list on (never a toggle). The style implies the list kind (bullets vs numbers).</summary>
    public void SetListStyle(ListMarkerStyle style) { SetListType(ListMarkerStyleKind(style), style); }

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

        // Apply to every selected top-level paragraph (just the caret's when there's no selection).
        var targets = SelectedTopLevelParagraphs();
        if (targets.Count == 0)
        {
            // Caret in a table cell etc. -> just flag that paragraph.
            _caretPosition.Paragraph.ListType = turningOff ? ListKind.None : kind;
            ApplyMarker(_caretPosition.Paragraph);
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
            ListType = p.ListType,
            ListLevel = p.ListLevel,
            Indent = p.Indent,
            TextAlignment = p.TextAlignment,
            Background = p.Background
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

    /// <summary>Sets the heading level of the caret paragraph (1–6 = h1–h6, 0 = body).
    /// The heading's larger, bold look is applied at layout time (to runs left at the body default),
    /// not baked into the runs — so toggling a heading on and back off never overwrites or loses a
    /// run's manually-set font size.</summary>
    public void SetHeading(int level)
    {
        if (_caretPosition.Paragraph == null || IsReadOnly) return;
        if (Document != null) PushUndo();
        _caretPosition.Paragraph.HeadingLevel = level;
        InvalidateVisual();
        NotifyStatus(); // the heading size changes the paragraph's height -> re-measure the scroll extent
    }

    /// <summary>Toggles blockquote styling (indented, with a quote bar) on the caret paragraph.</summary>
    public void ToggleQuote()
    {
        if (_caretPosition.Paragraph == null || IsReadOnly) return;
        if (Document != null) PushUndo();
        _caretPosition.Paragraph.IsQuote = !_caretPosition.Paragraph.IsQuote;
        InvalidateVisual();
        NotifyStatus();
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
