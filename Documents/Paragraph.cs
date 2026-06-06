using Avalonia.Collections;

namespace AvaloniaRichTextBoxPort.Documents;

public class Paragraph : Block
{
    public AvaloniaList<Inline> Inlines { get; } = new AvaloniaList<Inline>();
}
