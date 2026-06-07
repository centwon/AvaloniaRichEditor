using Avalonia.Media.Imaging;

namespace AvaloniaRichTextBoxPort.Documents;

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
