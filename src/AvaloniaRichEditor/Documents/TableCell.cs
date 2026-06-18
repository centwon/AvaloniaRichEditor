using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace AvaloniaRichEditor.Documents;

/// <summary>The content of one table cell: a list of block-level elements (like the body of an HTML
/// &lt;td&gt;) plus cell-level formatting (background). Milestone A introduces this so a cell can hold
/// more than a single paragraph (multiple paragraphs, block images, dividers, nested tables).
/// <para>Invariant: <see cref="Blocks"/> is never empty — an "empty" cell holds one empty
/// <see cref="Paragraph"/>. Through phases P1/P2 every cell still holds exactly one paragraph (so
/// <see cref="Para"/> is the common accessor); P3 enables genuine multi-block cells.</para></summary>
public class TableCell : TextElement
{
    /// <summary>The block-level content of the cell. Never empty (at least one paragraph).</summary>
    public List<Block> Blocks { get; set; } = new();
    /// <summary>The cell background fill brush (was previously stored on the cell's paragraph).</summary>
    public IBrush? Background { get; set; }

    /// <summary>Creates a cell containing a single empty paragraph.</summary>
    public TableCell()
    {
        Blocks.Add(new Paragraph { Inlines = { new Run { Text = "" } } });
    }

    /// <summary>Creates a cell wrapping an existing paragraph.</summary>
    public TableCell(Paragraph paragraph)
    {
        Blocks.Add(paragraph);
    }

    /// <summary>The cell's primary (and, through P1/P2, only) paragraph. Convenience for the many call
    /// sites that still assume one-paragraph cells; replaced by block-list walks in P3+.</summary>
    public Paragraph Para => (Paragraph)Blocks[0];

    /// <inheritdoc/>
    public override TextElement Clone()
    {
        var tc = new TableCell { Background = Background };
        tc.Blocks.Clear();
        foreach (var b in Blocks)
        {
            if (b.Clone() is Block bc)
            {
                bc.Parent = tc;
                tc.Blocks.Add(bc);
            }
        }
        if (tc.Blocks.Count == 0) tc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "" } } });
        return tc;
    }
}
