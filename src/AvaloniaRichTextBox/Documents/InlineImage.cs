using Avalonia.Media.Imaging;

namespace AvaloniaRichTextBox.Documents;

// An image that flows inline within a paragraph's text (e.g. a small logo/emoji icon).
// It occupies exactly one logical character position in the paragraph (see InlineLen),
// represented in text layouts by the object-replacement character U+FFFC.
public class InlineImage : Inline
{
    public Bitmap? Image { get; set; }
    public double Width { get; set; } = 16;
    public double Height { get; set; } = 16;

    public override TextElement Clone()
    {
        return new InlineImage
        {
            Image = this.Image,
            Width = this.Width,
            Height = this.Height
        };
    }
}
