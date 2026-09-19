using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// AltGr arrives as Ctrl+Alt, so on a layout with AltGr characters the heading shortcuts Ctrl+Alt+1..6 swallowed
// typing — German AltGr+2 (²) and AltGr+3 (³). From the WinUI port (2026-09-19). The key event's KeySymbol is the
// layout's answer; driven through the window with the symbol the layout would carry.
public class AltGrKeyTests
{
    private static InteractionHost Host()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "text" } } });
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        host.Click(new Point(20, 10));
        return host;
    }

    private static int Heading(InteractionHost host) => host.Editor.Document!.Blocks.OfType<Paragraph>().First().HeadingLevel;

    // Control: on a layout without AltGr the chord carries no character, and it is the heading shortcut.
    [AvaloniaFact]
    public void CtrlAlt2_WithoutACharacter_IsTheHeadingShortcut()
    {
        var host = Host();
        host.Key(Key.D2, RawInputModifiers.Control | RawInputModifiers.Alt, string.Empty);
        Assert.Equal(2, Heading(host));
    }

    [AvaloniaFact]
    public void AltGr2_TypingSuperscriptTwo_IsNotAShortcut()
    {
        var host = Host();
        host.Key(Key.D2, RawInputModifiers.Control | RawInputModifiers.Alt, "²");
        Assert.Equal(0, Heading(host));
    }

    [Theory]
    [InlineData(true, true, "²", true)]
    [InlineData(true, true, "", false)]
    [InlineData(true, true, null, false)]
    [InlineData(true, true, "", false)]
    [InlineData(true, false, "a", false)]
    [InlineData(false, true, "a", false)]
    public void IsAltGrTyping(bool ctrl, bool alt, string? symbol, bool expected)
        => Assert.Equal(expected, RichEditor.IsAltGrTyping(ctrl, alt, symbol));
}
