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
    // The label the product itself would put on a row command, in whatever language the run picked up
    // from the OS. Comparing against this keeps the test language-independent AND exact.
    private static string RowMenuLabel => RichEditorLocalization.GetString("InsertRowAbove");

    private static ContextMenu? OpenMenu(RichEditor ed)
        => typeof(RichEditor).GetField("_openContextMenu", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(ed) as ContextMenu;

    private static IEnumerable<string> Headers(ContextMenu menu)
        => (menu.ItemsSource ?? menu.Items)!.OfType<MenuItem>().Select(mi => mi.Header?.ToString() ?? "");

    // Every header in the menu, submenus included. Right-clicking inside a cell shows the ordinary text
    // menu with row/column operations grouped under a "Table" submenu (0.8.0), so a top-level-only walk
    // cannot see them.
    private static IEnumerable<string> AllHeaders(ContextMenu menu)
    {
        IEnumerable<string> Walk(IEnumerable<object?> items)
        {
            foreach (var mi in items.OfType<MenuItem>())
            {
                yield return mi.Header?.ToString() ?? "";
                foreach (var nested in Walk((mi.ItemsSource ?? mi.Items)!.Cast<object?>()))
                    yield return nested;
            }
        }
        return Walk((menu.ItemsSource ?? menu.Items)!.Cast<object?>());
    }

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

        // Derive the point from the geometry the renderer actually produced, never a hardcoded y.
        // NormalizeBlocks puts a paragraph in front of the table, so the table's top depends on the
        // default font's line height — which differs per machine (Malgun Gothic here, Segoe UI on a
        // Windows CI runner, another fallback on macOS). A fixed y landed in that leading paragraph on
        // CI and produced the plain text menu, on every OS but the author's.
        var row0 = host.RowHandles.First(r => ReferenceEquals(r.tb, tb) && r.rowIndex == 0);
        var insideFirstCell = new Point(row0.rect.Left + 20, row0.rect.Center.Y - row0.height / 2);
        host.Click(insideFirstCell, MouseButton.Right);

        var menu = OpenMenu(host.Editor);
        Assert.NotNull(menu);
        // Submenus included: a click inside a cell gets the text menu with the table operations
        // grouped under "Table", so a top-level-only walk cannot see them.
        //
        // Match the exact localized string rather than a fragment. The original check was
        // `h.Contains("행") || h.Contains("Row")` — and in Korean "실행 취소" (Undo) and "다시 실행"
        // (Redo) both contain 행, so it matched every menu ever built and tested nothing on the
        // author's machine. It only became a real assertion on an English CI runner, which is where
        // it finally failed.
        Assert.Contains(RowMenuLabel, AllHeaders(menu!));
    }

    // Control for the test above: the same recursive search must NOT find row operations when the
    // click was not in a table, or that assertion would hold no matter what the menu contained.
    [AvaloniaFact]
    public void RightClickingPlainTextOffersNoTableOperations()
    {
        var host = Host(TextDoc());

        host.Click(new Point(10, 8), MouseButton.Right);

        var menu = OpenMenu(host.Editor);
        Assert.NotNull(menu);
        Assert.DoesNotContain(RowMenuLabel, AllHeaders(menu!));
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

    // A decodable 1x1 PNG, as elsewhere in the interaction suites.
    private static readonly byte[] Png = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // The image is made deliberately large and preceded by one short paragraph, so a click at
    // ImagePoint is inside it by construction — the tests below assert which MENU came up, and that
    // assertion is only meaningful if the click actually landed on the picture.
    private static readonly Point ImagePoint = new(80, 120);

    private static InteractionHost HostWithImage(out ImageBlock image)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "above" } } });
        var img = new ImageBlock { Width = 300, Height = 300 };
        img.SetImageData(Png, "image/png");
        doc.Blocks.Add(img);
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "below" } } });
        image = img;
        return Host(doc);
    }

    // Right-clicking an IMAGE in a viewer offers the image's own Copy — the generic read-only menu's
    // Copy acts on the text selection, which is empty when you have just right-clicked a picture, so a
    // reader who wanted the image had no way to get it. Matches the WinUI peer, where the object menu
    // wins over the read-only branch and gates its editing verbs internally.
    [AvaloniaFact]
    public void ReadOnlyRightClickOnAnImage_OffersTheImagesOwnCopy()
    {
        var host = HostWithImage(out _);
        host.Editor.IsReadOnly = true;

        host.Click(ImagePoint, MouseButton.Right);

        var menu = OpenMenu(host.Editor);
        Assert.NotNull(menu);
        var headers = Headers(menu!).ToList();
        Assert.Equal(new[] { RichEditorLocalization.GetString("Copy") }, headers);
    }

    // …and NOTHING that would change it. Every verb below Copy in the image menu mutates the image, so a
    // viewer offering any of them would be a worse regression than the gap this closed.
    //
    // This one does NOT discriminate the routing change — before it, a read-only right-click got the
    // generic two-item menu, which has no editing verbs either. It guards the OTHER half: the
    // `if (IsReadOnly) return;` inside BuildImageMenu. Removing that alone fails this test and leaves the
    // one above passing, which is exactly the split the two are meant to cover.
    [AvaloniaFact]
    public void ReadOnlyRightClickOnAnImage_OffersNoEditingVerb()
    {
        var host = HostWithImage(out _);
        host.Editor.IsReadOnly = true;

        host.Click(ImagePoint, MouseButton.Right);

        var all = AllHeaders(OpenMenu(host.Editor)!).ToList();
        foreach (var key in new[] { "ImageSize", "InlineWithText", "ReplaceImage", "SaveImageAs", "Delete", "Margin" })
            Assert.DoesNotContain(RichEditorLocalization.GetString(key), all);
    }

    // The editable case is unchanged: the same click still opens the full image menu.
    [AvaloniaFact]
    public void EditableRightClickOnAnImage_StillOffersTheEditingVerbs()
    {
        var host = HostWithImage(out _);

        host.Click(ImagePoint, MouseButton.Right);

        var all = AllHeaders(OpenMenu(host.Editor)!).ToList();
        Assert.Contains(RichEditorLocalization.GetString("ImageSize"), all);
        Assert.Contains(RichEditorLocalization.GetString("Delete"), all);
    }
}
