namespace AvaloniaRichTextBoxPort.Documents;

public abstract class Block : TextElement
{
    // Base class for block-level elements like Paragraphs.
    // Left indent in px — for paragraphs it shifts text; for image/table blocks it shifts the block
    // right (used by the "press Space in front of an image/table" margin feature).
    public double Indent { get; set; } = 0;
}
