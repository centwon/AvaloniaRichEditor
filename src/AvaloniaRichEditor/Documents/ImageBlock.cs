using Avalonia.Media.Imaging;

namespace AvaloniaRichEditor.Documents;

/// <summary>A block-level image (its own line/paragraph), as opposed to a small in-line
/// <see cref="InlineImage"/>. Used for larger pictures; supports resize.</summary>
public class ImageBlock : Block
{
    public Bitmap? Image { get; set; }
    public double Width { get; set; } = double.NaN;
    public double Height { get; set; } = double.NaN;

    public override TextElement Clone()
    {
        return new ImageBlock
        {
            Image = this.Image,
            Width = this.Width,
            Height = this.Height,
            Indent = this.Indent
        };
    }
}
