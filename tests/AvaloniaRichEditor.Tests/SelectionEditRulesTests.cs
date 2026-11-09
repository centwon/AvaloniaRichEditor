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

    // ---- inserting keeps what is selected ----------------------------------------------------------------

    [AvaloniaFact]
    public void InsertTable_OverABackwardsSelection_GoesAtItsEnd()
    {
        // The block went in at the caret — the START of a selection made backwards.
        var p = TestHelpers.Para(new Run { Text = "hello world" });
        var ed = new RichEditor { Document = TestHelpers.Doc(p), PageSize = RichEditorPageSize.Continuous };
        Select(ed, p, 8, p, 3);
        ed.InsertDivider();
        var blocks = ed.Document!.Blocks;
        Assert.Equal("hello wo", ((Paragraph)blocks[0]).Text());
        Assert.IsType<DividerBlock>(blocks[1]);
        Assert.Equal("rld", ((Paragraph)blocks[2]).Text());
    }

    [AvaloniaFact]
    public void InsertTable_WithAPictureSelected_GoesRightAfterIt()
    {
        // With a picture selected the block went in where the caret had been before the click.
        var above = TestHelpers.Para(new Run { Text = "above" });
        var pic = new ImageBlock { Width = 50, Height = 50 };
        var ed = new RichEditor
        {
            Document = TestHelpers.Doc(above, pic, TestHelpers.Para(new Run { Text = "below" })),
            PageSize = RichEditorPageSize.Continuous,
        };
        PlaceCaret(ed, above, 0);
        typeof(RichEditor).GetField("_selectedBlock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(ed, pic);
        ed.InsertTable(2, 2);
        var blocks = ed.Document!.Blocks;
        // above, the picture, (the paragraph normalization keeps between two blocks), the table, below.
        Assert.Equal("above,ImageBlock,P,TableBlock,below",
            string.Join(",", blocks.Select(b => b is Paragraph bp ? (bp.Text().Length > 0 ? bp.Text() : "P") : b.GetType().Name)));
    }

    [AvaloniaFact]
    public void InsertInlineTable_OverABackwardsSelection_GoesAtItsEnd_AndKeepsTheText()
    {
        var p = TestHelpers.Para(new Run { Text = "hello world" });
        var ed = new RichEditor { Document = TestHelpers.Doc(p), PageSize = RichEditorPageSize.Continuous };
        Select(ed, p, 8, p, 3);
        ed.InsertInlineTable(1, 1);
        Assert.Equal("hello world", p.Text());
        int off = 0;
        foreach (var inl in p.Inlines) { if (inl is InlineTable) break; off += inl is Run r ? r.Text.Length : 1; }
        Assert.Equal(8, off);
        Assert.False(ed.HasSelection);
    }

    // ---- several lines of plain text pasted ----------------------------------------------------------------

    private static void PastePlain(RichEditor ed, string text)
        => typeof(RichEditor).GetMethod("InsertPlainText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(ed, new object[] { text });

    [AvaloniaFact]
    public void PlainLines_BecomeParagraphs()
    {
        // They went into one paragraph with line breaks.
        var p = TestHelpers.Para(new Run { Text = "Hello World" });
        var ed = new RichEditor { Document = TestHelpers.Doc(p), PageSize = RichEditorPageSize.Continuous };
        PlaceCaret(ed, p, 6);
        PastePlain(ed, "A1\r\nB2");
        Assert.Equal(new[] { "Hello A1", "B2World" }, ed.Document!.Blocks.OfType<Paragraph>().Select(x => x.Text()));
    }

    [AvaloniaFact]
    public void PlainLines_IntoAListItem_AreListItems_WithTheCaretsCharacterFormat()
    {
        var p = new Paragraph { ListType = ListKind.Bullet };
        p.Inlines.Add(new Run { Text = "item one", FontWeight = Avalonia.Media.FontWeight.Bold });
        var ed = new RichEditor { Document = TestHelpers.Doc(p), PageSize = RichEditorPageSize.Continuous };
        PlaceCaret(ed, p, 4);
        PastePlain(ed, "x\ny\nz");
        var paras = ed.Document!.Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(new[] { "itemx", "y", "z one" }, paras.Select(x => x.Text()));
        Assert.All(paras, x => Assert.Equal(ListKind.Bullet, x.ListType));
        Assert.All(paras.SelectMany(x => x.Inlines.OfType<Run>()), r => Assert.Equal(Avalonia.Media.FontWeight.Bold, r.FontWeight));
    }

    // ---- an inline table selected as an object; cutting a cell block ---------------------------------------

    private static void Type(RichEditor ed, string text)
        => ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });

    private static TableBlock Grid(int rows, int cols)
    {
        var t = new TableBlock(rows, cols);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                ((Run)t.Cells[r][c].Para.Inlines[0]).Text = ((char)('a' + r * cols + c)).ToString();
        return t;
    }

    private static void SelectTableObject(RichEditor ed, TableBlock tb)
        => typeof(RichEditor).GetMethod("SelectTableObject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(ed, new object[] { tb });

    private static (RichEditor ed, Paragraph host, TableBlock t) InlineTableLine()
    {
        var t = Grid(2, 2);
        var host = TestHelpers.Para(new Run { Text = "ab" }, new InlineTable { Table = t }, new Run { Text = "cd" });
        var ed = new RichEditor { Document = TestHelpers.Doc(host), PageSize = RichEditorPageSize.Continuous };
        PlaceCaret(ed, host, 0);
        return (ed, host, t);
    }

    [AvaloniaFact]
    public void AnInlineTableSelectedAsAnObject_TypesAfterIt_AndKeepsItsCells()
    {
        // The border selected all the table's cells as text, so a letter typed then emptied them.
        var (ed, host, t) = InlineTableLine();
        SelectTableObject(ed, t);
        Press(ed, Key.Z);
        Type(ed, "Z");
        Assert.Equal("abZcd", host.Text());
        Assert.Equal("a", t.Cells[0][0].Para.Text());
        Assert.Equal("d", t.Cells[1][1].Para.Text());
    }

    [AvaloniaFact]
    public void AnInlineTableSelectedAsAnObject_DeletesWhole()
    {
        var (ed, host, _) = InlineTableLine();
        SelectTableObject(ed, ((InlineTable)host.Inlines[1]).Table);
        Press(ed, Key.Delete);
        Assert.DoesNotContain(host.Inlines, i => i is InlineTable);
        Assert.Equal("abcd", host.Text());
    }

    [AvaloniaFact]
    public void DeletingAWholeTablesCells_EmptiesThem_AndKeepsTheTable()
    {
        // A staged Ctrl+A's whole-table selection is a cell block: Delete empties it (HWP). It removed the table.
        var t = Grid(2, 2);
        var ed = new RichEditor
        {
            Document = TestHelpers.Doc(TestHelpers.Para(new Run { Text = "before" }), t, TestHelpers.Para(new Run { Text = "after" })),
            PageSize = RichEditorPageSize.Continuous,
        };
        Select(ed, t.Cells[0][0].Para, 0, t.Cells[1][1].Para, 1);
        Press(ed, Key.Delete);
        Assert.Contains(ed.Document!.Blocks, b => ReferenceEquals(b, t));
        Assert.All(new[] { (0, 0), (0, 1), (1, 0), (1, 1) }, rc => Assert.Equal("", t.Cells[rc.Item1][rc.Item2].Para.Text()));
    }

    // Cut used to empty the cells of a part of a table and remove a whole one: whole rows and whole columns now go with
    // the cut too, as on the WinUI control; any other rectangle is still emptied.
    [AvaloniaTheory]
    [InlineData(1, 0, 1, 2, "rows")]
    [InlineData(0, 2, 2, 2, "columns")]
    [InlineData(0, 0, 2, 2, "table")]
    [InlineData(0, 0, 1, 1, "contents")]
    public void CuttingACellBlock_TakesWholeRowsColumnsOrTheTable(int r0, int c0, int r1, int c1, string kind)
    {
        var t = Grid(3, 3);
        var ed = new RichEditor
        {
            Document = TestHelpers.Doc(TestHelpers.Para(new Run { Text = "before" }), t, TestHelpers.Para(new Run { Text = "after" })),
            PageSize = RichEditorPageSize.Continuous,
        };
        Select(ed, t.Cells[r0][c0].Para, 0, t.Cells[r1][c1].Para, 1);
        Press(ed, Key.X, KeyModifiers.Control);
        switch (kind)
        {
            case "rows":
                Assert.Equal(2, t.Rows);
                Assert.Equal("g", t.Cells[1][0].Para.Text());
                break;
            case "columns":
                Assert.Equal(2, t.Columns);
                Assert.Equal("b", t.Cells[0][1].Para.Text());
                break;
            case "table":
                Assert.DoesNotContain(ed.Document!.Blocks, b => b is TableBlock);
                break;
            default:
                Assert.Equal((3, 3), (t.Rows, t.Columns));
                Assert.Equal("", t.Cells[0][0].Para.Text());
                Assert.Equal("", t.Cells[1][1].Para.Text());
                Assert.Equal("c", t.Cells[0][2].Para.Text());
                break;
        }
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
