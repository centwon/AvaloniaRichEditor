using Avalonia.Media;

namespace AvaloniaRichEditor.Documents;

/// <summary>A contiguous run of text sharing one set of character formatting (font, size, weight,
/// style, color, highlight, decorations, optional hyperlink). The basic inline building block.</summary>
public class Run : Inline
{
    /// <summary>The text content of this run.</summary>
    public string? Text { get; set; }
    /// <summary>Font weight (e.g. <see cref="FontWeight.Bold"/>). Default: Normal.</summary>
    public FontWeight FontWeight { get; set; } = FontWeight.Normal;
    /// <summary>Font style (e.g. <see cref="FontStyle.Italic"/>). Default: Normal.</summary>
    public FontStyle FontStyle { get; set; } = FontStyle.Normal;
    /// <summary>Foreground text brush. <see langword="null"/> falls back to the editor default.</summary>
    public IBrush? Foreground { get; set; }
    /// <summary>Highlight (background) brush. <see langword="null"/> = none.</summary>
    public IBrush? Background { get; set; }
    /// <summary>Font family name. <see langword="null"/> falls back to <see cref="Controls.RichEditor.DefaultFontFamily"/>.</summary>
    public string? FontFamily { get; set; }
    /// <summary>Font size in device-independent pixels. Default: 14.</summary>
    public double FontSize { get; set; } = 14;
    /// <summary>Hyperlink URL. When non-null the run is rendered as a blue underlined link.</summary>
    public string? NavigateUri { get; set; }
    /// <summary>Text decorations such as underline or strikethrough.</summary>
    public TextDecorationCollection? TextDecorations { get; set; }

    /// <inheritdoc/>
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
            // Copy the collection (not share the reference): an in-place edit to one run's
            // decorations would otherwise also mutate every clone/split tail.
            TextDecorations = this.TextDecorations != null
                ? new TextDecorationCollection(this.TextDecorations)
                : null
        };
    }
}
