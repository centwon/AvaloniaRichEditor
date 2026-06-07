namespace AvaloniaRichTextBox.Documents;

// A horizontal rule (<hr>): a thin full-width divider line occupying its own block.
public class DividerBlock : Block
{
    public override TextElement Clone() => new DividerBlock();
}
