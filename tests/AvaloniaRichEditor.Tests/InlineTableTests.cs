using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.TextFormatting;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Milestone B, P1 (model + offset). An InlineTable is an atomic object inline: like an inline image it
// occupies exactly one logical character position (U+FFFC), so the TextRange offset machinery treats it
// the same way. These pin that contract before P2 (render) and P3 (hit-test/caret descent) build on it.
public class InlineTableTests
{
    // Layout: "AB" [table] "CD"  → logical offsets A=0 B=1 TBL=2 C=3 D=4, length 5.
    private static Paragraph TableParagraph() =>
        TestHelpers.Para(new Run { Text = "AB" }, TestHelpers.Tbl(), new Run { Text = "CD" });

    [Fact]
    public void GetText_AcrossInlineTable_DropsPlaceholder_ButOffsetsCountIt()
    {
        var p = TableParagraph();
        Assert.Equal("ABCD", new TextRange(new TextPointer(p, 0), new TextPointer(p, 5)).GetText());
        // 1..4 spans B, the table, and C; the table is dropped from text but still consumed an offset.
        Assert.Equal("BC", new TextRange(new TextPointer(p, 1), new TextPointer(p, 4)).GetText());
    }

    [Fact]
    public void Delete_RangeCoveringInlineTable_RemovesTable()
    {
        var p = TableParagraph();
        new TextRange(new TextPointer(p, 1), new TextPointer(p, 4)).Delete(); // B, table, C
        Assert.Equal("AD", p.Text());
        Assert.DoesNotContain(p.Inlines, i => i is InlineTable);
    }

    [Fact]
    public void Delete_RangeBeforeInlineTable_KeepsTable()
    {
        var p = TableParagraph();
        new TextRange(new TextPointer(p, 0), new TextPointer(p, 1)).Delete(); // just "A"
        Assert.Equal("BCD", p.Text());
        Assert.Contains(p.Inlines, i => i is InlineTable);
    }

    [Fact]
    public void GetRichInlines_KeepsInlineTable()
    {
        // Range 1..4 spans B, the table, and C; the in-app rich clipboard must carry the table whole.
        var p = TableParagraph();
        var inlines = new TextRange(new TextPointer(p, 1), new TextPointer(p, 4)).GetRichInlines();
        Assert.Equal(3, inlines.Count);
        Assert.IsType<InlineTable>(inlines[1]);
        Assert.Equal("B", (inlines[0] as Run)?.Text);
        Assert.Equal("C", (inlines[2] as Run)?.Text);
    }

    // ---- P2: the inline table contributes its height to the paragraph line ----

    private static double MeasureParagraphHeight(params Inline[] inlines)
    {
        var ed = new RichEditor { MinHeight = 0, PageSize = RichEditorPageSize.Continuous };
        var doc = new FlowDocument();
        var p = new Paragraph();
        foreach (var i in inlines) p.Inlines.Add(i);
        doc.Blocks.Add(p);
        ed.Document = doc;
        ed.Measure(new Size(700, double.PositiveInfinity));
        return ed.DesiredSize.Height;
    }

    private static void Realize(RichEditor ed, double width = 800)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new Avalonia.PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    [AvaloniaFact]
    public void RenderingParagraphWithInlineTable_DoesNotRecurseInfinitely()
    {
        // Regression: FlushInlineTableDraws re-entered itself (DrawNestedTable -> DrawCellBlockList ->
        // flush) over the same un-cleared list, overflowing the stack. Rendering must complete.
        var it = TestHelpers.Tbl("Z");
        var host = TestHelpers.Para(new Run { Text = "AB" }, it, new Run { Text = "CD" });
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };

        Realize(ed); // must not throw / overflow
    }

    [AvaloniaFact]
    public void ParagraphWithInlineTable_IsTallerThanTextOnly()
    {
        double textOnly = MeasureParagraphHeight(new Run { Text = "abc" });

        // A 3-row inline table is much taller than one text line, so the line it sits on grows.
        var it = new InlineTable { Table = new TableBlock(3, 1) };
        for (int r = 0; r < 3; r++) it.Table.Cells[r][0].Para.Inlines.Add(new Run { Text = "row" });
        double withTable = MeasureParagraphHeight(new Run { Text = "abc" }, it);

        Assert.True(withTable > textOnly + 40,
            $"expected the inline table to raise the line height: textOnly={textOnly}, withTable={withTable}");
    }

    // ---- P3a: descent — enumeration, cache invalidation, hit-test ----

    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    [AvaloniaFact]
    public void GetAllParagraphsInOrder_IncludesInlineTableCellParagraphs()
    {
        // The cell paragraph inside an inline table must be reachable (selection/find/navigation).
        var it = TestHelpers.Tbl("Z");
        var host = TestHelpers.Para(new Run { Text = "AB" }, it);
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };

        var paras = (List<Paragraph>)typeof(RichEditor)
            .GetMethod("GetAllParagraphsInOrder", Priv)!.Invoke(ed, null)!;

        Assert.Contains(host, paras);
        Assert.Contains(it.Table.Cells[0][0].Para, paras); // descended into the inline table
    }

    [AvaloniaFact]
    public void EditingInlineTableCell_GrowsHostParagraphCachedLayout()
    {
        // ParagraphSig must fold in the inline table's cell content, so adding a line to the cell
        // invalidates the host paragraph's cached layout and the reserved table box grows.
        var it = TestHelpers.Tbl("Z");
        var host = TestHelpers.Para(new Run { Text = "AB" }, it);
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };

        var build = typeof(RichEditor).GetMethod("BuildTextLayout", Priv)!;
        double H() => ((TextLayout)build.Invoke(ed, new object?[] { host, 670.0, -1, null })!).Height;

        double before = H();
        it.Table.Cells[0][0].Blocks.Add(TestHelpers.Para(new Run { Text = "second" })); // taller cell
        double after = H();

        Assert.True(after > before, $"host layout should grow when the inline table's cell does: {before} -> {after}");
    }

    [AvaloniaFact]
    public void ClickInsideInlineTable_PlacesCaretInCell()
    {
        var it = TestHelpers.Tbl("Z");
        var host = TestHelpers.Para(new Run { Text = "AB" }, it);
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };
        ed.Measure(new Size(800, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, 800, ed.DesiredSize.Height));

        // The inline table is the 3rd logical char (offset 2). Its painted box is bottom-aligned in the
        // line; click its centre and assert the caret descended into the cell paragraph.
        double width = (double)typeof(RichEditor).GetProperty("ContentLayoutWidth", Priv)!.GetValue(ed)!;
        var build = typeof(RichEditor).GetMethod("BuildTextLayout", Priv)!;
        var layout = (TextLayout)build.Invoke(ed, new object?[] { host, width, -1, null })!;
        var rect = layout.HitTestTextRange(2, 1).First();
        var click = new Point(10 + rect.X + rect.Width / 2, rect.Y + rect.Height / 2); // ParaLeft=10, paraTop=0

        var hit = (TextPointer)typeof(RichEditor).GetMethod("GetPositionFromPoint", Priv)!
            .Invoke(ed, new object?[] { click })!;

        Assert.Same(it.Table.Cells[0][0].Para, hit.Paragraph);
    }

    // ---- P3b: keyboard navigation into/through/out of an inline table ----

    private static void Press(RichEditor ed, Avalonia.Input.Key key)
        => ed.RaiseEvent(new Avalonia.Input.KeyEventArgs
        { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = key });

    private static void PlaceCaret(RichEditor ed, Paragraph p, int offset)
    {
        foreach (var f in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(f, Priv)!.SetValue(ed, new TextPointer(p, offset));
    }

    private static TextPointer Caret(RichEditor ed)
        => (TextPointer)typeof(RichEditor).GetField("_caretPosition", Priv)!.GetValue(ed)!;

    // Host paragraph "AB" [1x2 inline table] "CD" — the table's ObjChar is at offset 2.
    private static (RichEditor ed, Paragraph host, InlineTable it) NavDoc()
    {
        var it = new InlineTable { Table = new TableBlock(1, 2) };
        it.Table.Cells[0][0].Para.Inlines.Add(new Run { Text = "P" });
        it.Table.Cells[0][1].Para.Inlines.Add(new Run { Text = "Q" });
        var host = TestHelpers.Para(new Run { Text = "AB" }, it, new Run { Text = "CD" });
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        return (new RichEditor { Document = doc }, host, it);
    }

    [AvaloniaFact]
    public void Right_FromHostBeforeTable_EntersFirstCell()
    {
        var (ed, host, it) = NavDoc();
        PlaceCaret(ed, host, 2); // right before the table's ObjChar
        Press(ed, Avalonia.Input.Key.Right);
        Assert.Same(it.Table.Cells[0][0].Para, Caret(ed).Paragraph);
        Assert.Equal(0, Caret(ed).Offset);
    }

    [AvaloniaFact]
    public void Right_FromLastCellEnd_ExitsToHostAfterTable()
    {
        var (ed, host, it) = NavDoc();
        var last = it.Table.Cells[0][1].Para;
        PlaceCaret(ed, last, 1); // end of last cell ("Q")
        Press(ed, Avalonia.Input.Key.Right);
        Assert.Same(host, Caret(ed).Paragraph);
        Assert.Equal(3, Caret(ed).Offset); // just after the table (ObjChar at 2)
    }

    [AvaloniaFact]
    public void Left_FromHostAfterTable_EntersLastCellEnd()
    {
        var (ed, host, it) = NavDoc();
        PlaceCaret(ed, host, 3); // right after the table's ObjChar
        Press(ed, Avalonia.Input.Key.Left);
        var last = it.Table.Cells[0][1].Para;
        Assert.Same(last, Caret(ed).Paragraph);
        Assert.Equal(1, Caret(ed).Offset); // end of "Q"
    }

    [AvaloniaFact]
    public void Left_FromFirstCellStart_ExitsToHostBeforeTable()
    {
        var (ed, host, it) = NavDoc();
        PlaceCaret(ed, it.Table.Cells[0][0].Para, 0);
        Press(ed, Avalonia.Input.Key.Left);
        Assert.Same(host, Caret(ed).Paragraph);
        Assert.Equal(2, Caret(ed).Offset); // just before the table
    }

    [AvaloniaFact]
    public void Right_FromFirstCellEnd_StepsToNextCell()
    {
        var (ed, _, it) = NavDoc();
        PlaceCaret(ed, it.Table.Cells[0][0].Para, 1); // end of first cell ("P")
        Press(ed, Avalonia.Input.Key.Right);
        Assert.Same(it.Table.Cells[0][1].Para, Caret(ed).Paragraph); // inter-cell, not exit
    }

    [AvaloniaFact]
    public void Right_AtHostEnd_GoesToNextParagraph_NotInlineTableCell()
    {
        // Regression: the inline table's cells were in the linear nav order, so → at the host paragraph's
        // end jumped into the first cell (and last-cell → looped back). The host's end must go to the
        // next block.
        var (ed, _, _) = NavDoc();
        var after = TestHelpers.Para(new Run { Text = "next" });
        ed.Document!.Blocks.Add(after);

        var host = ed.Document!.Blocks.OfType<Paragraph>().First();
        PlaceCaret(ed, host, 5); // end of "AB" + table + "CD"
        Press(ed, Avalonia.Input.Key.Right);

        Assert.Same(after, Caret(ed).Paragraph); // next paragraph, not an inline-table cell
    }

    [AvaloniaFact]
    public void CaretAfterTrailingInlineTable_PinnedToItsRightEdge()
    {
        // Regression: with no text after the inline table, the caret position right after it collapsed to
        // the table's LEFT edge (Avalonia drops a line-trailing DrawableTextRun from caret distance) — the
        // caret appeared in front of the table. FixCaretAfterTrailingImage must pin it to the right edge.
        var it = TestHelpers.Tbl("Z");
        var host = TestHelpers.Para(new Run { Text = "abc" }, it); // table is the 4th logical char
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };
        var layout = (TextLayout)typeof(RichEditor).GetMethod("BuildTextLayout", Priv)!
            .Invoke(ed, new object?[] { host, 670.0, -1, null })!;

        double tableLeft = layout.HitTestTextPosition(3).X;
        var raw = layout.HitTestTextPosition(4); // collapses to the table's left (the quirk)
        var fixedRect = RichEditor.FixCaretAfterTrailingImage(layout, host, 4, 4, raw);

        Assert.True(fixedRect.X > tableLeft + 50,
            $"caret should sit past the table, not in front: tableLeft={tableLeft}, caretX={fixedRect.X}");
    }

    [AvaloniaFact]
    public void CaretInLineWithInlineTable_BottomAligns()
    {
        // Regression: a caret on a line made tall by an inline table was centered (extra/2), drawing it
        // above the text which sits on the baseline. It must bottom-align (full extra), like inline images.
        var host = TestHelpers.Para(new Run { Text = "AB" }, TestHelpers.Tbl("Z"));
        double off = (double)typeof(RichEditor)
            .GetMethod("CaretYInLine", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { host, 100.0, 20.0 })!;
        Assert.Equal(80.0, off); // lineHeight - caretHeight (bottom), not 40 (centered)
    }

    [AvaloniaFact]
    public void Tab_InInlineTableCell_MovesToNextCell()
    {
        var (ed, _, it) = NavDoc();
        PlaceCaret(ed, it.Table.Cells[0][0].Para, 0);
        Press(ed, Avalonia.Input.Key.Tab);
        Assert.Same(it.Table.Cells[0][1].Para, Caret(ed).Paragraph);
    }

    // ---- P4: insertion, treat-as-character toggle, deletion ----

    [AvaloniaFact]
    public void InsertInlineTable_InsertsAtCaret_AsOneCharacter()
    {
        var host = TestHelpers.Para(new Run { Text = "ABCD" });
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };
        PlaceCaret(ed, host, 2); // between B and C

        ed.InsertInlineTable(2, 2);

        Assert.Single(host.Inlines.OfType<InlineTable>());
        Assert.Equal(5, GetParagraphLength(ed, host)); // "AB" + table(1) + "CD"
        Assert.Equal(3, Caret(ed).Offset);             // caret just after the table
    }

    [AvaloniaFact]
    public void ConvertTableBlockToInline_MovesTableIntoAdjacentParagraph()
    {
        var lead = TestHelpers.Para(new Run { Text = "lead" });
        var tb = new TableBlock(1, 1);
        tb.Cells[0][0].Para.Inlines.Add(new Run { Text = "Z" });
        var doc = new FlowDocument();
        doc.Blocks.Add(lead);
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };

        typeof(RichEditor).GetMethod("ConvertTableBlockToInline", Priv)!.Invoke(ed, new object[] { tb });

        Assert.DoesNotContain(ed.Document!.Blocks, b => b is TableBlock); // no longer a block
        Assert.Single(lead.Inlines.OfType<InlineTable>());                // now inline on the lead paragraph
    }

    [AvaloniaFact]
    public void ConvertInlineTableToBlock_PromotesBackToSiblingTable()
    {
        var it = TestHelpers.Tbl("Z");
        var host = TestHelpers.Para(new Run { Text = "AB" }, it);
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };

        typeof(RichEditor).GetMethod("ConvertInlineTableToBlock", Priv)!
            .Invoke(ed, new object[] { host, it });

        Assert.DoesNotContain(host.Inlines, i => i is InlineTable);     // removed from the paragraph
        Assert.Contains(ed.Document!.Blocks, b => b is TableBlock);     // promoted to a block sibling
    }

    [AvaloniaFact]
    public void Backspace_AfterInlineTable_DeletesIt()
    {
        var it = TestHelpers.Tbl("Z");
        var host = TestHelpers.Para(new Run { Text = "AB" }, it, new Run { Text = "CD" });
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };
        PlaceCaret(ed, host, 3); // just after the table's ObjChar

        Press(ed, Avalonia.Input.Key.Back);

        Assert.DoesNotContain(host.Inlines, i => i is InlineTable);
        Assert.Equal("ABCD", host.Text());
    }

    [AvaloniaFact]
    public void TypingBeforeLeadingInlineTable_InsertsTextInFrontOfIt()
    {
        // Regression: typing right before an inline table at the start of a paragraph appended the text
        // at the end (only InlineImage was special-cased). It must land in front of the table.
        var it = TestHelpers.Tbl("Z");
        var host = TestHelpers.Para(it, new Run { Text = "CD" });
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };
        PlaceCaret(ed, host, 0); // before the table

        ed.InsertText("X");

        Assert.IsType<Run>(host.Inlines[0]);
        Assert.Equal("X", ((Run)host.Inlines[0]).Text);   // text in front
        Assert.IsType<InlineTable>(host.Inlines[1]);       // table still right after it
        Assert.Equal("XCD", host.Text());
    }

    [AvaloniaFact]
    public void TypingAfterTrailingInlineTable_InsertsTextBehindIt()
    {
        var it = TestHelpers.Tbl("Z");
        var host = TestHelpers.Para(new Run { Text = "AB" }, it);
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };
        PlaceCaret(ed, host, 3); // after the table's ObjChar (AB=2, table=1)

        ed.InsertText("Y");

        Assert.IsType<InlineTable>(host.Inlines[1]);
        Assert.Equal("Y", (host.Inlines[2] as Run)?.Text); // text behind the table
        Assert.Equal("ABY", host.Text());
    }

    // ---- Draw-table (grid pick + drag-to-size) ----

    [AvaloniaFact]
    public void BeginTableDraw_ArmsPendingMode()
    {
        var ed = new RichEditor { Document = new FlowDocument() };
        typeof(RichEditor).GetMethod("BeginTableDraw", Priv)!.Invoke(ed, new object[] { 2, 2 });
        Assert.NotNull(typeof(RichEditor).GetField("_pendingTableDraw", Priv)!.GetValue(ed));
    }

    [AvaloniaFact]
    public void InsertTableDrawn_SizesColumnsAndRowsToRectangle()
    {
        var host = TestHelpers.Para(new Run { Text = "x" });
        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };
        PlaceCaret(ed, host, 0);

        typeof(RichEditor).GetMethod("InsertTableDrawn", Priv)!
            .Invoke(ed, new object[] { 2, 3, 300.0, 120.0 }); // 2 rows, 3 cols, 300x120

        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();
        Assert.Equal(2, tb.Rows);
        Assert.Equal(3, tb.Columns);
        Assert.Equal(100.0, tb.ColumnWidths[0], 1); // 300 / 3 columns
        Assert.Equal(60.0, tb.RowHeights[0], 1);    // 120 / 2 rows (minimum row height)
    }

    private static int GetParagraphLength(RichEditor ed, Paragraph p)
        => (int)typeof(RichEditor).GetMethod("GetParagraphLength", Priv, new[] { typeof(Paragraph) })!
            .Invoke(ed, new object[] { p })!;

    // ---- P5: serialization ----

    [Fact]
    public void Json_RoundTrip_PreservesInlineTable_AndSurroundingText()
    {
        var doc = new FlowDocument();
        var it = new InlineTable { Table = new TableBlock(1, 2) };
        it.Table.Cells[0][0].Para.Inlines.Add(new Run { Text = "L" });
        it.Table.Cells[0][1].Para.Inlines.Add(new Run { Text = "R" });
        var host = TestHelpers.Para(new Run { Text = "AB" }, it, new Run { Text = "CD" });
        doc.Blocks.Add(host);

        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));

        var host2 = (Paragraph)doc2.Blocks[0];
        Assert.Equal(3, host2.Inlines.Count);              // Run, InlineTable, Run — order preserved
        Assert.Equal("ABCD", host2.Text());                // surrounding text intact
        var it2 = host2.Inlines.OfType<InlineTable>().Single();
        Assert.Equal(2, it2.Table.Columns);
        Assert.Equal("L", it2.Table.Cells[0][0].Para.Text());
        Assert.Equal("R", it2.Table.Cells[0][1].Para.Text());
    }

    [Fact]
    public void Json_RoundTrip_PreservesNestedTableInsideInlineTable()
    {
        // The inline table reuses the recursive block-table DTO, so a table nested in its cell round-trips.
        var doc = new FlowDocument();
        var inner = new TableBlock(1, 1);
        inner.Cells[0][0].Para.Inlines.Add(new Run { Text = "deep" });
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        it.Table.Cells[0][0].Blocks.Clear();
        it.Table.Cells[0][0].Blocks.Add(inner);
        doc.Blocks.Add(TestHelpers.Para(it));

        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));

        var it2 = ((Paragraph)doc2.Blocks[0]).Inlines.OfType<InlineTable>().Single();
        var inner2 = it2.Table.Cells[0][0].Blocks.OfType<TableBlock>().Single();
        Assert.Equal("deep", inner2.Cells[0][0].Para.Text());
    }

    [Fact]
    public void ToHtml_EmitsInlineTableAsTable_WithSurroundingText()
    {
        // HTML has no inline table; exporting it as a <table> keeps the content for a Word/HWP paste.
        var doc = new FlowDocument();
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        it.Table.Cells[0][0].Para.Inlines.Add(new Run { Text = "Zcell" });
        doc.Blocks.Add(TestHelpers.Para(new Run { Text = "before" }, it, new Run { Text = "after" }));

        var html = AvaloniaRichEditor.Formatters.HtmlDocumentFormatter.ToHtml(doc);

        Assert.Contains("<table", html);
        Assert.Contains("Zcell", html);  // cell content survives
        Assert.Contains("before", html);
        Assert.Contains("after", html);
    }

    [Fact]
    public void HtmlRoundTrip_ToHtmlThenParseHtml_PreservesContent()
    {
        // The view's HTML export -> import goes ToHtml -> ParseHtml; this pins that the round-trip keeps
        // content (the import bug was a missing HTML branch that parsed HTML as JSON -> empty document).
        var doc = new FlowDocument();
        doc.Blocks.Add(TestHelpers.Para(new Run { Text = "hello world" }));
        var tb = new TableBlock(1, 1);
        tb.Cells[0][0].Para.Inlines.Add(new Run { Text = "celltext" });
        doc.Blocks.Add(tb);

        string html = AvaloniaRichEditor.Formatters.HtmlDocumentFormatter.ToHtml(doc);
        var doc2 = AvaloniaRichEditor.Formatters.HtmlDocumentFormatter.ParseHtml(html);

        Assert.Contains(doc2.Blocks, b => b is Paragraph p && p.Text() == "hello world");
        var tb2 = doc2.Blocks.OfType<TableBlock>().Single();
        Assert.Equal("celltext", tb2.Cells[0][0].Para.Text());
    }

    private const string Png1x1 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    [Fact]
    public void HtmlRoundTrip_PreservesColumnWidths()
    {
        // The table came back squished because column widths weren't carried in HTML; a <colgroup> now
        // restores the proportions.
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 3);
        tb.ColumnWidths[0] = 60;
        tb.ColumnWidths[1] = 200;
        tb.ColumnWidths[2] = 120;
        for (int c = 0; c < 3; c++) tb.Cells[0][c].Para.Inlines.Add(new Run { Text = $"c{c}" });
        doc.Blocks.Add(tb);

        var html = AvaloniaRichEditor.Formatters.HtmlDocumentFormatter.ToHtml(doc);
        var doc2 = AvaloniaRichEditor.Formatters.HtmlDocumentFormatter.ParseHtml(html);

        var tb2 = doc2.Blocks.OfType<TableBlock>().Single();
        Assert.Equal(60, tb2.ColumnWidths[0], 1);
        Assert.Equal(200, tb2.ColumnWidths[1], 1);
        Assert.Equal(120, tb2.ColumnWidths[2], 1);
    }

    [Fact]
    public void HtmlRoundTrip_PreservesNestedTableInsideCell()
    {
        // Regression: ParseTable parsed cells as inlines only, so a nested table inside a cell was dropped
        // on import (export emitted it, import lost it -> the table looked empty in the browser/editor).
        // (A cell block image also round-trips on a real platform; its decode is a no-op under headless,
        // so it's verified separately by the export test, not asserted here.)
        var doc = new FlowDocument();
        var outer = new TableBlock(1, 1);
        var inner = new TableBlock(1, 1);
        inner.Cells[0][0].Para.Inlines.Add(new Run { Text = "deepcell" });
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(TestHelpers.Para(new Run { Text = "lead" }));
        outer.Cells[0][0].Blocks.Add(inner);
        doc.Blocks.Add(outer);

        var html = AvaloniaRichEditor.Formatters.HtmlDocumentFormatter.ToHtml(doc);
        var doc2 = AvaloniaRichEditor.Formatters.HtmlDocumentFormatter.ParseHtml(html);

        var outer2 = doc2.Blocks.OfType<TableBlock>().Single();
        var inner2 = outer2.Cells[0][0].Blocks.OfType<TableBlock>().Single(); // nested table restored
        Assert.Equal("deepcell", inner2.Cells[0][0].Para.Text());
    }

    [Fact]
    public void ToHtml_EmitsCellBlockImage_AndNestedTable()
    {
        var doc = new FlowDocument();
        var outer = new TableBlock(1, 1);
        var inner = new TableBlock(1, 1);
        inner.Cells[0][0].Para.Inlines.Add(new Run { Text = "innercell" });
        var img = new ImageBlock { Width = 40, Height = 30 };
        img.SetImageData(System.Convert.FromBase64String(Png1x1), "image/png");
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(img);
        outer.Cells[0][0].Blocks.Add(inner);
        doc.Blocks.Add(outer);

        var html = AvaloniaRichEditor.Formatters.HtmlDocumentFormatter.ToHtml(doc);

        Assert.Contains("innercell", html);  // nested table content present
        Assert.Contains("data:image", html); // cell block image present
    }

    [Fact]
    public void Clone_DeepCopiesWrappedTable()
    {
        var it = TestHelpers.Tbl("deep");
        var clone = (InlineTable)it.Clone();

        Assert.NotSame(it, clone);
        Assert.NotSame(it.Table, clone.Table);                 // table is deep-copied, not shared
        Assert.Equal("deep", clone.Table.Cells[0][0].Para.Text());

        clone.Table.Cells[0][0].Para.Inlines.Clear();          // mutating the clone must not touch the original
        Assert.Equal("deep", it.Table.Cells[0][0].Para.Text());
    }
}
