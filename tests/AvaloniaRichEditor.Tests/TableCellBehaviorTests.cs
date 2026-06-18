using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// P0 safety net for milestone A ("blocks in cells"). These lock the *observable* behavior of the
// current single-paragraph-per-cell model so the P1 model swap (Paragraph -> TableCell, still one
// block per cell) can be proven behavior-preserving: cell grid text + cell background survive
// serialization, Tab moves between cells (Parent-chain dependent), and cell content drives table
// height. They assert through stable APIs (JSON/control) so only the cell-reference call sites need
// updating when P1 lands.
public class TableCellBehaviorTests
{
    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static readonly FieldInfo CaretPositionField =
        typeof(RichEditor).GetField("_caretPosition", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static void PlaceCaret(RichEditor ed, Paragraph p, int offset)
    {
        CaretPositionField.SetValue(ed, new TextPointer(p, offset));
        typeof(RichEditor).GetField("_selectionStart", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ed, new TextPointer(p, offset));
        typeof(RichEditor).GetField("_selectionEnd", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ed, new TextPointer(p, offset));
    }

    private static Paragraph CaretCell(RichEditor ed)
        => ((TextPointer)CaretPositionField.GetValue(ed)!).Paragraph!;

    private static void Realize(RichEditor ed, double width = 800)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    // ---- Serialization: cell grid text + per-cell background ---------------

    [Fact]
    public void Json_RoundTrip_PreservesCellGridTextAndBackground()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(2, 2);
        SetCellText(tb, 0, 0, "TL");
        SetCellText(tb, 0, 1, "TR");
        SetCellText(tb, 1, 0, "BL");
        SetCellText(tb, 1, 1, "BR");
        tb.Cells[0][1].Background = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x99));
        doc.Blocks.Add(tb);

        var json = DocumentSerializer.Serialize(doc);
        var doc2 = DocumentSerializer.Deserialize(json);

        var tb2 = doc2.Blocks.OfType<TableBlock>().Single();
        Assert.Equal("TL", tb2.Cells[0][0].Para.Text());
        Assert.Equal("TR", tb2.Cells[0][1].Para.Text());
        Assert.Equal("BL", tb2.Cells[1][0].Para.Text());
        Assert.Equal("BR", tb2.Cells[1][1].Para.Text());

        // The serializer rebuilds brushes as ImmutableSolidColorBrush (thread-affinity-free), so assert
        // through ISolidColorBrush rather than the concrete type.
        var bg = Assert.IsAssignableFrom<ISolidColorBrush>(tb2.Cells[0][1].Background);
        Assert.Equal(Color.FromRgb(0x33, 0x66, 0x99), bg.Color);
        Assert.Null(tb2.Cells[0][0].Background);
    }

    // ---- Tab navigation between cells (Parent-chain dependent) -------------

    [AvaloniaFact]
    public void Tab_MovesCaretToNextCell_ShiftTabMovesBack()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<table><tr><td>a</td><td>b</td></tr></table>");
        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();

        PlaceCaret(ed, tb.Cells[0][0].Para, 0);
        Press(ed, Key.Tab);
        Assert.Same(tb.Cells[0][1].Para, CaretCell(ed));

        Press(ed, Key.Tab, KeyModifiers.Shift);
        Assert.Same(tb.Cells[0][0].Para, CaretCell(ed));
    }

    [AvaloniaFact]
    public void Tab_InLastCell_AddsRow()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<table><tr><td>a</td><td>b</td></tr></table>");
        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();
        Assert.Equal(1, tb.Rows);

        PlaceCaret(ed, tb.Cells[0][1].Para, 1); // last cell
        Press(ed, Key.Tab);

        Assert.Equal(2, tb.Rows);
    }

    private const string Png1x1 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    // ---- P4-2a: a block image inside a cell --------------------------------

    [AvaloniaFact]
    public void InsertImage_WithCaretInCell_AddsImageBlockToCell()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<table><tr><td>x</td></tr></table>");
        var cell = ed.Document!.Blocks.OfType<TableBlock>().Single().Cells[0][0];
        PlaceCaret(ed, cell.Para, 1);
        ed.InsertImageBytes(Convert.FromBase64String(Png1x1));
        Assert.Contains(cell.Blocks, b => b is ImageBlock); // image landed inside the cell, not as a table sibling
    }

    [Fact]
    public void Json_RoundTrip_PreservesBlockImageInCell()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 1);
        var cell = tb.Cells[0][0];
        var img = new ImageBlock { Width = 40, Height = 30 };
        img.SetImageData(Convert.FromBase64String(Png1x1), "image/png");
        cell.Blocks.Add(img);
        doc.Blocks.Add(tb);

        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));
        var cell2 = doc2.Blocks.OfType<TableBlock>().Single().Cells[0][0];
        Assert.Contains(cell2.Blocks, b => b is ImageBlock);
    }

    // ---- P4: serialization of multi-block cells ----------------------------

    [Fact]
    public void Json_RoundTrip_PreservesMultiParagraphCell()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 1);
        var cell = tb.Cells[0][0];
        cell.Blocks.Clear();
        cell.Blocks.Add(Para(new Run { Text = "line1" }));
        cell.Blocks.Add(Para(new Run { Text = "line2" }));
        doc.Blocks.Add(tb);

        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));
        var cell2 = doc2.Blocks.OfType<TableBlock>().Single().Cells[0][0];
        Assert.Equal(2, cell2.Blocks.Count);
        Assert.Equal("line1", ((Paragraph)cell2.Blocks[0]).Text());
        Assert.Equal("line2", ((Paragraph)cell2.Blocks[1]).Text());
    }

    [Fact]
    public void Json_PlainOneParagraphCell_StaysLegacyFormat()
    {
        // A plain cell must NOT be wrapped in a "Cell" DTO, so pre-P4 readers still load it.
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 1);
        SetCellText(tb, 0, 0, "x");
        doc.Blocks.Add(tb);
        Assert.DoesNotContain("\"Cell\"", DocumentSerializer.Serialize(doc));
    }

    private static Paragraph Para(Run run) => new Paragraph { Inlines = { run } };

    // ---- P3: Enter in a cell splits into a sibling paragraph within the cell ----

    [AvaloniaFact]
    public void Enter_InCell_SplitsIntoSiblingParagraphWithinCell()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<table><tr><td>ab</td><td>c</td></tr></table>");
        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();
        var cell = tb.Cells[0][0];
        Assert.Single(cell.Blocks); // starts as one paragraph

        PlaceCaret(ed, cell.Para, 1); // between 'a' and 'b'
        Press(ed, Key.Enter);

        // The cell now holds two paragraphs ("a" / "b"); the table is unchanged structurally.
        Assert.Equal(2, cell.Blocks.Count);
        Assert.Equal("a", ((Paragraph)cell.Blocks[0]).Text());
        Assert.Equal("b", ((Paragraph)cell.Blocks[1]).Text());
        Assert.Equal(1, tb.Rows);
        Assert.Equal(2, tb.Columns);
        // Caret landed at the start of the new (second) paragraph.
        Assert.Same(cell.Blocks[1], CaretCell(ed));
    }

    // A cell split into two paragraphs ("a" / "b"), caret at the start of the second.
    private static (RichEditor ed, TableCell cell) CellWithTwoParagraphs()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<table><tr><td>ab</td><td>c</td></tr></table>");
        var cell = ed.Document!.Blocks.OfType<TableBlock>().Single().Cells[0][0];
        PlaceCaret(ed, cell.Para, 1);
        Press(ed, Key.Enter); // -> "a" / "b", caret at start of "b"
        return (ed, cell);
    }

    [AvaloniaFact]
    public void Left_AtCellParagraphStart_CrossesToPreviousParagraphInCell()
    {
        var (ed, cell) = CellWithTwoParagraphs();
        Press(ed, Key.Left); // from start of "b" -> end of "a"
        Assert.Same(cell.Blocks[0], CaretCell(ed));
    }

    [AvaloniaFact]
    public void Backspace_AtCellParagraphStart_MergesIntoPreviousParagraphInCell()
    {
        var (ed, cell) = CellWithTwoParagraphs();
        Press(ed, Key.Back); // merge "b" into "a"
        Assert.Single(cell.Blocks);
        Assert.Equal("ab", ((Paragraph)cell.Blocks[0]).Text());
    }

    [AvaloniaFact]
    public void Delete_AtCellParagraphEnd_MergesNextParagraphInCell()
    {
        var (ed, cell) = CellWithTwoParagraphs();
        Press(ed, Key.Left);   // end of "a"
        Press(ed, Key.Delete); // pull "b" up into "a"
        Assert.Single(cell.Blocks);
        Assert.Equal("ab", ((Paragraph)cell.Blocks[0]).Text());
    }

    // ---- Cell content drives table height ----------------------------------

    [AvaloniaFact]
    public void TableHeight_GrowsWithCellContent()
    {
        RichEditor Build(string cellText)
        {
            var doc = new FlowDocument();
            var tb = new TableBlock(1, 1);
            SetCellText(tb, 0, 0, cellText);
            doc.Blocks.Add(tb);
            return new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        }

        var shortEd = Build("x");
        Realize(shortEd);
        double shortH = shortEd.DesiredSize.Height;

        // Many words at a narrow table width force wrapping -> a taller cell -> a taller table.
        var tallEd = Build(string.Join(" ", Enumerable.Repeat("word", 60)));
        Realize(tallEd);
        double tallH = tallEd.DesiredSize.Height;

        Assert.True(tallH > shortH, $"wrapped cell ({tallH}) should be taller than single-char cell ({shortH})");
    }

    private static void SetCellText(TableBlock tb, int r, int c, string text)
    {
        tb.Cells[r][c].Para.Inlines.Clear();
        tb.Cells[r][c].Para.Inlines.Add(new Run { Text = text });
    }

    // ---- Copy: a selection inside one cell must not capture the whole table -----
    // CaptureBlockStructure resolves a cell paragraph to its enclosing TableBlock, so an intra-cell
    // selection used to clone the WHOLE table (start/end top-level block both == the table) -> paste
    // reproduced the entire table. It must return null so copy falls to the inline/run clipboard.
    private static object? CaptureBlockStructure(RichEditor ed, TextRange range) =>
        typeof(RichEditor).GetMethod("CaptureBlockStructure", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ed, new object[] { range });

    [AvaloniaFact]
    public void Copy_TextWithinSingleCell_DoesNotCaptureWholeTable()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<table><tr><td>hello</td><td>world</td></tr></table>");
        var cell = ed.Document!.Blocks.OfType<TableBlock>().Single().Cells[0][0].Para;

        var range = new TextRange(new TextPointer(cell, 0), new TextPointer(cell, 5));
        Assert.Null(CaptureBlockStructure(ed, range));
    }

    [AvaloniaFact]
    public void Copy_SelectionAcrossCells_CapturesTableStructure()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<table><tr><td>hello</td><td>world</td></tr></table>");
        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();

        var range = new TextRange(new TextPointer(tb.Cells[0][0].Para, 0), new TextPointer(tb.Cells[0][1].Para, 5));
        Assert.NotNull(CaptureBlockStructure(ed, range));
    }

    // ---- Paste: multi-block content lands inside the cell, not after the table -----
    // InsertParsedDocument/InsertBlocks used to splice multi-block content into Document.Blocks after the
    // caret's top-level block; with the caret in a cell that top-level block is the table, so the paste
    // landed *outside* the table. It must splice into the cell's block list instead.

    [AvaloniaFact]
    public void Paste_MultiBlockHtml_WithCaretInCell_SplitsAtCaretInsideCell()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<table><tr><td>x</td></tr></table>");
        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();
        var cell = tb.Cells[0][0];
        int topLevelBefore = ed.Document!.Blocks.Count;
        PlaceCaret(ed, cell.Para, 1); // end of "x"

        ed.InsertHtml("<p>one</p><p>two</p>");

        // Split at the caret: first pasted paragraph continues the caret line ("x"+"one"), the last
        // becomes a new sibling ("two"). Nothing leaked out as new top-level blocks.
        Assert.Equal(topLevelBefore, ed.Document!.Blocks.Count);
        Assert.Equal(2, cell.Blocks.Count);
        Assert.Equal("xone", ((Paragraph)cell.Blocks[0]).Text());
        Assert.Equal("two", ((Paragraph)cell.Blocks[1]).Text());
    }

    [AvaloniaFact]
    public void Paste_TableHtml_WithCaretInCell_StaysTopLevel()
    {
        // Nested tables aren't rendered in cells yet (P4-2b): a paste containing a table falls back to a
        // top-level insert rather than dropping an invisible nested table into the cell.
        var ed = new RichEditor();
        ed.LoadHtml("<table><tr><td>x</td></tr></table>");
        PlaceCaret(ed, ed.Document!.Blocks.OfType<TableBlock>().Single().Cells[0][0].Para, 1);

        ed.InsertHtml("<table><tr><td>nested</td></tr></table>");

        Assert.Equal(2, ed.Document!.Blocks.OfType<TableBlock>().Count());
    }
}
