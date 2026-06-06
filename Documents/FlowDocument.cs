using Avalonia;
using Avalonia.Collections;

namespace AvaloniaRichTextBoxPort.Documents;

public class FlowDocument : AvaloniaObject
{
    public AvaloniaList<Block> Blocks { get; } = new AvaloniaList<Block>();
}
