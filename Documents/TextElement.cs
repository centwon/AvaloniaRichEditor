using Avalonia;

namespace AvaloniaRichTextBoxPort.Documents;

public abstract class TextElement : AvaloniaObject
{
    [System.Text.Json.Serialization.JsonIgnore]
    public object? Parent { get; internal set; }

    public abstract TextElement Clone();
}
