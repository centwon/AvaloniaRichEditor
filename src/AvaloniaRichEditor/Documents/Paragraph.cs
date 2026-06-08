using System.Collections.Generic;
using Avalonia.Media;

namespace AvaloniaRichEditor.Documents;

public enum ListKind { None, Bullet, Ordered }

public class Paragraph : Block
{
    public List<Inline> Inlines { get; set; } = new();
    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;
    public double LineHeight { get; set; } = double.NaN;
    public double MarginTop { get; set; } = 0;
    public double MarginBottom { get; set; } = 10;
    public ListKind ListType { get; set; } = ListKind.None;
    public int HeadingLevel { get; set; } = 0; // 0 = body text, 1..6 = h1..h6
    public IBrush? Background { get; set; } // paragraph / table-cell background fill
    public bool IsQuote { get; set; } = false; // <blockquote>
    public int ListLevel { get; set; } = 0; // nested list depth (0 = top level)

    // Convenience: any list item (bullet or numbered).
    public bool IsListItem => ListType != ListKind.None;

    public override TextElement Clone()
    {
        var p = new Paragraph
        {
            MarginTop = this.MarginTop,
            MarginBottom = this.MarginBottom,
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
