namespace AvaloniaRichEditor.Documents;

/// <summary>A horizontal rule (<c>&lt;hr&gt;</c>): a thin full-width divider line occupying its own block.</summary>
public class DividerBlock : Block
{
    /// <inheritdoc/>
    public override TextElement Clone() => new DividerBlock();
}
