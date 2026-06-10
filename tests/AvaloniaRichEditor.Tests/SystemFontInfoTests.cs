using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests;

public class SystemFontInfoTests
{
    [Fact]
    public void MessageFontFaceName_OnWindows_ReturnsAFace()
    {
        var name = SystemFontInfo.MessageFontFaceName();
        if (System.OperatingSystem.IsWindows())
            Assert.False(string.IsNullOrWhiteSpace(name));
        else
            Assert.Null(name);
    }
}
