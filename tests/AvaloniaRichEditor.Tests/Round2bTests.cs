using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Avalonia.Media;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Round 2b: the three WinUI-port fixes confirmed to apply to this codebase — the table-cell list
// marker gutter, drag handles flipping IsModified, and FindCell's parent-chain lookup.
public class Round2bTests
{
    // Forces a render pass (no top-level Window) so measure/layout caches are populated.
    private static void Realize(RichEditor ed, double width = 800)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    // ---- 1. cell list marker gutter ---------------------------------------
    [AvaloniaFact]
    public void CellListParagraph_ReservesTheMarkerGutter_InTheCellMeasure()
    {
        // The marker gutter (CellParaLeft) must be applied by every cell walk, not just the renderer.
        // The measure walk is the one observable headlessly: a listed cell paragraph wraps in a
        // narrower box, so the row (and the document) grows taller.
        // Two editors rather than one mutated in place: a second measure pass would reuse the trusted
        // table-layout cache (the model was changed behind the editor's back).
        static double Height(ListKind kind)
        {
            var tb = new TableBlock(1, 1);
            var para = tb.Cells[0][0].Para;
            para.ListType = kind;
            ((Run)para.Inlines[0]).Text = "wrapping text inside a narrow table cell column";
            var doc = new FlowDocument();
            doc.Blocks.Add(tb);
            var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
            Realize(ed);
            return ed.DesiredSize.Height;
        }

        double plain = Height(ListKind.None), listed = Height(ListKind.Bullet);
        Assert.True(listed > plain,
            $"a listed cell paragraph should wrap inside the marker gutter ({listed} vs {plain})");
    }

    [AvaloniaFact]
    public void TogglingAList_AppliesToEverySelectedCellParagraph()
    {
        // The list commands collected only TOP-LEVEL selected paragraphs and fell back to "just the
        // caret's paragraph" otherwise, so a multi-paragraph selection inside a cell listed the first
        // paragraph only.
        var tb = new TableBlock(1, 1);
        var cell = tb.Cells[0][0];
        cell.Blocks.Clear();
        cell.Blocks.Add(TestHelpers.Para(new Run { Text = "first" }));
        cell.Blocks.Add(TestHelpers.Para(new Run { Text = "second" }));
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Realize(ed);

        ed.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.A,
            KeyModifiers = Avalonia.Input.KeyModifiers.Control,
        });
        ed.ToggleBullet();

        foreach (var b in cell.Blocks)
            Assert.Equal(ListKind.Bullet, ((Paragraph)b).ListType);

        ed.RemoveList();
        foreach (var b in cell.Blocks)
            Assert.Equal(ListKind.None, ((Paragraph)b).ListType);
    }

    // ---- AllowRemoteImagesOnPaste reaches the load/insert paths -------------
    // Only the blocked case is asserted: with remote images allowed the parser would really hit the
    // network. `false` must reach ParseHtml from LoadHtml/InsertHtml too, not just from paste.
    [AvaloniaFact]
    public void LoadHtml_AndInsertHtml_HonourAllowRemoteImagesOnPaste()
    {
        const string html = "<p><img src=\"http://example.com/x.png\"></p>";

        var loaded = new RichEditor { AllowRemoteImagesOnPaste = false };
        loaded.LoadHtml(html);
        Assert.Equal(0, ImageCount(loaded));

        var inserted = new RichEditor { AllowRemoteImagesOnPaste = false };
        inserted.LoadHtml("<p>abc</p>");
        inserted.FocusDocumentEnd();
        inserted.InsertHtml(html);
        Assert.Equal(0, ImageCount(inserted));

        static int ImageCount(RichEditor ed) =>
            ed.Document!.Blocks.OfType<ImageBlock>().Count()
            + ed.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineImage>().Count();
    }

    // ---- staged Ctrl+A inside a table (HWP/Excel) --------------------------
    private static TextPointer Field(RichEditor ed, string name)
        => (TextPointer)typeof(RichEditor)
            .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(ed)!;

    private static void SetCaret(RichEditor ed, Paragraph p, int off)
    {
        foreach (var name in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor)
                .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(ed, new TextPointer(p, off));
    }

    private static void CtrlA(RichEditor ed) => ed.RaiseEvent(new Avalonia.Input.KeyEventArgs
    {
        RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
        Key = Avalonia.Input.Key.A,
        KeyModifiers = Avalonia.Input.KeyModifiers.Control,
    });

    [AvaloniaFact]
    public void CtrlA_InsideACell_SelectsCellThenTableThenDocument()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(TestHelpers.Para(new Run { Text = "above" }));
        var tb = new TableBlock(2, 2);
        var cell = tb.Cells[0][0];
        cell.Blocks.Clear();
        cell.Blocks.Add(TestHelpers.Para(new Run { Text = "a" }));
        cell.Blocks.Add(TestHelpers.Para(new Run { Text = "bb" }));
        doc.Blocks.Add(tb);
        doc.Blocks.Add(TestHelpers.Para(new Run { Text = "below" }));
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Realize(ed);
        SetCaret(ed, (Paragraph)cell.Blocks[0], 1);

        CtrlA(ed); // 1st: the cell's own contents (both of its paragraphs)
        Assert.Same(cell.Blocks[0], Field(ed, "_selectionStart").Paragraph);
        Assert.Same(cell.Blocks[1], Field(ed, "_selectionEnd").Paragraph);
        Assert.Equal(2, Field(ed, "_selectionEnd").Offset); // "bb"

        CtrlA(ed); // 2nd: the whole table — first cell's start to last cell's end
        Assert.Same(cell.Blocks[0], Field(ed, "_selectionStart").Paragraph);
        Assert.Same(tb.Cells[1][1].Para, Field(ed, "_selectionEnd").Paragraph);

        CtrlA(ed); // 3rd: the document
        Assert.Same(doc.Blocks[0], Field(ed, "_selectionStart").Paragraph);
        Assert.Same(doc.Blocks[^1], Field(ed, "_selectionEnd").Paragraph);
    }

    [AvaloniaFact]
    public void CtrlA_InsideANestedTable_ClimbsOneLevelPerPress()
    {
        // A table inside a cell: cell -> inner table -> OUTER table -> document, one level per press.
        var doc = new FlowDocument();
        var outer = new TableBlock(1, 2);
        var inner = new TableBlock(1, 2);
        ((Run)inner.Cells[0][0].Para.Inlines[0]).Text = "i00";
        ((Run)inner.Cells[0][1].Para.Inlines[0]).Text = "i01";
        outer.Cells[0][0].Blocks.Add(inner); // normalization keeps a paragraph either side of it
        doc.Blocks.Add(outer);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Realize(ed);
        var innerCell = inner.Cells[0][0];
        SetCaret(ed, innerCell.Para, 0);

        CtrlA(ed); // 1: the inner cell
        Assert.Same(innerCell.Para, Field(ed, "_selectionStart").Paragraph);
        Assert.Same(innerCell.Para, Field(ed, "_selectionEnd").Paragraph);

        CtrlA(ed); // 2: the inner table
        Assert.Same(innerCell.Para, Field(ed, "_selectionStart").Paragraph);
        Assert.Same(inner.Cells[0][1].Para, Field(ed, "_selectionEnd").Paragraph);

        CtrlA(ed); // 3: the outer table — starts at the outer cell's own leading paragraph
        Assert.Same(outer.Cells[0][0].Blocks[0], Field(ed, "_selectionStart").Paragraph);
        Assert.Same(outer.Cells[0][1].Para, Field(ed, "_selectionEnd").Paragraph);

        CtrlA(ed); // 4: the document
        Assert.Same(doc.Blocks[0], Field(ed, "_selectionStart").Paragraph);
        Assert.Same(doc.Blocks[^1], Field(ed, "_selectionEnd").Paragraph);
    }

    [AvaloniaFact]
    public void CtrlA_InsideAnInlineTableInACell_ClimbsToTheHostsTable()
    {
        // An inline table hangs off a paragraph's inlines, not off a cell, so the climb has to go
        // through its host paragraph to reach the enclosing table instead of jumping to the document.
        var doc = new FlowDocument();
        var outer = new TableBlock(1, 2);
        var inline = TestHelpers.Tbl("t");
        outer.Cells[0][0].Para.Inlines.Add(inline);
        doc.Blocks.Add(outer);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Realize(ed);
        SetCaret(ed, inline.Table.Cells[0][0].Para, 0);

        CtrlA(ed); // 1: the inline table's cell (a 1x1 table, so its table stage is skipped)
        Assert.Same(inline.Table.Cells[0][0].Para, Field(ed, "_selectionStart").Paragraph);

        CtrlA(ed); // 2: the enclosing (outer) table, not the whole document
        Assert.Same(outer.Cells[0][0].Para, Field(ed, "_selectionStart").Paragraph);
        Assert.Same(outer.Cells[0][1].Para, Field(ed, "_selectionEnd").Paragraph);

        CtrlA(ed); // 3: the document
        Assert.Same(doc.Blocks[0], Field(ed, "_selectionStart").Paragraph);
        Assert.Same(doc.Blocks[^1], Field(ed, "_selectionEnd").Paragraph);
    }

    [AvaloniaFact]
    public void CtrlA_OutsideATable_SelectsTheDocumentAtOnce()
    {
        var ed = new RichEditor { PageSize = RichEditorPageSize.Continuous };
        ed.LoadHtml("<p>one</p><p>two</p>");
        Realize(ed);
        SetCaret(ed, (Paragraph)ed.Document!.Blocks[0], 0);

        CtrlA(ed);

        Assert.Same(ed.Document.Blocks[0], Field(ed, "_selectionStart").Paragraph);
        Assert.Same(ed.Document.Blocks[^1], Field(ed, "_selectionEnd").Paragraph);
    }

    [AvaloniaFact]
    public void ParseHtml_NeverOpensAConnectionForARemoteImage()
    {
        // The synchronous parse used to download remote images on the calling thread (up to a 5 s
        // budget), freezing the UI. It must now skip them outright — proven by pointing the <img> at a
        // local socket nobody answers and asserting nothing ever connects to it.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var doc = Formatters.HtmlDocumentFormatter.ParseHtml(
                $"<p>a<img src=\"http://127.0.0.1:{port}/x.png\">b</p>");

            Assert.False(listener.Pending(), "the synchronous parse must not perform network I/O");
            Assert.Empty(doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineImage>());
            Assert.Empty(doc.Blocks.OfType<ImageBlock>());
            Assert.Equal("ab", doc.Blocks.OfType<Paragraph>().First().Text()); // the rest is kept
        }
        finally { listener.Stop(); }
    }

    // ---- formatter / normalization defects (WinUI backport, verified here) --
    [AvaloniaFact]
    public void HtmlFontWeight_IsReadFromItsOwnValue()
    {
        // The old check searched the WHOLE style string for "bold"/":600"…, so an unrelated 600 in a
        // later declaration turned normal text bold.
        static FontWeight WeightOf(string style)
        {
            var doc = Formatters.HtmlDocumentFormatter.ParseHtml($"<p><span style=\"{style}\">x</span></p>");
            return doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Run>().First().FontWeight;
        }

        Assert.Equal(FontWeight.Normal, WeightOf("font-weight:normal;width:600px"));
        Assert.Equal(FontWeight.Bold, WeightOf("font-weight:bold"));
        Assert.Equal(FontWeight.Bold, WeightOf("font-weight: 650")); // numeric compare, not a fixed list
        Assert.Equal(FontWeight.Normal, WeightOf("font-weight:400"));
    }

    [AvaloniaFact]
    public void RtfPictureInsideATableRow_StaysInItsCell()
    {
        // A >=64px picture was spliced out as a document-level ImageBlock even mid-row, which pushed
        // the half-built cell paragraph into the body: the photo left the table and the text reordered.
        const string png1x1 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        string hex = System.Convert.ToHexString(System.Convert.FromBase64String(png1x1));
        // picwgoal/pichgoal are twips (/15 -> px): 1500 twips = 100 px, i.e. a block-sized picture.
        string rtf = "{\\rtf1\\ansi\\trowd\\cellx1500\\cellx3000 a{\\pict\\pngblip\\picwgoal1500\\pichgoal1500 "
                     + hex + "}\\cell b\\cell\\row\\par}";

        var doc = Formatters.RtfDocumentFormatter.Parse(rtf);

        Assert.Empty(doc.Blocks.OfType<ImageBlock>());
        var table = doc.Blocks.OfType<TableBlock>().Single();
        int inCell = table.Cells.SelectMany(r => r)
            .SelectMany(c => c.Blocks.OfType<Paragraph>())
            .SelectMany(p => p.Inlines).OfType<InlineImage>().Count();
        Assert.Equal(1, inCell);
    }

    [AvaloniaFact]
    public void InlineTableCells_AreNormalizedToHoldAParagraph()
    {
        // Rule #5 was applied to block-table cells only. An inline-table cell whose single block is an
        // image (as a deserialized .flow can be) kept no paragraph, so the caret could never enter it.
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        it.Table.Cells[0][0].Blocks.Clear();
        it.Table.Cells[0][0].Blocks.Add(new ImageBlock { Width = 30, Height = 30 });
        var host = TestHelpers.Para(new Run { Text = "a" }, it);
        var doc = new FlowDocument();
        doc.Blocks.Add(host);

        var ed = new RichEditor { Document = doc }; // assignment runs UpdateParents -> NormalizeBlocks

        Assert.Contains(it.Table.Cells[0][0].Blocks, b => b is Paragraph);
    }

    // ---- 2. resize handles must not flip IsModified on a bare click ---------
    private static readonly Avalonia.Input.Pointer TestPointer =
        new(1, Avalonia.Input.PointerType.Mouse, true);

    private static Avalonia.Input.PointerPointProperties LeftDown =>
        new(Avalonia.Input.RawInputModifiers.LeftMouseButton, Avalonia.Input.PointerUpdateKind.LeftButtonPressed);

    private static void Press(RichEditor ed, Point p) => ed.RaiseEvent(
        new Avalonia.Input.PointerPressedEventArgs(ed, TestPointer, ed, p, 0, LeftDown, Avalonia.Input.KeyModifiers.None));

    private static void Move(RichEditor ed, Point p) => ed.RaiseEvent(
        new Avalonia.Input.PointerEventArgs(Avalonia.Input.InputElement.PointerMovedEvent, ed, TestPointer, ed, p, 0,
            LeftDown, Avalonia.Input.KeyModifiers.None));

    private static void Release(RichEditor ed, Point p) => ed.RaiseEvent(
        new Avalonia.Input.PointerReleasedEventArgs(ed, TestPointer, ed, p, 0, LeftDown,
            Avalonia.Input.KeyModifiers.None, Avalonia.Input.MouseButton.Left));

    // A table tall enough that any y in the middle is inside it whatever the leading paragraph's
    // height is; its single internal column edge sits at x = 10 (block left) + 100 (column width).
    private static (RichEditor ed, TableBlock tb) TallTableEditor()
    {
        var tb = new TableBlock(1, 2);
        tb.RowHeights.Add(300);
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Realize(ed);
        ed.MarkSaved(); // assigning Document counts as a change; this is our clean baseline
        return (ed, tb);
    }

    private static readonly Point ColumnHandle = new(110, 200);

    [AvaloniaFact]
    public void ClickingAColumnHandle_WithoutDragging_LeavesTheDocumentUnmodified()
    {
        var (ed, tb) = TallTableEditor();
        double width = tb.ColumnWidths[0];

        Press(ed, ColumnHandle);
        Release(ed, ColumnHandle);

        Assert.False(ed.IsModified, "a click that resized nothing must not mark the document modified");
        Assert.Equal(width, tb.ColumnWidths[0]);
    }

    [AvaloniaFact]
    public void DraggingAColumnHandle_ResizesAndMarksModified()
    {
        var (ed, tb) = TallTableEditor();
        double width = tb.ColumnWidths[0];

        Press(ed, ColumnHandle);
        Move(ed, ColumnHandle + new Vector(30, 0));
        Release(ed, ColumnHandle + new Vector(30, 0));

        Assert.True(tb.ColumnWidths[0] > width, "test setup: the drag should have widened the column");
        Assert.True(ed.IsModified, "a real resize is an edit");
    }
}
