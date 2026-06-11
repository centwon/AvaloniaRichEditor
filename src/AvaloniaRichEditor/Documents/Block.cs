namespace AvaloniaRichEditor.Documents;

/// <summary>Abstract base for block-level elements (<see cref="Paragraph"/>, <see cref="TableBlock"/>,
/// <see cref="ImageBlock"/>, <see cref="DividerBlock"/>).</summary>
public abstract class Block : TextElement
{
    /// <summary>Left indent in device-independent pixels. Shifts paragraph text right; shifts
    /// image/table blocks right (used by the "Space before a block" margin feature). Default: 0.</summary>
    public double Indent { get; set; } = 0;
    /// <summary>Top margin in device-independent pixels (gap to the previous block). Default: 0.</summary>
    public double MarginTop { get; set; } = 0;
    /// <summary>Bottom margin in device-independent pixels (gap to the next block). Default: 10
    /// (<see cref="DividerBlock"/> overrides to 0 — its height already includes spacing).</summary>
    public double MarginBottom { get; set; } = 10;
}
