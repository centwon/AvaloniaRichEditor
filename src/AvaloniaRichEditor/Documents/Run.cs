using Avalonia.Media;

namespace AvaloniaRichEditor.Documents;

/// <summary>A contiguous run of text sharing one set of character formatting (font, size, weight,
/// style, color, highlight, decorations, optional hyperlink). The basic inline building block.</summary>
public class Run : Inline
{
    public string? Text { get; set; }
    public FontWeight FontWeight { get; set; } = FontWeight.Normal;
    public FontStyle FontStyle { get; set; } = FontStyle.Normal;
    public IBrush? Foreground { get; set; }
    public IBrush? Background { get; set; }
    public string? FontFamily { get; set; }
    public double FontSize { get; set; } = 14;
    public string? NavigateUri { get; set; }
    public TextDecorationCollection? TextDecorations { get; set; }

    public override TextElement Clone()
    {
        return new Run
        {
            Text = this.Text,
            FontWeight = this.FontWeight,
            FontStyle = this.FontStyle,
            Foreground = this.Foreground,
            Background = this.Background,
            FontFamily = this.FontFamily,
            FontSize = this.FontSize,
            NavigateUri = this.NavigateUri,
            TextDecorations = this.TextDecorations
        };
    }
}
