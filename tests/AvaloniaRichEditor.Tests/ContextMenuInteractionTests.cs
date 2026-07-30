using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The right-click menu was only ever tested by asking the routing helper what it *would* target
// (InlineTableMenuAndResizeTests). These right-click for real: the press has to reach ShowContextMenu,
// the hit position has to survive the trip, and the menu that opens has to carry the matching items.
public class ContextMenuInteractionTests
{
    private static ContextMenu? OpenMenu(RichEditor ed)
        => typeof(RichEditor).GetField("_openContextMenu", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(ed) as ContextMenu;

    private static IEnumerable<string> Headers(ContextMenu menu)
        => (menu.ItemsSource ?? menu.Items)!.OfType<MenuItem>().Select(mi => mi.Header?.ToString() ?? "");

    private static InteractionHost Host(FlowDocument doc)
    {
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        var host = InteractionHost.Create(ed);
        host.Render();
        return host;
    }

    private static FlowDocument TextDoc(string text = "hello world")
    {
        var doc = new FlowDocument();
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = text });
        doc.Blocks.Add(p);
        return doc;
    }

    [AvaloniaFact]
    public void RightClickingTextOpensAMenu()
    {
        var host = Host(TextDoc());

        host.Click(new Point(10, 8), MouseButton.Right);

        var menu = OpenMenu(host.Editor);
        Assert.NotNull(menu);
        Assert.NotEmpty(Headers(menu!));
    }

    // Right-clicking inside a table has to offer table operations — the menu is built from the clicked
    // point, so a lost or mistranslated position silently produces the plain text menu.
    [AvaloniaFact]
    public void RightClickingInsideATableOffersTableOperations()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(2, 2);
        ((Run)tb.Cells[0][0].Para.Inlines[0]).Text = "cell";
        doc.Blocks.Add(tb);
        var host = Host(doc);

        // Well inside the first cell, away from the boundary handles.
        host.Click(new Point(30, 14), MouseButton.Right);

        var menu = OpenMenu(host.Editor);
        Assert.NotNull(menu);
        Assert.Contains(Headers(menu!), h => h.Contains("행") || h.Contains("Row"));
    }

    // A right-click must not move the caret or drop the selection: the menu's Copy/Cut act on it.
    [AvaloniaFact]
    public void RightClickingKeepsTheSelection()
    {
        var host = Host(TextDoc());
        host.Drag(new Point(0, 8), new Point(40, 8));
        string selected = host.SelectedText;
        Assert.NotEqual("", selected); // precondition

        host.Click(new Point(20, 8), MouseButton.Right);

        Assert.Equal(selected, host.SelectedText);
    }

    // Read-only editors get the short menu: no editing commands to offer.
    [AvaloniaFact]
    public void ReadOnlyRightClickOffersOnlyCopyAndSelectAll()
    {
        var host = Host(TextDoc());
        host.Editor.IsReadOnly = true;

        host.Click(new Point(10, 8), MouseButton.Right);

        var menu = OpenMenu(host.Editor);
        Assert.NotNull(menu);
        Assert.Equal(2, Headers(menu!).Count());
    }
}
