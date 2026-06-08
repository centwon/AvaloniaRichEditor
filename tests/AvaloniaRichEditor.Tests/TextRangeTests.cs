using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

public class TextRangeTests
{
    [Fact]
    public void GetText_ReturnsSelectedSubstring()
    {
        var p = TestHelpers.Para(new Run { Text = "Hello World" });
        var range = new TextRange(new TextPointer(p, 0), new TextPointer(p, 5));
        Assert.Equal("Hello", range.GetText());
    }

    [Fact]
    public void Delete_RemovesSelectedSubstring()
    {
        var p = TestHelpers.Para(new Run { Text = "Hello World" });
        new TextRange(new TextPointer(p, 0), new TextPointer(p, 6)).Delete();
        Assert.Equal("World", p.Text());
    }

    [Fact]
    public void Delete_EmptyRange_IsNoOp()
    {
        var p = TestHelpers.Para(new Run { Text = "abc" });
        new TextRange(new TextPointer(p, 1), new TextPointer(p, 1)).Delete();
        Assert.Equal("abc", p.Text());
    }

    [Fact]
    public void ApplyPropertyValue_StylesSelectedRuns()
    {
        var p = TestHelpers.Para(new Run { Text = "abc" });
        var range = new TextRange(new TextPointer(p, 0), new TextPointer(p, 3));
        range.ApplyPropertyValue(r => r.FontWeight = Avalonia.Media.FontWeight.Bold);
        Assert.All(System.Linq.Enumerable.OfType<Run>(p.Inlines),
            r => Assert.Equal(Avalonia.Media.FontWeight.Bold, r.FontWeight));
    }
}
