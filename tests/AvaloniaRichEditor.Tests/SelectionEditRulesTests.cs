using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Selection and editing rules made the same as the WinUI control's (2026-11-09).
public class SelectionEditRulesTests
{
    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static void SetField(RichEditor ed, string name, TextPointer tp)
        => typeof(RichEditor).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(ed, tp);

    // Anchor at (from, fromOff), caret and active end at (to, toOff).
    private static void Select(RichEditor ed, Paragraph from, int fromOff, Paragraph to, int toOff)
    {
        SetField(ed, "_selectionStart", new TextPointer(from, fromOff));
        SetField(ed, "_selectionEnd", new TextPointer(to, toOff));
        SetField(ed, "_caretPosition", new TextPointer(to, toOff));
    }

    private static void PlaceCaret(RichEditor ed, Paragraph p, int off) => Select(ed, p, off, p, off);

    private static (RichEditor ed, Paragraph a, Paragraph b) TwoParagraphs()
    {
        var a = TestHelpers.Para(new Run { Text = "first" });
        var b = TestHelpers.Para(new Run { Text = "second" });
        var ed = new RichEditor { Document = TestHelpers.Doc(a, b), PageSize = RichEditorPageSize.Continuous };
        return (ed, a, b);
    }

    // ---- list toggle: one rule ----------------------------------------------------------------------

    [AvaloniaFact]
    public void ListToggle_OnAMixedSelection_TurnsEveryParagraphOn()
    {
        // The caret's paragraph used to decide: with the caret in the bulleted one, the toggle took the bullets OFF
        // both. A mixed selection turns the list on everywhere; only an all-bulleted selection turns it off.
        var (ed, a, b) = TwoParagraphs();
        a.ListType = ListKind.Bullet;
        Select(ed, b, 2, a, 2);
        ed.ToggleBullet();
        Assert.Equal(ListKind.Bullet, ((Paragraph)ed.Document!.Blocks[0]).ListType);
        Assert.Equal(ListKind.Bullet, ((Paragraph)ed.Document.Blocks[1]).ListType);

        ed.ToggleBullet();
        Assert.All(ed.Document.Blocks.OfType<Paragraph>(), p => Assert.Equal(ListKind.None, p.ListType));
    }

    [AvaloniaFact]
    public void ListToggle_AcrossATable_ListsTheCellParagraphsToo()
    {
        var a = TestHelpers.Para(new Run { Text = "before" });
        var tb = new TableBlock(1, 1);
        var cellPara = tb.Cells[0][0].Para;
        ((Run)cellPara.Inlines[0]).Text = "cell";
        var b = TestHelpers.Para(new Run { Text = "after" });
        var ed = new RichEditor { Document = TestHelpers.Doc(a, tb, b), PageSize = RichEditorPageSize.Continuous };
        Select(ed, a, 0, b, 2);
        ed.ToggleBullet();
        Assert.Equal(ListKind.Bullet, cellPara.ListType);
        Assert.All(ed.Document!.Blocks.OfType<Paragraph>(), p => Assert.Equal(ListKind.Bullet, p.ListType));
    }

    [AvaloniaFact]
    public void ListToggle_Off_ClearsTheLevelAndTheMarker()
    {
        // Turning off cleared only the kind: the level kept indenting the paragraph with no marker to explain it.
        var (ed, a, _) = TwoParagraphs();
        a.ListType = ListKind.Bullet;
        a.ListLevel = 2;
        a.ListMarker = ListMarkerStyle.Square;
        PlaceCaret(ed, a, 1);
        ed.ToggleBullet();
        Assert.Equal(ListKind.None, a.ListType);
        Assert.Equal(0, a.ListLevel);
        Assert.Equal(ListMarkerStyle.Default, a.ListMarker);
    }
}
