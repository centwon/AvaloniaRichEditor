using System.Collections.Generic;
using Avalonia.Media;

namespace AvaloniaRichTextBoxPort.Documents;

public class Paragraph : Block
{
    public List<Inline> Inlines { get; set; } = new();
    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;
    public double LineHeight { get; set; } = double.NaN;
    public double MarginTop { get; set; } = 0;
    public double MarginBottom { get; set; } = 10;
    public bool IsListItem { get; set; } = false;

    public override TextElement Clone()
    {
        var p = new Paragraph
        {
            MarginTop = this.MarginTop,
            MarginBottom = this.MarginBottom,
            TextAlignment = this.TextAlignment,
            LineHeight = this.LineHeight,
            IsListItem = this.IsListItem
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
