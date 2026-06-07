using System;

namespace AvaloniaRichTextBox.Documents;

public class TextPointer : IComparable<TextPointer>
{
    public Paragraph? Paragraph { get; set; }
    public int Offset { get; set; }

    public TextPointer(Paragraph? paragraph, int offset)
    {
        Paragraph = paragraph;
        Offset = offset;
    }

    public override bool Equals(object? obj) => obj is TextPointer t && t.Paragraph == Paragraph && t.Offset == Offset;
    public override int GetHashCode() => (Paragraph?.GetHashCode() ?? 0) ^ Offset.GetHashCode();
    public static bool operator ==(TextPointer? a, TextPointer? b) => ReferenceEquals(a, null) ? ReferenceEquals(b, null) : a.Equals(b);
    public static bool operator !=(TextPointer? a, TextPointer? b) => !(a == b);

    public int CompareTo(TextPointer? other)
    {
        if (other == null) return 1;
        if (ReferenceEquals(this.Paragraph, other.Paragraph))
        {
            return Offset.CompareTo(other.Offset);
        }

        // Compare by traversing FlowDocument blocks
        // We need to find the root FlowDocument
        var doc = GetFlowDocument(this.Paragraph);
        if (doc == null) return 0; // cannot compare without document

        int thisIdx = GetGlobalIndex(doc, this.Paragraph);
        int otherIdx = GetGlobalIndex(doc, other.Paragraph);

        return thisIdx.CompareTo(otherIdx);
    }

    private FlowDocument? GetFlowDocument(TextElement? element)
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

    private int GetGlobalIndex(FlowDocument doc, Paragraph? target)
    {
        int index = 0;
        bool found = false;

        void TraverseBlocks(System.Collections.IEnumerable blocks)
        {
            foreach (var block in blocks)
            {
                if (found) return;
                
                if (block is Paragraph p)
                {
                    if (p == target) { found = true; return; }
                    index++;
                }
                else if (block is TableBlock tb)
                {
                    index++; // table itself
                    for (int r = 0; r < tb.Rows; r++)
                    {
                        for (int c = 0; c < tb.Columns; c++)
                        {
                            var cell = tb.Cells[r][c];
                            if (cell == target) { found = true; return; }
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
        return found ? index : -1;
    }
}
