using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// ToolbarFocusTests asserts no button is Focusable, which is how the round 3 fix was written. That is a
// property check, not the behaviour: what broke was that clicking a button stole focus, so the caret
// vanished and the next keystroke went to the button. These click for real and then keep typing.
public class ToolbarClickInteractionTests : IDisposable
{
    private readonly List<InteractionHost> _hosts = new();

    // An attached toolbar listens to the static LanguageChanged event, so leaving one in the tree makes a
    // later test that changes the language rebuild it off the UI thread. Detach at the end of each test.
    public void Dispose()
    {
        foreach (var h in _hosts) h.Dispose();
    }

    private (InteractionHost host, RichEditorToolbar toolbar) Setup(string html = "<p>abc</p>")
    {
        var ed = new RichEditor { PageSize = RichEditorPageSize.Continuous };
        ed.LoadHtml(html);
        var pair = InteractionHost.CreateWithToolbar(ed);
        _hosts.Add(pair.host);
        pair.host.Click(new Point(0, 8)); // caret into the text, as the user would before using a button
        return pair;
    }

    private static Button Bold(RichEditorToolbar toolbar)
        => (Button)typeof(RichEditorToolbar)
            .GetField("_boldBtn", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(toolbar)!;

    private static IEnumerable<Button> Buttons(RichEditorToolbar toolbar)
        => toolbar.GetLogicalDescendants().OfType<Button>();

    [AvaloniaFact]
    public void ClickingAToolbarButtonLeavesFocusInTheEditor()
    {
        var (host, toolbar) = Setup();

        host.ClickControl(Bold(toolbar));

        Assert.True(host.EditorHasFocus);
        Assert.Same(host.Editor, host.FocusedElement);
    }

    // The consequence the user actually saw: typing stopped going into the document.
    [AvaloniaFact]
    public void TypingStillReachesTheDocumentAfterAToolbarClick()
    {
        var (host, toolbar) = Setup();

        host.ClickControl(Bold(toolbar));
        host.Type("X");

        Assert.Equal("Xabc", ((Paragraph)host.Editor.Document!.Blocks[0]).Text());
    }

    // ...and the command still has to run, bold applying to what gets typed next.
    [AvaloniaFact]
    public void AToolbarClickStillRunsItsCommand()
    {
        var (host, toolbar) = Setup();

        host.ClickControl(Bold(toolbar));
        host.Type("X");

        var run = ((Paragraph)host.Editor.Document!.Blocks[0]).Inlines.OfType<Run>().First();
        Assert.Equal(Avalonia.Media.FontWeight.Bold, run.FontWeight);
    }

    // Every flyout the strip owns: the ones hung on a button, plus the pickers that attach theirs to the
    // surrounding box (lists, line spacing).
    private static IEnumerable<FlyoutBase> Flyouts(RichEditorToolbar toolbar)
        => toolbar.GetLogicalDescendants().OfType<Control>()
            .Select(c => (c as Button)?.Flyout ?? FlyoutBase.GetAttachedFlyout(c))
            .OfType<FlyoutBase>()
            .Distinct();

    // Every button, not just the one this file pokes at: one button that swallows focus is enough to lose
    // the caret, and the toolbar gains buttons over time. A picker button legitimately hands focus to its
    // popup while that is open — for those the guarantee is that closing the popup gives focus back.
    [AvaloniaFact]
    public void NoToolbarButtonLeavesFocusOutsideTheEditor()
    {
        var (host, toolbar) = Setup();

        // Only the buttons that are laid out can be clicked; a collapsed one has no position.
        var clickable = Buttons(toolbar).Where(b => b.Bounds.Width > 0 && b.Bounds.Height > 0).ToList();
        Assert.NotEmpty(clickable); // guard against silently clicking nothing

        foreach (var b in clickable)
        {
            host.ClickControl(b);

            var opened = Flyouts(toolbar).Where(f => f.IsOpen).ToList();
            foreach (var f in opened) f.Hide();
            host.Pump();

            Assert.True(host.EditorHasFocus,
                $"focus stayed outside the editor after clicking '{ToolTip.GetTip(b)}'"
                + $" (popups opened: {opened.Count}) -> {host.FocusedElement}");
        }
    }

    // The picker popups take focus while open, which is fine; what broke the caret was never getting it
    // back. Opening and closing each one has to leave the editor focused and typing working.
    [AvaloniaFact]
    public void ClosingAPickerPopupReturnsFocusToTheEditor()
    {
        var (host, toolbar) = Setup();
        var flyouts = Flyouts(toolbar).ToList();
        Assert.NotEmpty(flyouts);

        foreach (var f in flyouts)
        {
            f.ShowAt(toolbar);
            host.Pump();
            f.Hide();
            host.Pump();
            Assert.True(host.EditorHasFocus, $"focus never came back after a {f.GetType().Name} closed");
        }

        host.Type("Z");
        Assert.StartsWith("Z", ((Paragraph)host.Editor.Document!.Blocks[0]).Text());
    }
}
