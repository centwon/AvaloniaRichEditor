using Avalonia;
using Avalonia.Media;

namespace AvaloniaRichTextBoxPort.Documents;

public class Run : Inline
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<Run, string>(nameof(Text), string.Empty);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<Run, FontWeight>(nameof(FontWeight), FontWeight.Normal);

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<Run, IBrush>(nameof(Foreground), Brushes.Black);

    public IBrush Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }
}
