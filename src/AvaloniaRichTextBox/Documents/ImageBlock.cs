using Avalonia.Media.Imaging;

namespace AvaloniaRichTextBox.Documents;

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
