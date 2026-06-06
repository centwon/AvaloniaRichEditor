using Avalonia;
using Avalonia.Collections;

namespace AvaloniaRichTextBoxPort.Documents;

public class FlowDocument : AvaloniaObject
{
    public AvaloniaList<Block> Blocks { get; } = new AvaloniaList<Block>();

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
