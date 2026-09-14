using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// What a file's table spans may be, a page setup that looks default, and the pool of a document's pictures. Measured
// in the WinUI port 2026-09-14, whose loader and page-setup capture were the same code: a negative span took its
// editor down on load, spans no merge explained hid cells' text from the editor and every export, "ColSpans":[null]
// and a null pool entry threw NullReferenceException instead of loading, and a Continuous page chosen under an A4
// host reopened as A4.
public class ModelContractTests
{
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static string TableJson(int rows, int cols, Action<JsonObject> mutate)
    {
        var tb = new TableBlock(rows, cols);
        foreach (var (r, c, cell) in tb.LogicalCells()) ((Run)cell.Para.Inlines[0]).Text = $"T{r}{c}";
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "before" } } });
        doc.Blocks.Add(tb);
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "after" } } });
        var root = JsonNode.Parse(DocumentSerializer.Serialize(doc))!.AsObject();
        mutate(root["Blocks"]!.AsArray().Select(b => b!.AsObject()).First(b => (string?)b["Type"] == "Table"));
        return root.ToJsonString();
    }

    private static JsonArray Grid(params int[][] rows)
        => new(rows.Select(r => (JsonNode?)new JsonArray(r.Select(v => (JsonNode?)v).ToArray())).ToArray());

    // Every slot is an anchor (both spans >= 1, its merge inside the grid) or covered (both 0), and lies in exactly
    // one anchor's merge.
    private static void AssertConsistent(TableBlock tb)
    {
        int rows = tb.Cells.Count;
        var claims = tb.Cells.Select(row => new int[row.Count]).ToArray();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < tb.Cells[r].Count; c++)
            {
                int cs = tb.ColSpans[r][c], rs = tb.RowSpans[r][c];
                if (cs == 0 && rs == 0) continue;
                Assert.True(cs >= 1 && rs >= 1, $"({r},{c}) spans {cs},{rs}");
                Assert.True(r + rs <= rows && c + cs <= tb.Cells[r].Count, $"({r},{c}) reaches out of the grid");
                for (int rr = r; rr < r + rs; rr++)
                    for (int cc = c; cc < c + cs; cc++)
                    {
                        if (rr != r || cc != c) Assert.True(tb.ColSpans[rr][cc] == 0 && tb.RowSpans[rr][cc] == 0, $"({rr},{cc}) is inside ({r},{c})'s merge");
                        claims[rr][cc]++;
                    }
            }
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < claims[r].Length; c++)
                Assert.True(claims[r][c] == 1, $"({r},{c}) is in {claims[r][c]} merges");
    }

    public static TheoryData<string> SpanGarbage => new()
    {
        "null-row", "colspan-overflow", "rowspan-overflow", "negative", "all-covered", "overlap", "pair-mismatch", "anchor-under-merge",
    };

    private static (int rows, int cols, Action<JsonObject> mutate) Garbage(string name) => name switch
    {
        "null-row" => (2, 2, t => t["ColSpans"] = new JsonArray(null, new JsonArray(1, 1))),
        "colspan-overflow" => (2, 2, t => t["ColSpans"] = Grid(new[] { 5, 1 }, new[] { 1, 1 })),
        "rowspan-overflow" => (2, 2, t => t["RowSpans"] = Grid(new[] { 3, 1 }, new[] { 1, 1 })),
        "negative" => (2, 2, t => t["ColSpans"] = Grid(new[] { -2, 1 }, new[] { 1, 1 })),
        "all-covered" => (2, 2, t => { t["ColSpans"] = Grid(new[] { 0, 0 }, new[] { 0, 0 }); t["RowSpans"] = Grid(new[] { 0, 0 }, new[] { 0, 0 }); }),
        "overlap" => (1, 3, t => { t["ColSpans"] = Grid(new[] { 2, 2, 1 }); t["RowSpans"] = Grid(new[] { 1, 1, 1 }); }),
        "pair-mismatch" => (2, 2, t => t["ColSpans"] = Grid(new[] { 0, 1 }, new[] { 1, 1 })),
        _ => (2, 2, t => { t["ColSpans"] = Grid(new[] { 2, 0 }, new[] { 0, 1 }); t["RowSpans"] = Grid(new[] { 2, 0 }, new[] { 0, 1 }); }),
    };

    [AvaloniaTheory, MemberData(nameof(SpanGarbage))]
    public void SpansFromAFile_LoadAsAConsistentGrid_HidingNoCellsText(string name)
    {
        var (rows, cols, mutate) = Garbage(name);
        string json = TableJson(rows, cols, mutate);

        var doc = DocumentSerializer.Deserialize(json);
        var tb = doc.Blocks.OfType<TableBlock>().Single();
        AssertConsistent(tb);
        var visible = tb.LogicalCells().Select(x => ((Run)x.cell.Para.Inlines[0]).Text!).ToList();
        string html = HtmlDocumentFormatter.ToHtml(doc), rtf = RtfDocumentFormatter.Write(doc);
        foreach (var t in visible) { Assert.Contains(t, html); Assert.Contains(t, rtf); }
        if (name is "all-covered" or "pair-mismatch" or "negative" or "null-row" or "anchor-under-merge")
            Assert.Equal(rows * cols, visible.Count);

        using var host = InteractionHost.Create(new RichEditor { PageSize = RichEditorPageSize.Continuous });
        host.Editor.LoadJson(json);
        host.Render();
        string text = host.Editor.GetPlainText();
        foreach (var t in visible) Assert.Contains(t, text);
    }

    [Fact]
    public void AConsistentGrid_IsLeftExactlyAsItWas()
    {
        var tb = new TableBlock(4, 4);
        tb.MergeCells(0, 0, 1, 1);
        tb.MergeCells(0, 2, 0, 3);
        tb.MergeCells(2, 1, 3, 1);
        string Spans() => string.Join(";", tb.ColSpans.Select(r => string.Join(",", r))) + "|" + string.Join(";", tb.RowSpans.Select(r => string.Join(",", r)));
        string before = Spans();

        tb.EnsureSpanConsistency();

        Assert.Equal(before, Spans());
        AssertConsistent(tb);
    }

    // A host's own model is trusted no more than a file — a table nested in a cell and an inline table included.
    [AvaloniaFact]
    public void AHostBuiltModelWithBadSpans_IsMadeConsistent_AndPaints()
    {
        var inner = new TableBlock(2, 2);
        inner.ColSpans[0][0] = -3;
        var outer = new TableBlock(1, 1);
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(inner);
        var inlineTable = new TableBlock(2, 2);
        inlineTable.RowSpans[1][1] = -1;
        var doc = new FlowDocument();
        doc.Blocks.Add(outer);
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "x" }, new InlineTable { Table = inlineTable } } });
        doc.Blocks.Add(new Paragraph());

        using var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();

        AssertConsistent(inner);
        AssertConsistent(inlineTable);
    }

    [Fact]
    public void ANullPoolEntry_LoadsTheDocument_WithoutThatPicture()
    {
        var root = JsonNode.Parse(TableJson(1, 1, _ => { }))!.AsObject();
        root["Images"] = new JsonObject { ["k"] = null };
        root["Blocks"]![0]!["Inlines"] = new JsonArray(new JsonObject { ["Type"] = "Image", ["ImageRef"] = "k" }, new JsonObject { ["Type"] = "Run", ["Text"] = "kept" });

        var doc = DocumentSerializer.Deserialize(root.ToJsonString());

        var p = (Paragraph)doc.Blocks[0];
        Assert.Null(Assert.IsType<InlineImage>(p.Inlines[0]).RawBytes);
        Assert.Equal("kept", ((Run)p.Inlines[1]).Text);
    }

    [Fact]
    public void ANullPoolEntry_InAPackage_LoadsThePictureAnyway()
    {
        var doc = new FlowDocument();
        var img = new ImageBlock();
        img.SetImageData(Convert.FromBase64String(OnePixelPng), "image/png");
        doc.Blocks.Add(new Paragraph());
        doc.Blocks.Add(img);
        doc.Blocks.Add(new Paragraph());
        using var ms = new MemoryStream();
        DocumentPackage.Save(doc, ms);

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("document.json")!;
            JsonObject root;
            using (var s = entry.Open()) root = JsonNode.Parse(s)!.AsObject();
            foreach (var key in root["Images"]!.AsObject().Select(kv => kv.Key).ToList()) root["Images"]![key] = null;
            entry.Delete();
            using var w = new StreamWriter(zip.CreateEntry("document.json").Open());
            w.Write(root.ToJsonString());
        }
        ms.Position = 0;

        var back = DocumentPackage.Load(ms).Blocks.OfType<ImageBlock>().Single();

        Assert.NotNull(back.RawBytes);
        Assert.Equal("image/png", back.MimeType);
    }

    [AvaloniaFact]
    public void Continuous_ChosenUnderAnA4Host_SurvivesSaveAndReopen()
    {
        var ed = new RichEditor { PageSize = RichEditorPageSize.A4 };
        ed.LoadHtml("<p>x</p>");
        typeof(RichEditor).GetMethod("EditDocumentPageSetup", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ed, new object[] { (Action)(() => { ed.PageSize = RichEditorPageSize.Continuous; ed.ShowPageBoundaries = false; }) });
        string json = ed.ToJson();

        ed.LoadJson(json);
        Assert.Equal(RichEditorPageSize.Continuous, ed.PageSize);
        var other = new RichEditor { PageSize = RichEditorPageSize.A4 };
        other.LoadJson(json);
        Assert.Equal(RichEditorPageSize.Continuous, other.PageSize);
    }

    [AvaloniaFact]
    public void APlainDocument_InAPlainHost_IsWrittenWithoutAPageSetup()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>x</p>");

        Assert.Null(ed.Document!.PageSetup);
        Assert.DoesNotContain("PageSetup", ed.ToJson());
    }
}
