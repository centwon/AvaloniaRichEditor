using System.Collections.Generic;
using Avalonia.Media;

namespace AvaloniaRichEditor.Documents;

/// <summary>List style for a <see cref="Paragraph"/>.</summary>
public enum ListKind
{
    /// <summary>Not a list item.</summary>
    None,
    /// <summary>Unordered bullet list.</summary>
    Bullet,
    /// <summary>Ordered (numbered) list.</summary>
    Ordered
}

/// <summary>A block of inline content (<see cref="Run"/>s and <see cref="InlineImage"/>s) with
/// paragraph-level formatting: alignment, line spacing, indent, list style, heading level, background.
/// Also serves as the content of a table cell.</summary>
public class Paragraph : Block
{
    /// <summary>The inline elements (runs and inline images) that make up the paragraph.</summary>
    public List<Inline> Inlines { get; set; } = new();
    /// <summary>Horizontal text alignment. Default: Left.</summary>
    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;
    /// <summary>Line height multiplier. <see cref="double.NaN"/> = single spacing (default).</summary>
    public double LineHeight { get; set; } = double.NaN;
    /// <summary>Right margin in device-independent pixels — narrows the wrap width. Paragraph-only:
    /// nothing flows around images/tables, so a right margin would be invisible there
    /// (the left margin is <see cref="Block.Indent"/>). Default: 0.</summary>
    public double MarginRight { get; set; } = 0;
    /// <summary>List style for this paragraph. Default: None.</summary>
    public ListKind ListType { get; set; } = ListKind.None;
    /// <summary>Heading level: 0 = body text, 1–6 = h1–h6.</summary>
    public int HeadingLevel { get; set; } = 0;
    /// <summary>Paragraph or table-cell background fill brush.</summary>
    public IBrush? Background { get; set; }
    /// <summary>Whether this paragraph is a blockquote.</summary>
    public bool IsQuote { get; set; } = false;
    /// <summary>Nested list depth (0 = top level).</summary>
    public int ListLevel { get; set; } = 0;

    /// <summary><see langword="true"/> if this paragraph is a bullet or numbered list item.</summary>
    public bool IsListItem => ListType != ListKind.None;

    /// <inheritdoc/>
    public override TextElement Clone()
    {
        var p = new Paragraph
        {
            MarginTop = this.MarginTop,
            MarginBottom = this.MarginBottom,
            MarginRight = this.MarginRight,
            TextAlignment = this.TextAlignment,
            LineHeight = this.LineHeight,
            ListType = this.ListType,
            HeadingLevel = this.HeadingLevel,
            Background = this.Background,
            Indent = this.Indent,
            IsQuote = this.IsQuote,
            ListLevel = this.ListLevel
        };
        foreach (var inline in Inlines)
        {
            var inlineClone = inline.Clone() as Inline;
            if (inlineClone != null)
            {
                inlineClone.Parent = p;
                p.Inlines.Add(inlineClone);
            }
        }
        return p;
    }
}
