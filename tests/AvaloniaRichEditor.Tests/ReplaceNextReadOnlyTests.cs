using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Read-only, ReplaceNext moved the selection to the next match and returned true for a replacement that never happened.
// It does nothing and returns false, as in the WinUI control and as ReplaceAll does (2026-11-09).
public class ReplaceNextReadOnlyTests
{
    [AvaloniaFact]
    public void ReplaceNext_DoesNothingWhenReadOnly()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>cat cat cat</p>");
        ed.IsReadOnly = true;
        string before = ed.ToJson();

        Assert.False(ed.ReplaceNext("cat", "dog", matchCase: false));
        Assert.Equal(before, ed.ToJson());
        Assert.False(ed.HasSelection);
    }
}
