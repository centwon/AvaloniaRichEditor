using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Round 35 — a full audit (2026-10-01). Most of what it found is one axis the earlier rounds never measured: the
// SIZE and DEPTH of untrusted input. Nesting that recursion cannot survive, grids a few bytes can multiply into
// a billion cells, a picture header that claims gigabytes. Plus a selection that deleted a line it never reached.
public class Round35AuditTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static string Png1x1 => "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static IEnumerable<TableBlock> AllTables(FlowDocument doc)
    {
        foreach (var b in doc.Blocks)
            if (b is TableBlock t) yield return t;
            else if (b is Paragraph p) foreach (var it in p.Inlines.OfType<InlineTable>()) yield return it.Table;
    }

    private static int TableDepth(FlowDocument doc)
    {
        int levels = 0;
        for (Block? b = doc.Blocks.OfType<TableBlock>().FirstOrDefault(); b is TableBlock t; b = t.Cells[0][0].Blocks.OfType<TableBlock>().FirstOrDefault())
            levels++;
        return levels;
    }

    // ---- a selection into an inline table ------------------------------------------------------------------

    // Document order puts an inline table's cells after their host paragraph, so a selection from the line above
    // into such a cell had the whole host "between" its ends: Delete took the host's text after the table, and
    // the table itself, although the selection never reached either. Shift+arrow makes that selection.
    [AvaloniaFact]
    public void DeletingASelectionIntoAnInlineTableCell_KeepsWhatTheSelectionDidNotReach()
    {
        var t = new TableBlock(1, 1);
        var cell = t.Cells[0][0].Para;
        cell.Inlines.Add(new Run { Text = "cellText" });
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "hello" } } });
        doc.Blocks.Add(new Paragraph { Inlines = { new InlineTable { Table = t }, new Run { Text = " after" } } });
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        var ed = host.Editor;
        var p0 = (Paragraph)ed.Document!.Blocks[0];
        typeof(RichEditor).GetField("_caretPosition", NP)!.SetValue(ed, new TextPointer(p0, 2));
        typeof(RichEditor).GetField("_selectionStart", NP)!.SetValue(ed, new TextPointer(p0, 2));
        typeof(RichEditor).GetField("_selectionEnd", NP)!.SetValue(ed, new TextPointer(p0, 2));
        for (int i = 0; i < 3 + 1 + 1 + 4; i++) host.Key(Key.Right, RawInputModifiers.Shift); // "llo", the break, into the cell, "cell"
        Assert.Same(cell, host.Caret.Paragraph); // precondition: the selection really ends inside the cell
        Assert.Equal(4, host.Caret.Offset);

        host.Key(Key.Delete);

        string text = ed.GetPlainText();
        Assert.StartsWith("he", text);
        Assert.Contains("Text", text);   // the rest of the cell
        Assert.Contains("after", text);  // the host's text after the table
        Assert.Single(AllTables(ed.Document!));
    }

    [Fact]
    public void ASelectionIntoAnInlineTableCell_CopiesAndFormatsOnlyWhatItCovers()
    {
        var t = new TableBlock(1, 1);
        var cell = t.Cells[0][0].Para;
        cell.Inlines.Add(new Run { Text = "cellText" });
        var host = new Paragraph { Inlines = { new Run { Text = "before " }, new InlineTable { Table = t }, new Run { Text = " after" } } };
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "hello" } } });
        doc.Blocks.Add(host);
        typeof(RichEditor).GetMethod("UpdateParents", NP)!.Invoke(new RichEditor(), [doc]);
        var range = new TextRange(new TextPointer((Paragraph)doc.Blocks[0], 2), new TextPointer(cell, 4));

        string copied = range.GetText();
        Assert.Contains("before", copied);
        Assert.DoesNotContain("after", copied);

        range.ApplyPropertyValue(r => r.FontWeight = Avalonia.Media.FontWeight.Bold);
        var afterRun = host.Inlines.OfType<Run>().Single(r => r.Text!.Contains("after"));
        Assert.NotEqual(Avalonia.Media.FontWeight.Bold, afterRun.FontWeight);
    }

    // ---- depth: recursion no handler can catch ----------------------------------------------------------------

    // A StackOverflowException takes the process down — no catch in the paste path helps. 2,000 nested <div>s
    // (11 KB) did it; HtmlAgilityPack parses far deeper, and its own subtree removal recursed too.
    [AvaloniaTheory]
    [InlineData("div", 3000)]
    [InlineData("span", 3000)]
    [InlineData("blockquote", 3000)]
    [InlineData("div", 20000)] // the depth at which HtmlAgilityPack's own recursive subtree removal overflowed (~3 s)
    public void DeeplyNestedHtml_ParsesAndRenders_KeepingItsText(string tag, int depth)
    {
        string html = string.Concat(Enumerable.Repeat($"<{tag}>", depth)) + "deep text" + string.Concat(Enumerable.Repeat($"</{tag}>", depth));
        var doc = HtmlDocumentFormatter.ParseHtml(html);
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        Assert.Contains("deep text", host.Editor.GetPlainText());
    }

    [AvaloniaFact]
    public void DeeplyNestedHtmlLists_Parse_AtABoundedLevel()
    {
        var doc = HtmlDocumentFormatter.ParseHtml(string.Concat(Enumerable.Repeat("<ul><li>x", 3000)) + string.Concat(Enumerable.Repeat("</li></ul>", 3000)));
        Assert.All(doc.Blocks.OfType<Paragraph>(), p => Assert.InRange(p.ListLevel, 0, 8));
        Assert.Contains(doc.Blocks.OfType<Paragraph>(), p => p.IsListItem);
    }

    // Every table level is another round of the renderer's recursion: 150 levels rendered, 400 overflowed. The
    // readers bound it — RTF's \itap had no bound at all, and HTML only through its DOM depth.
    [AvaloniaFact]
    public void DeeplyNestedTables_FromRtf_AreBounded_AndRender()
    {
        const int depth = 400;
        var sb = new StringBuilder(@"{\rtf1\ansi ");
        for (int d = 1; d <= depth; d++) sb.Append(@"\pard\intbl\itap").Append(d).Append(' ');
        sb.Append("deep");
        for (int d = depth; d >= 2; d--)
            sb.Append(@"\nestcell{\*\nesttableprops\trowd\itap").Append(d).Append(@"\cellx1000\nestrow}{\nonesttables\par}\pard\intbl\itap").Append(d - 1).Append(' ');
        sb.Append(@"\cell\trowd\cellx2000\row\pard after\par}");

        var doc = RtfDocumentFormatter.Parse(sb.ToString());
        Assert.InRange(TableDepth(doc), 2, RtfParser.MaxNesting);
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        Assert.Contains("deep", host.Editor.GetPlainText());
    }

    [AvaloniaFact]
    public void DeeplyNestedTables_FromHtml_AreBounded_AndKeepTheirText()
    {
        const int depth = 300;
        var doc = HtmlDocumentFormatter.ParseHtml(string.Concat(Enumerable.Repeat("<table><tr><td>", depth)) + "deep text" + string.Concat(Enumerable.Repeat("</td></tr></table>", depth)));
        Assert.InRange(TableDepth(doc), 2, HtmlDocumentFormatter.MaxDomDepth / 3 + 1);
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        Assert.Contains("deep text", host.Editor.GetPlainText());
    }

    // ---- size: grids a few bytes multiply --------------------------------------------------------------------

    [Fact]
    public void Json_AWideRowCannotPadEveryRowPastTheBounds()
    {
        var sb = new StringBuilder("{\"Blocks\":[{\"Type\":\"Table\",\"Cells\":[");
        sb.Append('[').Append(string.Join(",", Enumerable.Repeat("{}", 1500))).Append(']');
        for (int i = 0; i < 3; i++) sb.Append(",[{}]");
        sb.Append("]}]}");
        var tb = DocumentSerializer.Deserialize(sb.ToString()).Blocks.OfType<TableBlock>().Single();
        Assert.Equal(TableBlock.MaxImportColumns, tb.Columns);
        Assert.All(tb.Cells, row => Assert.Equal(tb.Columns, row.Count));
    }

    [Fact]
    public void Rtf_CellBoundariesCannotMakeAHugeGrid()
    {
        var sb = new StringBuilder(@"{\rtf1\ansi ");
        for (int r = 0; r < 3; r++)
        {
            sb.Append(@"\trowd");
            if (r == 0) for (int c = 1; c <= 1500; c++) sb.Append(@"\cellx").Append(c * 20);
            else sb.Append(@"\cellx100");
            sb.Append(@"\pard\intbl x\cell\row ");
        }
        sb.Append(@"\pard after\par}");
        var tb = RtfDocumentFormatter.Parse(sb.ToString()).Blocks.OfType<TableBlock>().Single();
        Assert.True(tb.Columns <= TableBlock.MaxImportColumns, $"{tb.Columns} columns");
    }

    [Fact]
    public void Html_ManyRowsUnderAWideOne_StayWithinTheCellBound()
    {
        var html = new StringBuilder("<table><tr><td colspan=\"1000\">wide</td></tr>");
        for (int i = 0; i < 2000; i++) html.Append("<tr><td>r</td></tr>");
        html.Append("</table>");
        var tb = HtmlDocumentFormatter.ParseHtml(html.ToString()).Blocks.OfType<TableBlock>().Single();
        Assert.True((long)tb.Rows * tb.Columns <= TableBlock.MaxImportCells, $"{tb.Rows} x {tb.Columns}");
        Assert.Equal(2001, tb.Rows);
    }

    // A row wider than the cap: the cells past it were indexed anyway, and ArgumentOutOfRangeException came out
    // of ParseHtml.
    [Fact]
    public void Html_ARowWiderThanTheCap_Parses()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<table><tr>" + string.Concat(Enumerable.Repeat("<td>x</td>", 1001)) + "</tr></table>");
        Assert.Equal(TableBlock.MaxImportColumns, doc.Blocks.OfType<TableBlock>().Single().Columns);
    }

    [Fact]
    public void APackage_ReadsOnlyThePicturesItsDocumentUses()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            string json = "{\"Blocks\":[{\"Type\":\"Image\",\"ImageRef\":\"used\"}],\"Images\":{\"used\":{},\"unused\":{}}}";
            using (var s = zip.CreateEntry("document.json").Open()) s.Write(Encoding.UTF8.GetBytes(json));
            using (var s = zip.CreateEntry("images/used").Open()) s.Write(Convert.FromBase64String(Png1x1));
            using (var s = zip.CreateEntry("images/unused", CompressionLevel.Optimal).Open())
            {
                var zeros = new byte[1 << 20];
                for (int i = 0; i < 64; i++) s.Write(zeros); // 64 MB that deflates to ~64 KB
            }
        }
        ms.Position = 0;
        var (_, pool) = DocumentPackage.ReadPackage(ms);
        Assert.True(pool.ContainsKey("used"));
        Assert.False(pool.ContainsKey("unused"), "an unreferenced 64 MB entry was inflated into memory");
    }

    // ---- a picture header that claims gigabytes ----------------------------------------------------------------

    // Only the header matters to the check, so the test needs no real pixels.
    private static byte[] PngClaiming(int w, int h)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
        b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
        b[24] = 8;
        return b;
    }

    // Decoded, 40000x40000 took 1.6 GB (real Skia, Tests.Render TempBombProbe) from 1.5 MB of PNG. These are the
    // paths that decode: paste/import, the model's lazy Image, and the display cache.
    [AvaloniaFact]
    public void APictureClaimingMorePixelsThanTheCap_IsNotDecoded()
    {
        var bomb = PngClaiming(20000, 20000);
        Assert.True(ImageInfo.TooLargeToDecode(bomb)); // precondition

        var doc = HtmlDocumentFormatter.ParseHtml($"<p>x<img src=\"data:image/png;base64,{Convert.ToBase64String(bomb)}\" width=\"100\" height=\"100\"></p>");
        Assert.Empty(doc.Blocks.OfType<ImageBlock>());
        Assert.Empty(doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<InlineImage>()));

        var ib = new ImageBlock();
        ib.SetImageData(bomb, "image/png");
        Assert.Null(ib.Image);
        Assert.Same(bomb, ib.RawBytes); // kept: a save writes it back

        Assert.Null(new ImageDisplayCache().Get(bomb, null, 100, 100));
    }

    [AvaloniaFact]
    public void APictureWithinTheCap_StillImports()
    {
        var doc = HtmlDocumentFormatter.ParseHtml($"<p>x<img src=\"data:image/png;base64,{Png1x1}\" width=\"100\" height=\"100\"></p>");
        Assert.Single(doc.Blocks.OfType<ImageBlock>());
    }

    // ---- what reaches exported HTML ----------------------------------------------------------------------------

    // A picture's type from a JSON or .flow file went into <img src="data:…"> as it was.
    [Fact]
    public void AMimeTypeFromAFile_CannotInjectAnAttributeIntoExportedHtml()
    {
        string json = "{\"Blocks\":[{\"Type\":\"Image\",\"ImageRef\":\"k\"}],\"Images\":{\"k\":{\"Data\":\"" + Png1x1 +
                      "\",\"MimeType\":\"image/png\\\" onerror=\\\"alert(1)\"}}}";
        var doc = DocumentSerializer.Deserialize(json);
        // The model does not carry it either: MimeType is public, and a host may write it out itself.
        Assert.Equal("image/png", doc.Blocks.OfType<ImageBlock>().Single().MimeType);
        string html = HtmlDocumentFormatter.ToHtml(doc);
        Assert.DoesNotContain("onerror", html);
        Assert.Contains("src=\"data:image/png;base64,", html);
    }

    // And one a host puts on a picture itself, which no reader sees.
    [Fact]
    public void AMimeTypeSetByAHost_CannotInjectAnAttributeEither()
    {
        var ib = new ImageBlock();
        ib.SetImageData(Convert.FromBase64String(Png1x1), "image/png\" onerror=\"alert(1)");
        var doc = new FlowDocument();
        doc.Blocks.Add(ib);
        Assert.DoesNotContain("onerror", HtmlDocumentFormatter.ToHtml(doc));
    }

    [Fact]
    public void ScriptAndStyle_InsideAParagraph_AreNotContent()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<div>text<script>var secret=1;</script><style>.a{color:red}</style> more</div>");
        string all = string.Concat(doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).Select(r => r.Text));
        Assert.Contains("text", all);
        Assert.Contains("more", all);
        Assert.DoesNotContain("secret", all);
        Assert.DoesNotContain("color", all);
    }

    // -1 threw out of the HTML writer — and so out of every copy — and a million wrote a million <ul>.
    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    [InlineData(1_000_000)]
    public void AListLevelFromAFile_IsBounded_AndExports(int level)
    {
        var doc = DocumentSerializer.Deserialize("{\"Blocks\":[{\"Type\":\"Paragraph\",\"ListType\":\"Bullet\",\"ListLevel\":" + level + ",\"Inlines\":[{\"Text\":\"item\"}]}]}");
        Assert.InRange(((Paragraph)doc.Blocks[0]).ListLevel, 0, 8);
        Assert.True(HtmlDocumentFormatter.ToHtml(doc).Length < 2000);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000)]
    public void AListLevelSetByAHost_Exports(int level)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { ListType = ListKind.Bullet, ListLevel = level, Inlines = { new Run { Text = "item" } } });
        string? html = null;
        Assert.Null(Record.Exception(() => html = HtmlDocumentFormatter.ToHtml(doc)));
        Assert.True(html!.Length < 2000);
    }

    // ---- editor ------------------------------------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData(-1, 2)]
    [InlineData(2, -1)]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    public void InsertTable_WithANonPositiveSize_InsertsNothing(int rows, int cols)
    {
        var ed = new RichEditor { Document = new FlowDocument(), PageSize = RichEditorPageSize.Continuous };
        ed.FocusDocumentEnd();
        ed.InsertTable(rows, cols);
        ed.Measure(new Size(700, double.PositiveInfinity)); // a negative size overflowed an array here
        Assert.Empty(ed.Document!.Blocks.OfType<TableBlock>());
        Assert.False(ed.CanUndo);
    }

    // A table with no rows — from a file, or a host's model — held nothing the caret walks could take, and
    // arrowing into an inline one threw out of the key handler.
    [AvaloniaFact]
    public void ATableWithNoRows_IsNotInTheDocument_AndArrowsPastItsPlace()
    {
        var ed = new RichEditor { PageSize = RichEditorPageSize.Continuous };
        ed.LoadJson("{\"Blocks\":[{\"Type\":\"Paragraph\",\"Inlines\":[{\"Type\":\"Table\",\"Table\":{\"Type\":\"Table\",\"Cells\":[]}},{\"Text\":\"after\"}]}," +
                    "{\"Type\":\"Table\",\"Cells\":[]},{\"Type\":\"Paragraph\",\"Inlines\":[{\"Text\":\"end\"}]}]}");
        Assert.Empty(AllTables(ed.Document!));
        var host = InteractionHost.Create(ed);
        host.Render();
        typeof(RichEditor).GetField("_caretPosition", NP)!.SetValue(ed, new TextPointer((Paragraph)ed.Document!.Blocks[0], 0));
        Assert.Null(Record.Exception(() => { for (int i = 0; i < 12; i++) host.Key(Key.Right); for (int i = 0; i < 12; i++) host.Key(Key.Left); }));
    }

    [AvaloniaFact]
    public void AHostModelWithAnEmptyGrid_HasItDropped()
    {
        var empty = new TableBlock(1, 1);
        empty.Cells.Clear(); empty.Rows = 0;
        var inlineEmpty = new TableBlock(1, 1);
        inlineEmpty.Cells[0].Clear(); inlineEmpty.Columns = 0;
        var doc = new FlowDocument();
        doc.Blocks.Add(empty);
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "a" }, new InlineTable { Table = inlineEmpty } } });
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Assert.Empty(AllTables(ed.Document!));
        Assert.Null(Record.Exception(() => InteractionHost.Create(ed).Render()));
    }

    // Pasting ONE paragraph kept only its runs and pictures, so an inline table pasted with text around it —
    // from another instance of this editor, through the system clipboard's HTML — vanished.
    [AvaloniaFact]
    public void PastingOneParagraphWithAnInlineTable_KeepsTheTable()
    {
        var src = new FlowDocument();
        var t = new TableBlock(1, 1);
        t.Cells[0][0].Para.Inlines.Add(new Run { Text = "cell" });
        src.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "ab" }, new InlineTable { Table = t }, new Run { Text = "cd" } } });
        var ed = new RichEditor { Document = new FlowDocument(), PageSize = RichEditorPageSize.Continuous };
        ed.FocusDocumentEnd();

        ed.InsertHtml(HtmlDocumentFormatter.ToHtml(src));

        var host = Assert.Single(ed.Document!.Blocks.OfType<Paragraph>(), p => p.Inlines.OfType<InlineTable>().Any());
        Assert.Contains("ab", string.Concat(host.Inlines.OfType<Run>().Select(r => r.Text)));
        Assert.Contains("cd", string.Concat(host.Inlines.OfType<Run>().Select(r => r.Text)));
    }

    // A host opening a file by assigning Document left the previous file's edits undoable: Ctrl+Z put the OLD
    // file back, and the next save wrote it over the new one.
    [AvaloniaFact]
    public void AssigningANewDocument_StartsANewHistory()
    {
        var first = new FlowDocument();
        first.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "OLD FILE" } } });
        var host = InteractionHost.Create(new RichEditor { Document = first, PageSize = RichEditorPageSize.Continuous });
        host.Click(new Point(600, 8));
        host.Type("!");
        Assert.True(host.Editor.CanUndo); // precondition

        var second = new FlowDocument();
        second.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "NEW FILE" } } });
        host.Editor.Document = second;
        Assert.False(host.Editor.CanUndo);
        host.Key(Key.Z, RawInputModifiers.Control);
        Assert.DoesNotContain("OLD FILE", host.Editor.GetPlainText());

        // Undo and redo swap documents through the same property, and keep their history.
        host.Click(new Point(500, 8)); // not the spot clicked above: that would be a double-click, selecting a word
        host.Type("?");
        host.Key(Key.Z, RawInputModifiers.Control);
        Assert.True(host.Editor.CanRedo);
        host.Key(Key.Y, RawInputModifiers.Control);
        Assert.Equal("NEW FILE?", host.Editor.GetPlainText());
        Assert.True(host.Editor.CanUndo);
    }
}
