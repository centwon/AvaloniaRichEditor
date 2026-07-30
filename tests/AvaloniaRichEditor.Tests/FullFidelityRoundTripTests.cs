using System.IO;
using System.Linq;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A "kitchen sink" document driven through every persistence format, for the 1.0 sweep. The
// per-feature tests each cover one axis; this one checks the axes still hold when milestone A
// (blocks in cells, nested tables) and milestone B (inline tables) content is present at once,
// which is the combination a real user document reaches and no single-axis test builds.
public class FullFidelityRoundTripTests
{
    // 1x1 PNG, so image bytes survive a format that must decode or re-encode them.
    private static readonly byte[] Png = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static FlowDocument Sink()
    {
        var doc = new FlowDocument();

        doc.Blocks.Add(new Paragraph
        {
            HeadingLevel = 2,
            TextAlignment = TextAlignment.Center,
            Inlines = { new Run { Text = "Heading" } }
        });

        doc.Blocks.Add(new Paragraph
        {
            ListType = ListKind.Ordered,
            ListMarker = ListMarkerStyle.LowerAlpha,
            LineSpacing = 1.5,
            Indent = 24,
            Inlines =
            {
                new Run { Text = "bold", FontWeight = FontWeight.Bold },
                new Run { Text = "red", Foreground = Brushes.Red, FontSize = 18 },
                new Run { Text = "link", NavigateUri = "https://example.com/a?b=1" },
            }
        });

        // Outer table: merged first row, a shaded cell, a cell holding two paragraphs plus a
        // nested table, and a cell holding a block image.
        var outer = new TableBlock(2, 2);
        outer.Cells[0][0].Para.Inlines.Add(new Run { Text = "merged" });
        outer.MergeCells(0, 0, 0, 1);
        outer.Cells[1][0].Background = Brushes.LightYellow;
        outer.Cells[1][0].Para.Inlines.Add(new Run { Text = "first" });
        outer.Cells[1][0].Blocks.Add(new Paragraph { Inlines = { new Run { Text = "second" } } });
        var nested = new TableBlock(1, 2);
        nested.Cells[0][0].Para.Inlines.Add(new Run { Text = "n00" });
        nested.Cells[0][1].Para.Inlines.Add(new Run { Text = "n01" });
        outer.Cells[1][0].Blocks.Add(nested);
        // A paragraph AFTER the nested table: the order must survive, and RTF must reopen the cell's
        // own nesting level before writing it (Word drops it otherwise).
        outer.Cells[1][0].Blocks.Add(new Paragraph { Inlines = { new Run { Text = "afterNested" } } });
        var cellImg = new ImageBlock { Width = 40, Height = 40 };
        cellImg.SetImageData(Png, "image/png");
        outer.Cells[1][1].Blocks.Add(cellImg);
        doc.Blocks.Add(outer);

        // Host paragraph carrying an inline table between two text runs.
        var inlineTbl = new InlineTable { Table = new TableBlock(1, 2) };
        inlineTbl.Table.Cells[0][0].Para.Inlines.Add(new Run { Text = "i0" });
        inlineTbl.Table.Cells[0][1].Para.Inlines.Add(new Run { Text = "i1" });
        var host = new Paragraph();
        host.Inlines.Add(new Run { Text = "before" });
        host.Inlines.Add(inlineTbl);
        host.Inlines.Add(new Run { Text = "after" });
        doc.Blocks.Add(host);

        doc.Blocks.Add(new DividerBlock());
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "tail" } } });
        return doc;
    }

    private static string AllText(FlowDocument d)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(System.Collections.Generic.IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
            {
                if (b is Paragraph p)
                {
                    foreach (var inl in p.Inlines)
                    {
                        if (inl is Run r && r.Text != null) sb.Append(r.Text);
                        else if (inl is InlineTable it)
                            foreach (var (_, _, c) in it.Table.LogicalCells()) Walk(c.Blocks);
                    }
                }
                else if (b is TableBlock tb)
                    foreach (var (_, _, c) in tb.LogicalCells()) Walk(c.Blocks);
            }
        }
        Walk(d.Blocks);
        return sb.ToString();
    }

    private static void AssertTextSurvives(FlowDocument d, params string[] fragments)
    {
        string all = AllText(d);
        foreach (var f in fragments) Assert.Contains(f, all);
    }

    // JSON is the lossless format: everything must come back, structure included.
    [Fact]
    public void Json_PreservesEveryStructure()
    {
        var d = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(Sink()));

        AssertTextSurvives(d, "Heading", "boldredlink", "merged", "first", "second",
                              "n00", "n01", "before", "i0", "i1", "after", "tail");

        var outer = d.Blocks.OfType<TableBlock>().Single();
        Assert.Equal((2, 1), outer.SpanOf(0, 0));                                   // merge
        Assert.NotNull(outer.Cells[1][0].Background);                               // cell shading
        Assert.Equal(4, outer.Cells[1][0].Blocks.Count);                            // para, para, table, para
        Assert.IsType<TableBlock>(outer.Cells[1][0].Blocks[2]);                     // nested table
        Assert.Equal("afterNested", ((Paragraph)outer.Cells[1][0].Blocks[3]).Text()); // order preserved
        var img = outer.Cells[1][1].Blocks.OfType<ImageBlock>().Single();
        Assert.Equal(Png, img.RawBytes);                                            // image bytes

        var host = d.Blocks.OfType<Paragraph>().Single(p => p.Inlines.OfType<InlineTable>().Any());
        Assert.Equal(3, host.Inlines.Count);                                        // run, table, run
        Assert.Single(d.Blocks.OfType<DividerBlock>());

        var styled = d.Blocks.OfType<Paragraph>().First(p => p.ListType == ListKind.Ordered);
        Assert.Equal(ListMarkerStyle.LowerAlpha, styled.ListMarker);
        Assert.Equal(1.5, styled.LineSpacing, 3);
        Assert.Equal(24, styled.Indent, 3);
        Assert.Equal("https://example.com/a?b=1",
            styled.Inlines.OfType<Run>().Single(r => r.Text == "link").NavigateUri);
    }

    // Serializing what we just read back must be byte-identical — otherwise a save/load cycle
    // silently rewrites the user's file.
    [Fact]
    public void Json_IsIdempotentForTheWholeSink()
    {
        string a = DocumentSerializer.Serialize(Sink());
        string b = DocumentSerializer.Serialize(DocumentSerializer.Deserialize(a));
        Assert.Equal(a, b);
    }

    // The .flow package pulls image bytes into the zip's image pool; the document must come back whole.
    [Fact]
    public void FlowPackage_PreservesEveryStructure()
    {
        using var ms = new MemoryStream();
        DocumentPackage.Save(Sink(), ms);
        ms.Position = 0;
        var d = DocumentPackage.Load(ms);

        AssertTextSurvives(d, "merged", "second", "n00", "before", "i1", "tail");
        var outer = d.Blocks.OfType<TableBlock>().Single();
        Assert.Equal(Png, outer.Cells[1][1].Blocks.OfType<ImageBlock>().Single().RawBytes);
        Assert.IsType<TableBlock>(outer.Cells[1][0].Blocks[2]);
    }

    // HTML is lossy by nature, but the structural claims the README/CHANGELOG make must hold:
    // nested tables, multi-block cells, and inline tables (via data-are-inline) all come back.
    [Fact]
    public void Html_PreservesNestedAndInlineTables()
    {
        var d = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(Sink()));

        AssertTextSurvives(d, "Heading", "merged", "first", "second", "n00", "n01",
                              "before", "i0", "i1", "after", "tail");

        var outer = d.Blocks.OfType<TableBlock>().Single();
        Assert.Equal((2, 1), outer.SpanOf(0, 0));
        Assert.Contains(outer.Cells[1][0].Blocks, b => b is TableBlock);

        // The inline table stays inline, on the same paragraph as the text around it.
        var host = d.Blocks.OfType<Paragraph>().Single(p => p.Inlines.OfType<InlineTable>().Any());
        Assert.Equal("beforeafter", string.Concat(host.Inlines.OfType<Run>().Select(r => r.Text)));
    }

    // RTF writing is deliberately wider than reading (merges/shading are export-only). What must
    // hold is that no TEXT is lost in either direction — the failure mode the P3 audit found twice.
    [Fact]
    public void Rtf_LosesNoTextInEitherDirection()
    {
        string rtf = RtfDocumentFormatter.Write(Sink());
        var d = RtfDocumentFormatter.Parse(rtf);

        // The inline table splits its host paragraph on export (RTF has no inline table), so
        // "before"/"after" land on separate paragraphs — but both must still be there.
        AssertTextSurvives(d, "Heading", "merged", "first", "second", "n00", "n01",
                              "before", "i0", "i1", "after", "tail");
    }

    // Found by opening the export in Word and HWP: the writer never terminated the paragraph that
    // preceded a nested table, so Word glued the parent cell's text onto the inner table's first cell
    // ("...형제 문단)중첩1").
    [Fact]
    public void Rtf_ClosesTheParentParagraphBeforeANestedTable()
    {
        string rtf = RtfDocumentFormatter.Write(Sink());
        int nest = rtf.IndexOf(@"\nestcell", System.StringComparison.Ordinal);
        Assert.True(nest > 0, "expected a nested table in the output");

        // Between the parent cell's last text and the first nested cell there must be a \par.
        int cellStart = rtf.LastIndexOf(@"\trowd", nest, System.StringComparison.Ordinal);
        string between = rtf.Substring(cellStart, nest - cellStart);
        Assert.Contains(@"\par", between);
    }

    // Same session: a paragraph written AFTER a nested table stayed at the inner table's \itap, so
    // Word discarded it outright ("② 중첩 표 뒤 문단" was missing from the Word render).
    [Fact]
    public void Rtf_ReopensTheCellLevelAfterANestedTable()
    {
        string rtf = RtfDocumentFormatter.Write(Sink());
        int lastNestRow = rtf.LastIndexOf(@"\nestrow", System.StringComparison.Ordinal);
        int afterText = rtf.IndexOf("afterNested", lastNestRow, System.StringComparison.Ordinal);
        Assert.True(afterText > lastNestRow, "expected the post-nested-table paragraph in the output");

        // The cell's own level must be re-declared between the inner table and that text, and it must
        // not be the inner table's level.
        string between = rtf.Substring(lastNestRow, afterText - lastNestRow);
        int reopen = between.IndexOf(@"\pard\intbl", System.StringComparison.Ordinal);
        Assert.True(reopen >= 0, @"expected \pard\intbl between the nested table and the text after it");
        Assert.DoesNotContain(@"\itap2", between.Substring(reopen));
    }

    // Word and HWP drew every exported table with no lines at all — the grid was there but invisible,
    // so the document did not look like the one on screen until borders were applied by hand.
    [Fact]
    public void Rtf_EmitsCellBorders()
    {
        string rtf = RtfDocumentFormatter.Write(Sink());
        foreach (var cw in new[] { @"\clbrdrt", @"\clbrdrl", @"\clbrdrb", @"\clbrdrr" })
            Assert.Contains(cw, rtf);
    }

    // An inline table exported at width:100% became a full-width band on its own line in browsers and
    // Word — everywhere except our own importer, which reads the marker.
    [Fact]
    public void Html_SizesAnInlineTableToItsColumns_NotFullWidth()
    {
        string html = HtmlDocumentFormatter.ToHtml(Sink());
        int at = html.IndexOf("data-are-inline", System.StringComparison.Ordinal);
        Assert.True(at > 0, "expected the inline-table marker");
        string tag = html.Substring(at, html.IndexOf('>', at) - at);

        Assert.DoesNotContain("width:100%", tag);
        Assert.Contains("display:inline-table", tag);
        // Block tables still fill the column.
        Assert.Contains("width:100%", html);
    }

    // The writer has always emitted merge flags and cell shading (Word and HWP honour them), but the
    // reader ignored both — so our own export came back with the grid un-merged and the colours gone,
    // losing more through our own round trip than through Word's.
    [Fact]
    public void Rtf_ReadsBackCellMergesAndShading()
    {
        var d = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(Sink()));
        var outer = d.Blocks.OfType<TableBlock>().First();

        Assert.Equal((2, 1), outer.SpanOf(0, 0));       // horizontal merge on the first row
        Assert.NotNull(outer.Cells[1][0].Background);   // cell shading
    }

    [Fact]
    public void Rtf_ReadsBackAVerticalMerge()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(2, 2);
        tb.ColumnWidths[0] = 100; tb.ColumnWidths[1] = 100;
        tb.Cells[0][1].Para.Inlines.Add(new Run { Text = "tall" });
        tb.MergeCells(0, 1, 1, 1);
        doc.Blocks.Add(tb);

        var d = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(doc));
        var back = d.Blocks.OfType<TableBlock>().Single();
        Assert.Equal((1, 2), back.SpanOf(0, 1));
    }

    // RTF has no inline table, so one goes out as a block table preceded by our own ignorable
    // {\*\arinline} marker. Other readers skip the marker and see the block table as before; ours
    // reads it and puts the table back on the text line.
    [Fact]
    public void Rtf_RestoresAnInlineTableOntoItsTextLine()
    {
        var d = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(Sink()));

        var host = d.Blocks.OfType<Paragraph>().Single(p => p.Inlines.OfType<InlineTable>().Any());
        Assert.Equal("beforeafter", string.Concat(host.Inlines.OfType<Run>().Select(r => r.Text)));
        var inner = host.Inlines.OfType<InlineTable>().Single().Table;
        Assert.Equal(2, inner.Columns);
    }

    // The marker must not leak into what other applications see: it is an ignorable destination, and
    // the table itself still goes out as an ordinary block-level table.
    [Fact]
    public void Rtf_InlineTableMarkerIsAnIgnorableDestination()
    {
        string rtf = RtfDocumentFormatter.Write(Sink());
        Assert.Contains(@"{\*\arinline}", rtf);
        // Foreign RTF (no marker) still imports an inline-looking table as a block table.
        var plain = RtfDocumentFormatter.Parse(rtf.Replace(@"{\*\arinline}", ""));
        Assert.DoesNotContain(plain.Blocks.OfType<Paragraph>(), p => p.Inlines.OfType<InlineTable>().Any());
    }

    // The 1.0 README describes RTF import as flattening nested tables. Since the P3 follow-up it
    // reads \nestcell/\nestrow back as a real nested table, so a table inside a cell survives.
    [Fact]
    public void Rtf_ReadsANestedTableBackAsANestedTable()
    {
        var d = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(Sink()));

        bool anyNested = d.Blocks.OfType<TableBlock>()
            .SelectMany(t => t.LogicalCells())
            .Any(x => x.cell.Blocks.OfType<TableBlock>().Any());
        Assert.True(anyNested, "a table inside a cell should come back nested, not flattened");
    }
}
