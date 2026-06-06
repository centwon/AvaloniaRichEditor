using System;
using System.Collections.Generic;
using System.Text;

namespace AvaloniaRichTextBoxPort.Documents;

public class TextRange
{
    private TextPointer _start;
    private TextPointer _end;

    public TextPointer Start => _start;
    public TextPointer End => _end;

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

    public bool IsEmpty => _start.CompareTo(_end) == 0;

    public void Delete()
    {
        if (IsEmpty) return;

        if (ReferenceEquals(_start.Paragraph, _end.Paragraph))
        {
            DeleteInParagraph(_start.Paragraph, _start.Offset, _end.Offset);
        }
        else
        {
            var doc = GetFlowDocument(_start.Paragraph);
            if (doc == null) return;

            int p1Len = GetParagraphLength(_start.Paragraph);
            DeleteInParagraph(_start.Paragraph, _start.Offset, p1Len);

            // Remove every top-level block (paragraphs, images, tables) strictly between the
            // start and end paragraphs' top-level blocks, so a drag selection that spans an
            // image or table deletes those too.
            var startTop = TopLevelBlockOf(doc, _start.Paragraph);
            var endTop = TopLevelBlockOf(doc, _end.Paragraph);
            int si = startTop != null ? doc.Blocks.IndexOf(startTop) : -1;
            int ei = endTop != null ? doc.Blocks.IndexOf(endTop) : -1;
            if (si >= 0 && ei >= 0 && ei > si)
            {
                for (int i = ei - 1; i > si; i--) doc.Blocks.RemoveAt(i);
            }

            DeleteInParagraph(_end.Paragraph, 0, _end.Offset);

            MergeParagraphs(_start.Paragraph, _end.Paragraph, doc);
        }

        _end = new TextPointer(_start.Paragraph, _start.Offset);
    }

    public string GetText()
    {
        if (IsEmpty) return "";

        if (ReferenceEquals(_start.Paragraph, _end.Paragraph))
        {
            return GetParagraphText(_start.Paragraph, _start.Offset, _end.Offset);
        }

        var doc = GetFlowDocument(_start.Paragraph);
        if (doc == null) return "";

        var allParagraphs = GetAllParagraphsInOrder(doc);
        int startIdx = allParagraphs.IndexOf(_start.Paragraph);
        int endIdx = allParagraphs.IndexOf(_end.Paragraph);

        var sb = new StringBuilder();
        int p1Len = GetParagraphLength(_start.Paragraph);
        sb.Append(GetParagraphText(_start.Paragraph, _start.Offset, p1Len));

        for (int i = startIdx + 1; i < endIdx; i++)
        {
            sb.Append('\n');
            int pLen = GetParagraphLength(allParagraphs[i]);
            sb.Append(GetParagraphText(allParagraphs[i], 0, pLen));
        }

        sb.Append('\n');
        sb.Append(GetParagraphText(_end.Paragraph, 0, _end.Offset));

        return sb.ToString();
    }

    // Returns cloned Runs covering the range, preserving formatting. Paragraph breaks within
    // the range are represented as Runs whose Text is "\n" (matching how this editor stores
    // line breaks inside a paragraph), so the result can be re-inserted at any caret position.
    public List<Run> GetRichRuns()
    {
        var result = new List<Run>();
        if (IsEmpty) return result;

        if (ReferenceEquals(_start.Paragraph, _end.Paragraph))
        {
            result.AddRange(GetParagraphRuns(_start.Paragraph, _start.Offset, _end.Offset));
            return result;
        }

        var doc = GetFlowDocument(_start.Paragraph);
        if (doc == null) return result;

        var allParagraphs = GetAllParagraphsInOrder(doc);
        int startIdx = allParagraphs.IndexOf(_start.Paragraph);
        int endIdx = allParagraphs.IndexOf(_end.Paragraph);

        result.AddRange(GetParagraphRuns(_start.Paragraph, _start.Offset, GetParagraphLength(_start.Paragraph)));
        for (int i = startIdx + 1; i < endIdx; i++)
        {
            result.Add(new Run { Text = "\n" });
            result.AddRange(GetParagraphRuns(allParagraphs[i], 0, GetParagraphLength(allParagraphs[i])));
        }
        result.Add(new Run { Text = "\n" });
        result.AddRange(GetParagraphRuns(_end.Paragraph, 0, _end.Offset));

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

    public void ApplyPropertyValue(Action<Run> styleAction)
    {
        if (IsEmpty) return;

        if (ReferenceEquals(_start.Paragraph, _end.Paragraph))
        {
            ApplyStyleToParagraph(_start.Paragraph, _start.Offset, _end.Offset, styleAction);
        }
        else
        {
            var doc = GetFlowDocument(_start.Paragraph);
            if (doc == null) return;

            var allParagraphs = GetAllParagraphsInOrder(doc);
            int startIdx = allParagraphs.IndexOf(_start.Paragraph);
            int endIdx = allParagraphs.IndexOf(_end.Paragraph);

            int p1Len = GetParagraphLength(_start.Paragraph);
            ApplyStyleToParagraph(_start.Paragraph, _start.Offset, p1Len, styleAction);

            for (int i = startIdx + 1; i < endIdx; i++)
            {
                int pLen = GetParagraphLength(allParagraphs[i]);
                ApplyStyleToParagraph(allParagraphs[i], 0, pLen, styleAction);
            }

            ApplyStyleToParagraph(_end.Paragraph, 0, _end.Offset, styleAction);
        }
    }

    private string GetParagraphText(Paragraph p, int startOffset, int endOffset)
    {
        var sb = new StringBuilder();
        foreach (var inline in p.Inlines)
        {
            if (inline is Run r && r.Text != null) sb.Append(r.Text);
            else if (inline is InlineImage) sb.Append(ObjChar);
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
    }

    private void MergeParagraphs(Paragraph target, Paragraph source, FlowDocument doc)
    {
        foreach (var inline in source.Inlines)
        {
            inline.Parent = target;
            target.Inlines.Add(inline);
        }
        source.Inlines.Clear();

        RemoveParagraphFromDocument(doc, source);
    }

    private void RemoveParagraphFromDocument(FlowDocument doc, Paragraph p)
    {
        if (doc.Blocks.Contains(p))
        {
            doc.Blocks.Remove(p);
            return;
        }

        foreach (var block in doc.Blocks)
        {
            if (block is TableBlock tb)
            {
                for (int r = 0; r < tb.Rows; r++)
                    for (int c = 0; c < tb.Columns; c++)
                        if (tb.Cells[r][c] == p)
                        {
                            p.Inlines.Clear();
                            p.Inlines.Add(new Run { Text = "", Parent = p });
                            return;
                        }
            }
        }
    }

    // The top-level Block in the document that contains the given paragraph: the paragraph
    // itself if it's a top-level block, otherwise the TableBlock whose cell it is.
    private Block? TopLevelBlockOf(FlowDocument doc, Paragraph p)
    {
        foreach (var block in doc.Blocks)
        {
            if (block == p) return block;
            if (block is TableBlock tb)
                for (int r = 0; r < tb.Rows; r++)
                    for (int c = 0; c < tb.Columns; c++)
                        if (tb.Cells[r][c] == p) return tb;
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

                    var newRun = new Run
                    {
                        Text = text2,
                        FontWeight = run.FontWeight,
                        FontStyle = run.FontStyle,
                        FontSize = run.FontSize,
                        Foreground = run.Foreground,
                        TextDecorations = run.TextDecorations,
                        NavigateUri = run.NavigateUri,
                        Parent = p
                    };

                    p.Inlines.Insert(i + 1, newRun);
                    return;
                }
            }
            currentIndex += len;
        }
    }

    // One inline image counts as a single character position (U+FFFC), matching the editor's
    // logical offset model so split/delete/style ranges stay aligned with the rendered layout.
    private const char ObjChar = '￼';
    private static int InlineLen(Inline i) => i is Run r ? (r.Text?.Length ?? 0) : (i is InlineImage ? 1 : 0);

    private int GetParagraphLength(Paragraph p)
    {
        int len = 0;
        foreach (var inline in p.Inlines) len += InlineLen(inline);
        return len;
    }

    private List<Paragraph> GetAllParagraphsInOrder(FlowDocument doc)
    {
        var result = new List<Paragraph>();
        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph p) result.Add(p);
            else if (block is TableBlock tb)
            {
                for (int r = 0; r < tb.Rows; r++)
                    for (int c = 0; c < tb.Columns; c++)
                        result.Add(tb.Cells[r][c]);
            }
        }
        return result;
    }
}
