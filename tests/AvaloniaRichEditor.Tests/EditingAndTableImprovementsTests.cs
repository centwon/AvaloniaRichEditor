using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

public class EditingAndTableImprovementsTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags NPS = BindingFlags.NonPublic | BindingFlags.Static;

    private static void PlaceCaret(RichEditor ed, Paragraph p, int off)
    {
        foreach (var n in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(n, NP)!.SetValue(ed, new TextPointer(p, off));
    }

    private static void SetSelection(RichEditor ed, Paragraph sPara, int sOff, Paragraph ePara, int eOff)
    {
        typeof(RichEditor).GetField("_selectionStart", NP)!.SetValue(ed, new TextPointer(sPara, sOff));
        typeof(RichEditor).GetField("_selectionEnd", NP)!.SetValue(ed, new TextPointer(ePara, eOff));
        typeof(RichEditor).GetField("_caretPosition", NP)!.SetValue(ed, new TextPointer(ePara, eOff));
    }

    private static void Type(RichEditor ed, string s)
        => ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = s });

    // ---- A1: Undo/Redo resets _editRun so subsequent typing does not coalesce over the redone step ----

    [AvaloniaFact]
    public void TypingAfterRedo_StartsNewUndoStep_KeepingRedoneState()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "" } } };
        var ed = new RichEditor { Document = new FlowDocument { Blocks = { p } } };
        PlaceCaret(ed, p, 0);

        Type(ed, "a");
        Type(ed, "b");
        Type(ed, "c");
        Assert.Equal("abc", ed.GetPlainText());

        ed.Undo();
        Assert.Equal("", ed.GetPlainText());

        ed.Redo();
        Assert.Equal("abc", ed.GetPlainText());

        Type(ed, "d");
        Assert.Equal("abcd", ed.GetPlainText());

        ed.Undo();
        Assert.Equal("abc", ed.GetPlainText());
    }

    // ---- A2: TableBlock.MergeCells preserves multi-paragraph/block order & layout cache signature ----

    [Fact]
    public void MergeCells_PreservesAllParagraphsAndBlocksInCellOrder()
    {
        var tb = new TableBlock(1, 2);
        var c0 = tb.Cells[0][0];
        c0.Blocks.Clear();
        c0.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "A1" } }, Parent = c0 });
        c0.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "A2" } }, Parent = c0 });

        var c1 = tb.Cells[0][1];
        c1.Blocks.Clear();
        c1.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "B1" } }, Parent = c1 });
        c1.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "B2" } }, Parent = c1 });

        tb.MergeCells(0, 0, 0, 1);

        var texts = c0.Blocks.OfType<Paragraph>()
            .Select(p => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text)))
            .ToArray();
        Assert.Equal(new[] { "A1", "A2", "B1", "B2" }, texts);
    }

    [Fact]
    public void ParagraphSig_ChangesWhenInlineTableSpansOrCellVerticalAlignmentChange()
    {
        var sigMethod = typeof(RichEditor).GetMethod("ParagraphSig", NPS)!;
        var it = new InlineTable { Table = new TableBlock(2, 2) };
        var host = new Paragraph { Inlines = { it } };

        long sig0 = (long)sigMethod.Invoke(null, new object[] { host })!;
        it.Table.Cells[0][0].VerticalAlignment = CellVerticalAlignment.Bottom;
        long sig1 = (long)sigMethod.Invoke(null, new object[] { host })!;
        Assert.NotEqual(sig0, sig1);

        it.Table.MergeCells(0, 0, 0, 1);
        long sig2 = (long)sigMethod.Invoke(null, new object[] { host })!;
        Assert.NotEqual(sig1, sig2);
    }

    // ---- A3: Multi-paragraph selection inside a single table cell keeps paragraph breaks on copy ----

    [AvaloniaFact]
    public void CaptureBlockStructure_SingleCellMultiParagraphSelection_ReturnsParagraphBlocks()
    {
        var tb = new TableBlock(1, 2);
        var cell = tb.Cells[0][0];
        cell.Blocks.Clear();
        var p1 = new Paragraph { ListType = ListKind.Bullet, Inlines = { new Run { Text = "First" } }, Parent = cell };
        var p2 = new Paragraph { ListType = ListKind.Bullet, Inlines = { new Run { Text = "Second" } }, Parent = cell };
        cell.Blocks.Add(p1);
        cell.Blocks.Add(p2);

        var ed = new RichEditor { Document = new FlowDocument { Blocks = { tb } } };
        var range = new TextRange(new TextPointer(p1, 0), new TextPointer(p2, 6));
        var capMethod = typeof(RichEditor).GetMethod("CaptureBlockStructure", NP)!;
        var blocks = (List<Block>?)capMethod.Invoke(ed, new object[] { range });

        Assert.NotNull(blocks);
        Assert.Equal(2, blocks!.Count);
        Assert.All(blocks, b => Assert.IsType<Paragraph>(b));
        Assert.Equal(ListKind.Bullet, ((Paragraph)blocks[0]).ListType);
        Assert.Equal(ListKind.Bullet, ((Paragraph)blocks[1]).ListType);
    }

    // ---- A4: InsertTableFromTsv strips blank lines between rows and falls back at max table nesting ----

    [AvaloniaFact]
    public void InsertTableFromTsv_StripsBlankLinesBetweenRows()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "" } } };
        var ed = new RichEditor { Document = new FlowDocument { Blocks = { p } } };
        PlaceCaret(ed, p, 0);

        typeof(RichEditor).GetMethod("InsertTableFromTsv", NP)!.Invoke(ed, new object[] { "a\tb\n\nc\td\n" });

        var tb = Assert.Single(ed.Document!.Blocks.OfType<TableBlock>());
        Assert.Equal(2, tb.Rows);
        Assert.Equal(2, tb.Columns);
    }

    // ---- A5: Non-finite double serialization & FontFamily semicolon escaping ----

    [Fact]
    public void Serialize_WithNonFiniteModelNumbers_DoesNotThrowAndRoundTrips()
    {
        var p = new Paragraph
        {
            Indent = double.PositiveInfinity,
            MarginRight = double.NegativeInfinity,
            Inlines = { new Run { Text = "hi", FontSize = double.PositiveInfinity } }
        };
        var tb = new TableBlock(1, 1) { Indent = double.PositiveInfinity };
        tb.ColumnWidths[0] = double.NaN;
        tb.RowHeights.Add(double.PositiveInfinity);
        var doc = new FlowDocument
        {
            PageSetup = new PageSetup { Margin = new PageMargins(double.PositiveInfinity, double.NaN, 20, 20) },
            Blocks = { p, tb }
        };

        string json = DocumentSerializer.Serialize(doc);
        var roundTripped = DocumentSerializer.Deserialize(json);
        Assert.NotNull(roundTripped);
    }

    [Fact]
    public void HtmlExport_StripsSemicolonFromFontFamily()
    {
        var doc = new FlowDocument
        {
            Blocks =
            {
                new Paragraph
                {
                    Inlines = { new Run { Text = "x", FontFamily = "MyFont;color:red" } }
                }
            }
        };
        string html = HtmlDocumentFormatter.ToHtml(doc);
        Assert.DoesNotContain("MyFont;color:red", html);
        Assert.Contains("font-family:'MyFontcolor:red'", html);
    }

    // ---- A6: DeleteLocalText keeps empty run; removing inline object coalesces runs; CellBlockAsTable RowHeights ----

    [AvaloniaFact]
    public void DeleteLocalText_EmptyingParagraph_LeavesSingleEmptyRun()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "hello" } } };
        var ed = new RichEditor { Document = new FlowDocument { Blocks = { p } } };

        typeof(RichEditor).GetMethod("DeleteLocalText", NP)!.Invoke(ed, new object[] { p, 0, 5 });

        var run = Assert.Single(p.Inlines.OfType<Run>());
        Assert.Equal("", run.Text);
    }

    [AvaloniaFact]
    public void RemoveInlineImageCharacter_CoalescesAdjacentRunsWithSameFormat()
    {
        var img = new InlineImage { Width = 10, Height = 10 };
        var p = new Paragraph
        {
            Inlines =
            {
                new Run { Text = "he" },
                img,
                new Run { Text = "llo" }
            }
        };
        var ed = new RichEditor { Document = new FlowDocument { Blocks = { p } } };

        typeof(RichEditor).GetMethod("RemoveInlineImageCharacter", NP)!.Invoke(ed, new object[] { p, img });

        var run = Assert.Single(p.Inlines.OfType<Run>());
        Assert.Equal("hello", run.Text);
    }

    [Fact]
    public void CellBlockAsTable_WithZeroRowHeights_LeavesRowHeightsEmpty()
    {
        var tb = new TableBlock(2, 2);
        tb.RowHeights.Add(0);
        tb.RowHeights.Add(0);

        var method = typeof(RichEditor).GetMethod("CellBlockAsTable", NPS)!;
        var sub = (TableBlock)method.Invoke(null, new object[] { tb, (0, 0, 1, 1) })!;

        Assert.Empty(sub.RowHeights);
    }

    // Row heights listed for only the first rows (the rest unset): a set height in the block was dropped with the
    // rest. Kept now, the unlisted rows as 0 — as the WinUI port's TableBlock.Extract.
    [Fact]
    public void CellBlockAsTable_WithRowHeightsShorterThanTheBlock_KeepsTheSetHeight()
    {
        var tb = new TableBlock(2, 2);
        tb.RowHeights.Add(40);

        var method = typeof(RichEditor).GetMethod("CellBlockAsTable", NPS)!;
        var sub = (TableBlock)method.Invoke(null, new object[] { tb, (0, 0, 1, 1) })!;

        Assert.Equal(new[] { 40.0, 0.0 }, sub.RowHeights);
    }

    // ---- B8: TSV paste inside a table cell fills existing table cells instead of nesting a table ----

    [AvaloniaFact]
    public void InsertTableFromTsv_WhenCaretInsideTableCell_FillsCellsAndGrowsRows()
    {
        var tb = new TableBlock(1, 2);
        var ed = new RichEditor { Document = new FlowDocument { Blocks = { tb } } };
        PlaceCaret(ed, tb.Cells[0][0].Para, 0);

        typeof(RichEditor).GetMethod("InsertTableFromTsv", NP)!.Invoke(ed, new object[] { "A\tB\tExtraCol\nC\tD\tExtraCol" });

        Assert.Equal(2, tb.Rows);
        Assert.Equal(2, tb.Columns);
        Assert.Empty(tb.Cells[0][0].Blocks.OfType<TableBlock>());
        Assert.Equal("A", ((Run)tb.Cells[0][0].Para.Inlines[0]).Text);
        Assert.Equal("B", ((Run)tb.Cells[0][1].Para.Inlines[0]).Text);
        Assert.Equal("C", ((Run)tb.Cells[1][0].Para.Inlines[0]).Text);
        Assert.Equal("D", ((Run)tb.Cells[1][1].Para.Inlines[0]).Text);
    }

    // ---- B9: TableDrawRoom caps top-level drawn table width to ContentLayoutWidth - 20 ----

    [AvaloniaFact]
    public void TableDrawRoom_TopLevelInSizedEditor_CapsAtContentLayoutWidthMinus20()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "hello" } } };
        var ed = new RichEditor { Document = new FlowDocument { Blocks = { p } }, PageSize = RichEditorPageSize.Continuous };
        var host = InteractionHost.Create(ed, 500, 400);
        host.Render();

        var room = (double)typeof(RichEditor).GetMethod("TableDrawRoom", NP)!.Invoke(ed, new object[] { new Point(30, 30) })!;
        Assert.True(double.IsFinite(room), $"Expected finite TableDrawRoom, got {room}");
        Assert.True(room <= 500);
    }

    // ---- A5: TableNesting 32-level ceiling enforced on HTML import and InsertTable ----

    [AvaloniaFact]
    public void InsertTable_At32LevelNestingLimit_IsNoOpAndPushesNoUndoStep()
    {
        var ed = new RichEditor { Document = new FlowDocument() };
        var root = new TableBlock(1, 1);
        ed.Document.Blocks.Clear();
        ed.Document.Blocks.Add(root);
        var cur = root;
        for (int i = 1; i < 32; i++)
        {
            var next = new TableBlock(1, 1);
            cur.Cells[0][0].Blocks.Clear();
            cur.Cells[0][0].Blocks.Add(next);
            cur = next;
        }
        var deepestPara = cur.Cells[0][0].Para;
        ed.NotifyDocumentChanged();
        PlaceCaret(ed, deepestPara, 0);
        ed.MarkSaved();

        ed.InsertTable(2, 2);

        Assert.False(ed.CanUndo);
        Assert.Empty(cur.Cells[0][0].Blocks.OfType<TableBlock>());
    }

    // ---- B7: Drag selected text to move or Ctrl+drag to copy ----

    [AvaloniaFact]
    public void DropTextSelection_SingleParagraphMoveAndCopy_UpdatesTextAndSelectsDroppedRange()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "Hello world" } } };
        var ed = new RichEditor { Document = new FlowDocument { Blocks = { p } } };
        SetSelection(ed, p, 0, p, 6); // "Hello "

        bool moved = ed.DropTextSelection(new TextPointer(p, 11), copy: false);
        Assert.True(moved);
        Assert.Equal("worldHello ", ed.GetPlainText());
        Assert.True(ed.CanUndo);

        ed.Undo();
        Assert.Equal("Hello world", ed.GetPlainText());

        SetSelection(ed, p = (Paragraph)ed.Document!.Blocks[0], 0, p, 6);
        bool copied = ed.DropTextSelection(new TextPointer(p, 11), copy: true);
        Assert.True(copied);
        Assert.Equal("Hello worldHello ", ed.GetPlainText());
    }

    [AvaloniaFact]
    public void DropTextSelection_MultiParagraphMove_MovesSelectedLinesInOneUndoStep()
    {
        var p1 = new Paragraph { Inlines = { new Run { Text = "First" } } };
        var p2 = new Paragraph { Inlines = { new Run { Text = "SecondTail" } } };
        var ed = new RichEditor { Document = new FlowDocument { Blocks = { p1, p2 } } };
        SetSelection(ed, p1, 0, p2, 6); // "First\nSecond"

        bool moved = ed.DropTextSelection(new TextPointer(p2, 10), copy: false);
        Assert.True(moved);
        Assert.Equal("TailFirst\nSecond", ed.GetPlainText().Replace("\r\n", "\n"));

        ed.Undo();
        Assert.Equal("First\nSecondTail", ed.GetPlainText().Replace("\r\n", "\n"));
    }
}
