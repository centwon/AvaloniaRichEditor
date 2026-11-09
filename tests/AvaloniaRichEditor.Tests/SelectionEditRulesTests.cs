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

    // ---- Tab over paragraphs ------------------------------------------------------------------------

    [AvaloniaFact]
    public void Tab_OverSeveralParagraphs_IndentsEachAndKeepsTheText()
    {
        // The key typed four spaces over the selection, deleting it. Now each paragraph steps in: a list item a level,
        // any other an indent step; Shift+Tab steps them back.
        var (ed, a, b) = TwoParagraphs();
        a.ListType = ListKind.Bullet;
        Select(ed, a, 1, b, 3);
        Press(ed, Key.Tab);
        Assert.Equal("first", a.Text());
        Assert.Equal("second", b.Text());
        Assert.Equal(1, a.ListLevel);
        Assert.Equal(0, a.Indent);
        Assert.Equal(20, b.Indent);

        Press(ed, Key.Tab, KeyModifiers.Shift);
        Assert.Equal(0, a.ListLevel);
        Assert.Equal(0, b.Indent);
        Assert.Equal("first", a.Text());
    }

    [AvaloniaFact]
    public void Tab_OnAListItem_StepsItsLevel()
    {
        var (ed, a, _) = TwoParagraphs();
        a.ListType = ListKind.Ordered;
        PlaceCaret(ed, a, 2);
        Press(ed, Key.Tab);
        Assert.Equal("first", a.Text());
        Assert.Equal(1, a.ListLevel);
        Press(ed, Key.Tab, KeyModifiers.Shift);
        Assert.Equal(0, a.ListLevel);
    }

    // ---- "treat as character" inside a cell -----------------------------------------------------------

    private static (RichEditor ed, TableCell cell, Paragraph a) CellHolding(Block inner)
    {
        var outer = new TableBlock(1, 1);
        var cell = outer.Cells[0][0];
        var a = cell.Para;
        ((Run)a.Inlines[0]).Text = "cell";
        cell.Blocks.Add(inner);
        cell.Blocks.Add(TestHelpers.Para(new Run { Text = "" }));
        var ed = new RichEditor
        {
            Document = TestHelpers.Doc(TestHelpers.Para(new Run { Text = "top" }), outer, TestHelpers.Para(new Run { Text = "end" })),
            PageSize = RichEditorPageSize.Continuous,
        };
        return (ed, cell, a);
    }

    [AvaloniaFact]
    public void ATableInACell_ConvertsToInline_WithinItsCell_AndBack()
    {
        var nested = new TableBlock(1, 1);
        var (ed, cell, a) = CellHolding(nested);
        ed.ConvertTableBlockToInline(nested);
        Assert.DoesNotContain(cell.Blocks, x => x is TableBlock);
        var it = Assert.Single(a.Inlines.OfType<InlineTable>());

        ed.ConvertInlineTableToBlock(a, it);
        Assert.DoesNotContain(a.Inlines, i => i is InlineTable);
        Assert.Single(cell.Blocks.OfType<TableBlock>());
    }

    [AvaloniaFact]
    public void APictureInACell_ConvertsToInline_WithinItsCell_CappedToTheCell()
    {
        // The conversion was top-level only; and the inline kept the block's size, wider than the cell it joins.
        var ib = new ImageBlock { Width = 400, Height = 200 };
        var (ed, cell, a) = CellHolding(ib);
        ed.ConvertImageBlockToInline(ib);
        Assert.DoesNotContain(cell.Blocks, x => x is ImageBlock);
        var im = Assert.Single(a.Inlines.OfType<InlineImage>());
        Assert.True(im.Width <= 240, $"width {im.Width}");
        Assert.Equal(im.Width / 2, im.Height, 3);

        ed.ConvertInlineImageToBlock(a, im);
        Assert.Single(cell.Blocks.OfType<ImageBlock>());
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
