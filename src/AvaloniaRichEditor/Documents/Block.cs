namespace AvaloniaRichEditor.Documents;

/// <summary>Abstract base for block-level elements (<see cref="Paragraph"/>, <see cref="TableBlock"/>,
/// <see cref="ImageBlock"/>, <see cref="DividerBlock"/>).</summary>
public abstract class Block : TextElement
{
    /// <summary>Left indent in device-independent pixels. Shifts paragraph text right; shifts
    /// image/table blocks right (used by the "Space before a block" margin feature). Default: 0.</summary>
    public double Indent { get; set; } = 0;
}
