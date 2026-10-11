using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A toolbar button must not take focus from the editor: the caret is only painted while the editor is
// focused, so clicking one made the caret vanish and sent the next keystroke to the button. The command
// itself still ran against the remembered caret position, which is why the buttons looked like they
// worked while typing had stopped.
public class ToolbarFocusTests
{
    private static RichEditorToolbar ToolbarFor(RichEditor ed)
    {
        var tb = new RichEditorToolbar { Target = ed };
        tb.Measure(new Avalonia.Size(1200, 60));
        tb.Arrange(new Avalonia.Rect(0, 0, 1200, 60));
        return tb;
    }

    private static System.Collections.Generic.IEnumerable<Button> Buttons(Control root)
    {
        foreach (var c in root.GetLogicalDescendants().OfType<Button>()) yield return c;
    }

    [AvaloniaFact]
    public void EveryToolbarButtonRefusesFocus()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        var toolbar = ToolbarFor(ed);

        var focusable = Buttons(toolbar).Where(b => b.Focusable).ToList();

        Assert.Empty(focusable);
    }

    // Host items carry the same guarantee (class docs). The trailing ones were added after the pass that
    // clears Focusable, so they kept it. Reported and fixed in olivierjc/AvaloniaRichEditor.
    [AvaloniaFact]
    public void HostSuppliedLeadingAndTrailingButtonsRefuseFocus()
    {
        var ed = new RichEditor();
        var lead = new Button { Content = "lead" };
        var trail = new Button { Content = "trail" };
        var tb = new RichEditorToolbar { Target = ed };
        tb.LeadingItems.Add(lead);
        tb.TrailingItems.Add(trail);

        Assert.False(lead.Focusable);
        Assert.False(trail.Focusable);
    }

    [AvaloniaFact]
    public void TheToolbarHasButtonsToCheck()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        var toolbar = ToolbarFor(ed);

        Assert.NotEmpty(Buttons(toolbar)); // guards the test above against silently checking nothing
    }
}
