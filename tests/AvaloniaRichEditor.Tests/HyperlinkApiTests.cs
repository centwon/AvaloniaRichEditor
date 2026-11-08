using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The public link API (PR #87): a host builds its own link UI — a toolbar button, a menu on a clicked link —
// with what the built-in context menu uses internally.
public class HyperlinkApiTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Url = "https://example.com/";

    private static RichEditor Editor(RichEditorViewMode mode = RichEditorViewMode.Continuous)
    {
        var doc = TestHelpers.Doc(
            TestHelpers.Para(new Run { Text = "link", NavigateUri = Url }, new Run { Text = " and plain text" }));
        var ed = new RichEditor { ViewMode = mode, Document = doc };
        if (mode == RichEditorViewMode.Page) ed.PageSize = RichEditorPageSize.A4; // the default size does not paginate
        ed.Measure(new Size(700, 900));
        ed.Arrange(new Rect(0, 0, 700, 900));
        return ed;
    }

    private static Paragraph First(RichEditor ed) => ed.Document!.Blocks.OfType<Paragraph>().First();

    private static void Select(RichEditor ed, int from, int to)
    {
        var p = First(ed);
        typeof(RichEditor).GetField("_selectionStart", NP)!.SetValue(ed, new TextPointer(p, from));
        typeof(RichEditor).GetField("_selectionEnd", NP)!.SetValue(ed, new TextPointer(p, to));
        typeof(RichEditor).GetField("_caretPosition", NP)!.SetValue(ed, new TextPointer(p, to));
    }

    // A point on the link in the editor's own space (what e.GetPosition(editor) gives a host): found in
    // document space with the internal hit test, then mapped to the view the way pointer input arrives.
    private static Point PointOnLink(RichEditor ed)
    {
        var hit = typeof(RichEditor).GetMethod("GetLinkRunAtPoint", NP)!;
        for (double y = 0; y < 400; y += 2)
            for (double x = 0; x < 300; x += 2)
                if (hit.Invoke(ed, new object[] { new Point(x, y) }) != null)
                    return ed.MapDocToView(new Point(x + 4, y + 4));
        throw new Xunit.Sdk.XunitException("no link found");
    }

    [AvaloniaFact]
    public void HasSelection_IsFalseForACaret_AndTrueForARange()
    {
        var ed = Editor();
        Select(ed, 6, 6);
        Assert.False(ed.HasSelection);
        Select(ed, 5, 8);
        Assert.True(ed.HasSelection);
    }

    [AvaloniaFact]
    public void SetHyperlink_LinksTheSelection_Trimmed_AndBlankRemovesIt()
    {
        var ed = Editor();
        Select(ed, 9, 14); // "plain"

        ed.SetHyperlink("  https://example.org/ "); // trimmed, like the WinUI port
        Assert.Equal("https://example.org/", First(ed).Inlines.OfType<Run>().Single(r => r.Text == "plain").NavigateUri);

        ed.SetHyperlink("  "); // blank removes it; the unlinked run may merge back into its neighbour, so check by address
        Assert.DoesNotContain(First(ed).Inlines.OfType<Run>(), r => r.NavigateUri is { Length: > 0 } u && u != Url);
    }

    [AvaloniaFact]
    public void SetHyperlink_DoesNothing_WhenReadOnly()
    {
        var ed = Editor();
        Select(ed, 9, 14);
        ed.IsReadOnly = true;

        ed.SetHyperlink("https://example.org/");

        Assert.DoesNotContain(First(ed).Inlines.OfType<Run>(), r => r.NavigateUri == "https://example.org/");
    }

    // Page view puts page gaps and margins between the view and the document, so a point the host takes
    // from a pointer event must be mapped before the hit test.
    [AvaloniaTheory]
    [InlineData(RichEditorViewMode.Continuous)]
    [InlineData(RichEditorViewMode.Page)]
    public void GetHyperlinkAt_TakesAPointInTheEditorsOwnSpace(RichEditorViewMode mode)
    {
        var ed = Editor(mode);
        Assert.Equal(Url, ed.GetHyperlinkAt(PointOnLink(ed)));
        Assert.Null(ed.GetHyperlinkAt(new Point(5, 850)));
    }

    [AvaloniaFact]
    public void TrySetHyperlinkAt_ChangesOnlyTheClickedLink_KeepsTheSelection_AsOneUndoStep()
    {
        var ed = Editor(RichEditorViewMode.Page);
        Select(ed, 9, 14);
        var point = PointOnLink(ed);

        Assert.True(ed.TrySetHyperlinkAt(point, "  https://example.org/ ")); // trimmed

        var runs = First(ed).Inlines.OfType<Run>().ToList();
        Assert.Equal("https://example.org/", runs.Single(r => r.Text == "link").NavigateUri);
        Assert.All(runs.Where(r => r.Text != "link"), r => Assert.Null(r.NavigateUri));
        Assert.True(ed.HasSelection); // the host's selection is left alone

        ed.Undo();
        Assert.Equal(Url, First(ed).Inlines.OfType<Run>().Single(r => r.Text == "link").NavigateUri);
    }

    [AvaloniaFact]
    public void TrySetHyperlinkAt_BlankRemovesTheLink_AndReportsNoLinkOrReadOnly()
    {
        var ed = Editor();
        var point = PointOnLink(ed);

        Assert.False(ed.TrySetHyperlinkAt(new Point(5, 850), "https://example.org/")); // no link there

        ed.IsReadOnly = true;
        Assert.False(ed.TrySetHyperlinkAt(point, null));
        Assert.Equal(Url, First(ed).Inlines.OfType<Run>().Single(r => r.Text == "link").NavigateUri);

        ed.IsReadOnly = false;
        Assert.True(ed.TrySetHyperlinkAt(point, " "));
        Assert.Null(First(ed).Inlines.OfType<Run>().Single(r => r.Text == "link").NavigateUri);
        Assert.Null(ed.GetHyperlinkAt(point));
    }
}
