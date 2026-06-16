using System;

namespace AvaloniaRichEditor.Documents;

/// <summary>An immutable-by-convention position inside a <see cref="Paragraph"/>: the paragraph reference
/// plus a character offset. Inline images count as one logical character (the U+FFFC placeholder).</summary>
public class TextPointer : IComparable<TextPointer>
{
    /// <summary>The paragraph that contains this position.</summary>
    public Paragraph? Paragraph { get; set; }
    /// <summary>The character offset within <see cref="Paragraph"/>. Inline images count as 1.</summary>
    public int Offset { get; set; }

    /// <summary>Creates a new pointer at <paramref name="offset"/> inside <paramref name="paragraph"/>.</summary>
    public TextPointer(Paragraph? paragraph, int offset)
    {
        Paragraph = paragraph;
        Offset = offset;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TextPointer t && t.Paragraph == Paragraph && t.Offset == Offset;
    /// <inheritdoc/>
    public override int GetHashCode() => (Paragraph?.GetHashCode() ?? 0) ^ Offset.GetHashCode();
    /// <summary>Equality comparison.</summary>
    public static bool operator ==(TextPointer? a, TextPointer? b) => ReferenceEquals(a, null) ? ReferenceEquals(b, null) : a.Equals(b);
    /// <summary>Inequality comparison.</summary>
    public static bool operator !=(TextPointer? a, TextPointer? b) => !(a == b);

    /// <inheritdoc/>
    public int CompareTo(TextPointer? other)
    {
        if (other == null) return 1;
        if (ReferenceEquals(this.Paragraph, other.Paragraph))
        {
            return Offset.CompareTo(other.Offset);
        }

        // Different paragraphs: order by position in the document. A single ordered walk records both
        // paragraphs' global indices (it used to run two full traversals, one per pointer) and exits as
        // soon as both are located — their relative order can't change after that. A paragraph not found
        // keeps index -1, so it sorts before any present one (unchanged from the prior behaviour).
        var doc = GetFlowDocument(this.Paragraph);
        if (doc == null) return 0; // cannot compare without document

        int index = 0, thisIdx = -1, otherIdx = -1;
        void Locate(Paragraph cell)
        {
            if (thisIdx < 0 && ReferenceEquals(cell, this.Paragraph)) thisIdx = index;
            if (otherIdx < 0 && ReferenceEquals(cell, other.Paragraph)) otherIdx = index;
            index++;
        }

        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph p) Locate(p);
            else if (block is TableBlock tb)
            {
                index++; // the table itself occupies one index, matching the historical numbering
                for (int r = 0; r < tb.Rows; r++)
                    for (int c = 0; c < tb.Columns; c++)
                        Locate(tb.Cells[r][c]);
            }
            else index++;
            if (thisIdx >= 0 && otherIdx >= 0) break; // both located: their order is now fixed
        }

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
}
