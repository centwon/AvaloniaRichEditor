using Avalonia;
using Avalonia.Collections;

namespace AvaloniaRichEditor.Documents;

/// <summary>The root document model: an ordered list of block-level elements
/// (<see cref="Paragraph"/>, <see cref="TableBlock"/>, <see cref="ImageBlock"/>, <see cref="DividerBlock"/>).</summary>
public class FlowDocument : AvaloniaObject
{
    /// <summary>The ordered list of top-level block elements.</summary>
    public AvaloniaList<Block> Blocks { get; } = new AvaloniaList<Block>();

    /// <summary>Creates a deep clone of this document (all blocks and their children are cloned recursively).
    /// Image bytes are reference-shared (not copied) for efficiency.</summary>
    public FlowDocument Clone()
    {
        var doc = new FlowDocument();
        foreach (var block in Blocks)
        {
            var clone = block.Clone() as Block;
            if (clone != null)
            {
                clone.Parent = doc;
                doc.Blocks.Add(clone);
            }
        }
        return doc;
    }
}
