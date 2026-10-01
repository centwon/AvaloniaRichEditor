using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The writer leaves out what every reader assumes (2026-10-01). A run spent ~90 bytes on "Italic":false,
// "Underline":false,…; Korean went out as \uXXXX; and every document was indented. Measured on tests/corpus:
// 240.6 KB -> 37.7 KB, 3,043 KB -> 378 KB. Nothing a reader does changed, and the 1.0-1.3 readers were checked
// against the new output directly: an OLD tree read 18 documents (the fixpoint set + corpus pages) written lean
// and produced exactly what it produces from its own output, as JSON and as .flow.
public class LeanJsonFormatTests
{
    private static FlowDocument Build(string name)
        => (FlowDocument)typeof(FormatFixpointTests).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [name])!;

    private static FlowDocument OneParagraph(params Inline[] inlines)
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Inlines.AddRange(inlines);
        doc.Blocks.Add(p);
        return doc;
    }

    [Fact]
    public void APlainRun_WritesItsText_AndNothingElse()
    {
        string json = DocumentSerializer.Serialize(OneParagraph(new Run { Text = "안녕" }));
        Assert.Equal("{\"Version\":\"1.0\",\"Blocks\":[{\"Inlines\":[{\"Text\":\"안녕\"}]}]}", json);
        // And reading it back fills in what was left out.
        var p = (Paragraph)DocumentSerializer.Deserialize(json).Blocks[0];
        var r = (Run)p.Inlines[0];
        Assert.Equal((10.0, Avalonia.Media.FontWeight.Normal, Avalonia.Media.FontStyle.Normal), (r.FontSize, r.FontWeight, r.FontStyle));
        Assert.Null(r.TextDecorations);
        Assert.Equal((0, ListKind.None, Avalonia.Media.TextAlignment.Left, 0.0, false), (p.HeadingLevel, p.ListType, p.TextAlignment, p.Indent, p.IsQuote));
    }

    // Letters stay as they are; what an HTML page is sensitive to is still escaped, so the JSON can be embedded.
    [Fact]
    public void KoreanIsWrittenAsItIs_AndHtmlSensitiveCharactersAreNot()
    {
        string json = DocumentSerializer.Serialize(OneParagraph(new Run { Text = "정보공시 </script> & 'x'" }));
        Assert.Contains("정보공시", json);
        Assert.DoesNotContain("\\uC815", json);
        Assert.DoesNotContain("</script>", json);
        Assert.DoesNotContain("&", json.Replace("\\u0026", ""));
        Assert.Equal("정보공시 </script> & 'x'", ((Run)((Paragraph)DocumentSerializer.Deserialize(json).Blocks[0]).Inlines[0]).Text);
    }

    [Fact]
    public void WhatDiffersFromTheDefault_IsWritten()
    {
        var doc = OneParagraph(new Run { Text = "b", FontWeight = Avalonia.Media.FontWeight.Bold, FontStyle = Avalonia.Media.FontStyle.Italic, FontSize = 12 });
        var p = (Paragraph)doc.Blocks[0];
        p.HeadingLevel = 2; p.ListType = ListKind.Ordered; p.ListLevel = 1; p.IsQuote = true; p.Indent = 20;
        p.TextAlignment = Avalonia.Media.TextAlignment.Center; p.MarginRight = 5; p.MarginTop = 3;
        string json = DocumentSerializer.Serialize(doc);
        foreach (var field in new[] { "\"Bold\":true", "\"Italic\":true", "\"FontSize\":12", "\"HeadingLevel\":2", "\"ListType\":\"Ordered\"",
                                      "\"ListLevel\":1", "\"IsQuote\":true", "\"Indent\":20", "\"TextAlignment\":\"Center\"", "\"MarginRight\":5", "\"MarginTop\":3" })
            Assert.Contains(field, json);
        Assert.Equal(json, DocumentSerializer.Serialize(DocumentSerializer.Deserialize(json)));
    }

    // Each block kind reads its own default for an absent bottom margin (paragraph 0, picture/table 10): what equals
    // it is left out, anything else is written.
    [Fact]
    public void BlockMargins_AreLeftOutOnlyAtTheirOwnDefault()
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "p" } } });
        doc.Blocks.Add(new TableBlock(1, 1));
        doc.Blocks.Add(new TableBlock(1, 1) { MarginBottom = 0 });
        doc.Blocks.Add(new DividerBlock { MarginBottom = 10 });
        var back = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));
        Assert.Equal(10, back.Blocks.OfType<TableBlock>().First().MarginBottom);
        Assert.Equal(0, back.Blocks.OfType<TableBlock>().Last().MarginBottom);
        Assert.Equal(10, back.Blocks.OfType<DividerBlock>().Single().MarginBottom);
    }

    [Fact]
    public void OnlyAMergedTable_CarriesItsSpanGrids()
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(new TableBlock(2, 2));
        Assert.DoesNotContain("ColSpans", DocumentSerializer.Serialize(doc));
        ((TableBlock)doc.Blocks[0]).MergeCells(0, 0, 0, 1);
        string merged = DocumentSerializer.Serialize(doc);
        Assert.Contains("\"ColSpans\"", merged);
        Assert.Equal((2, 1), DocumentSerializer.Deserialize(merged).Blocks.OfType<TableBlock>().Single().SpanOf(0, 0));
    }

    // ---- one document, one spelling, in both editors (the port writes the same) ------------------------------

    // Color.ToString() spells a known colour by NAME ("Red") and anything else as lowercase hex; the format says
    // #AARRGGBB, which is what the port writes. Both readers take either, so this is about the bytes.
    [AvaloniaFact]
    public void Colours_AreWrittenAsUppercaseArgbHex()
    {
        var red = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Colors.Red);
        var other = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromArgb(0x80, 0x12, 0xAB, 0xCD));
        string json = DocumentSerializer.Serialize(OneParagraph(new Run { Text = "a", Foreground = red, Background = other }));
        Assert.Contains("\"Foreground\":\"#FFFF0000\"", json);
        Assert.Contains("\"Background\":\"#8012ABCD\"", json);
        var r = (Run)((Paragraph)DocumentSerializer.Deserialize(json).Blocks[0]).Inlines[0];
        Assert.Equal(Avalonia.Media.Colors.Red, ((Avalonia.Media.ISolidColorBrush)r.Foreground!).Color);
    }

    // A file may split one line into any number of runs; it loads as one model whatever the split (as HTML and
    // RTF already did, and as the port does).
    [AvaloniaFact]
    public void EqualRunsInAFile_LoadAsOne()
    {
        const string split = "{\"Version\":\"1.0\",\"Blocks\":[{\"Inlines\":[{\"Text\":\"ab\"},{\"Text\":\"cd\"},{\"Text\":\"e\",\"Bold\":true},{\"Text\":\"f\",\"Bold\":true}]}]}";
        var p = (Paragraph)DocumentSerializer.Deserialize(split).Blocks[0];
        Assert.Equal(new[] { "abcd", "ef" }, p.Inlines.OfType<Run>().Select(r => r.Text));
    }

    // The interchange contract with the port: one document using every field, in the canonical form. The port
    // (WinUIRichEditor) holds the same file and the same test, so a change to what either editor writes — a
    // field's order, a colour's spelling, a default — fails in the repository that made it.
    [AvaloniaFact]
    public void TheInterchangeDocument_ReadsAndWritesBackByteForByte()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "format-1.0-interchange.json");
        string canonical = File.ReadAllText(path);
        string written = DocumentSerializer.Serialize(DocumentSerializer.Deserialize(canonical));
        if (written != canonical) File.WriteAllText(Path.Combine(Path.GetTempPath(), "interchange-actual.json"), written);
        Assert.Equal(canonical, written);
    }

    // The format as 1.0-1.3 wrote it — every field, indented — written by the tree before this change from the
    // fixpoint suite's kitchen-sink document. Reading it must give what the document is.
    [AvaloniaFact]
    public void TheVerboseFormatEarlierVersionsWrote_StillReadsWhole()
    {
        string verbose = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "format-1.0-verbose-kitchen-sink.json"));
        Assert.Contains("\"Italic\": false", verbose); // precondition: it is the old, verbose form
        Assert.Equal(DocumentSerializer.Serialize(DocumentSerializer.Deserialize(DocumentSerializer.Serialize(Build("kitchen-sink")))),
                     DocumentSerializer.Serialize(DocumentSerializer.Deserialize(verbose)));
    }

    [AvaloniaFact]
    public void APackagesDocumentJson_IsLeanToo()
    {
        using var ms = new MemoryStream();
        DocumentPackage.Save(Build("kitchen-sink"), ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("document.json")!.Open());
        string json = reader.ReadToEnd();
        Assert.DoesNotContain("\"Italic\":false", json);
        Assert.DoesNotContain("\n", json);
    }

    // NaN — a host's AutoTopMargin put on a paragraph — has no JSON spelling: writing it threw.
    [Fact]
    public void AParagraphWithAnAutoTopMargin_Serializes()
    {
        var doc = OneParagraph(new Run { Text = "x" });
        ((Paragraph)doc.Blocks[0]).MarginTop = Block.AutoTopMargin;
        string? json = null;
        Assert.Null(Record.Exception(() => json = DocumentSerializer.Serialize(doc)));
        Assert.Equal(0, ((Paragraph)DocumentSerializer.Deserialize(json!).Blocks[0]).MarginTop);
    }

    // ---- versions ------------------------------------------------------------------------------------------

    // A newer MAJOR format would come in with what this reader does not know turned into empty paragraphs, and a
    // save would write that over the file. The loading paths refuse it, like a damaged file.
    [AvaloniaFact]
    public async System.Threading.Tasks.Task ANewerMajorFormat_IsRefusedByTheLoadingPaths()
    {
        const string newer = "{\"Version\":\"2.0\",\"Blocks\":[{\"Inlines\":[{\"Text\":\"x\"}]}]}";
        var ed = new RichEditor();
        ed.LoadJson("{\"Version\":\"1.0\",\"Blocks\":[{\"Inlines\":[{\"Text\":\"kept\"}]}]}");
        Assert.Throws<JsonException>(() => ed.LoadJson(newer));
        Assert.Contains("kept", ed.GetPlainText()); // the open document is left alone
        await Assert.ThrowsAsync<JsonException>(() => ed.LoadJsonAsync(newer)); // awaited: blocking would deadlock the UI thread
        // The public Deserialize stays lenient, as it always was.
        Assert.Equal("x", ((Paragraph)DocumentSerializer.Deserialize(newer).Blocks[0]).Text());
    }

    [AvaloniaTheory]
    [InlineData("\"1.0\"")]
    [InlineData("\"1.9\"")]
    [InlineData("1")] // the legacy integers predate "1.0"
    [InlineData("2")]
    public void OlderAndSameMajorFormats_Load(string version)
    {
        var ed = new RichEditor();
        ed.LoadJson("{\"Version\":" + version + ",\"Blocks\":[{\"Inlines\":[{\"Text\":\"x\"}]}]}");
        Assert.Contains("x", ed.GetPlainText());
    }

    // Within the same major, a block type this reader does not know is read as text — and said so, since saving
    // then drops it.
    [Fact]
    public void AnUnknownBlockType_IsReported()
    {
        RichEditorDiagnostics.Reset();
        Exception? seen = null;
        void Handler(object? s, RichEditorFaultEventArgs e) { if (e.Exception is NotSupportedException) seen = e.Exception; }
        RichEditorDiagnostics.Fault += Handler;
        try
        {
            var doc = DocumentSerializer.Deserialize("{\"Version\":\"1.4\",\"Blocks\":[{\"Type\":\"Chart\",\"Inlines\":[{\"Text\":\"caption\"}]}]}");
            Assert.Equal("caption", ((Paragraph)doc.Blocks[0]).Text());
        }
        finally { RichEditorDiagnostics.Fault -= Handler; }
        Assert.NotNull(seen);
        Assert.Contains("Chart", seen!.Message);
    }
}
