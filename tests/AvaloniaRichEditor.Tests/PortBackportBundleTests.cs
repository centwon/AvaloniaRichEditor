using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Backported 2026-09-12 from the WinUI port's four verification rounds (editor commands, feature flags,
// document API wrappers, hyperlinks). Every item here was checked against this code first and found
// identical — a shared defect or a shared design the port changed by user decision:
//   - the caret format reported the RAW run size (toolbar "10" for text drawn at 14/20; "larger" shrank it)
//   - an armed format painter survived a document swap
//   - the Tab key never auto-linked; a balanced closing bracket was cut off a URL
//   - LoadJson / LoadJsonAsync / LoadPackageAsync replaced the open document with an EMPTY one for JSON
//     that is not a document of this library, or a zip with no document.json
//   - a document with no page setup (and Clear) inherited the PREVIOUS document's page setup
//   - rich content inserted into an editor with AllowImages / AllowTables off kept its pictures and tables
// Paste is exercised through InsertHtml, which funnels into the same InsertParsedDocument as HTML/RTF paste
// (this suite has no real clipboard — see RichEditorClipboardTests).
public class PortBackportBundleTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly string PngUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGP8//8/AzJgYkAD5AsAAP//A+8DTgn2rL0AAAAASUVORK5CYII=";

    private static Paragraph P(string text) => TestHelpers.Para(new Run { Text = text });

    private static void SetField(RichEditor ed, string name, TextPointer tp)
        => typeof(RichEditor).GetField(name, NP)!.SetValue(ed, tp);

    private static void PlaceCaret(RichEditor ed, Paragraph p, int off)
    {
        foreach (var n in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            SetField(ed, n, new TextPointer(p, off));
    }

    private static void Realize(RichEditor ed, double width = 800)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    private static void Press(RichEditor ed, Key key)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    private static void Type(RichEditor ed, string s)
        => ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = s });

    private static (string text, string? uri)[] Links(RichEditor ed)
        => ed.Document!.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>())
             .Where(r => !string.IsNullOrEmpty(r.NavigateUri)).Select(r => (r.Text ?? "", r.NavigateUri)).ToArray();

    private static int Count(IEnumerable<Block> blocks, Func<Block, bool> block, Func<Inline, bool> inline)
    {
        int n = 0;
        foreach (var b in blocks)
        {
            if (block(b)) n++;
            if (b is Paragraph p)
                foreach (var i in p.Inlines)
                {
                    if (inline(i)) n++;
                    if (i is InlineTable it)
                        foreach (var (_, _, cell) in it.Table.LogicalCells()) n += Count(cell.Blocks, block, inline);
                }
            else if (b is TableBlock tb)
                foreach (var (_, _, cell) in tb.LogicalCells()) n += Count(cell.Blocks, block, inline);
        }
        return n;
    }

    private static int Images(RichEditor ed) => Count(ed.Document!.Blocks, b => b is ImageBlock, i => i is InlineImage);
    private static int Tables(RichEditor ed) => Count(ed.Document!.Blocks, b => b is TableBlock, i => i is InlineTable);
    private static string Flat(RichEditor ed) => ed.GetPlainText().Replace("\r", "").Replace("\n", "|");

    // ---- the caret format reports the size the text is DRAWN at ----------------------------------

    [AvaloniaFact]
    public void AnUnsetRunSize_IsReportedAtTheDefaultFontSize_AndLargerGrowsIt()
    {
        var run = new Run { Text = "alpha", FontSize = 0 };
        var p = TestHelpers.Para(run);
        var ed = new RichEditor { Document = TestHelpers.Doc(p), DefaultFontSize = 14 };
        PlaceCaret(ed, p, 2);

        Assert.Equal(14, ed.GetCaretFormat().FontSize);

        ed.IncreaseFontSize();
        Assert.True(ed.GetCaretFormat().FontSize > 14, $"'larger' took 14pt text to {ed.GetCaretFormat().FontSize}");
    }

    [AvaloniaFact]
    public void AHeadingsUnstyledRun_IsReportedAtTheHeadingSize_AndLargerGrowsIt()
    {
        var p = P("Title");
        p.HeadingLevel = 1;
        var ed = new RichEditor { Document = TestHelpers.Doc(p) };
        PlaceCaret(ed, p, 2);

        Assert.Equal(20, ed.GetCaretFormat().FontSize); // an H1 is drawn at 20

        ed.IncreaseFontSize();
        Assert.True(ed.GetCaretFormat().FontSize > 20, $"'larger' took a 20pt heading to {ed.GetCaretFormat().FontSize}");
    }

    // ---- a document swap disarms the format painter -----------------------------------------------

    [AvaloniaFact]
    public void LoadingADocument_DisarmsTheFormatPainter()
    {
        var p = P("source");
        var ed = new RichEditor { Document = TestHelpers.Doc(p) };
        PlaceCaret(ed, p, 2);
        ed.StartFormatPainter();
        Assert.True(ed.IsFormatPainterActive);

        ed.LoadJson(DocumentSerializer.Serialize(TestHelpers.Doc(P("NEW"))));

        Assert.False(ed.IsFormatPainterActive);
    }

    // ---- auto-link: the Tab key, and brackets that belong to the URL ------------------------------

    [AvaloniaFact]
    public void TheTabKey_LinksTheUrlBeforeIt()
    {
        var p = P("x ");
        var ed = new RichEditor { Document = TestHelpers.Doc(p) };
        Realize(ed);
        PlaceCaret(ed, p, 2);

        Type(ed, "https://example.com");
        Press(ed, Key.Tab);

        Assert.Equal("https://example.com", Assert.Single(Links(ed)).uri);
    }

    [Theory]
    [InlineData("https://example.com.", "https://example.com")]
    [InlineData("https://example.com)", "https://example.com")] // the token of "(see https://example.com)"
    [InlineData("https://en.wikipedia.org/wiki/Foo_(bar)", "https://en.wikipedia.org/wiki/Foo_(bar)")]
    [InlineData("https://en.wikipedia.org/wiki/Foo_(bar).", "https://en.wikipedia.org/wiki/Foo_(bar)")]
    public void TrimUrlTail_KeepsABalancedClosingBracket(string token, string expected)
        => Assert.Equal(expected, RichEditor.TrimUrlTail(token));

    [AvaloniaFact]
    public void AWikipediaUrl_IsLinkedWithItsClosingBracket()
    {
        var p = P("x ");
        var ed = new RichEditor { Document = TestHelpers.Doc(p) };
        Realize(ed);
        PlaceCaret(ed, p, 2);

        Type(ed, "https://en.wikipedia.org/wiki/Foo_(bar)");
        Type(ed, " ");

        Assert.Equal("https://en.wikipedia.org/wiki/Foo_(bar)", Assert.Single(Links(ed)).uri);
    }

    // ---- input that is not a document leaves the open one alone -----------------------------------

    [AvaloniaTheory]
    [InlineData("{}")]
    [InlineData("{\"name\":\"pkg\",\"version\":\"1.2.3\",\"dependencies\":{}}")]
    public void LoadJson_RejectsJsonThatIsNotADocument_AndKeepsTheOpenOne(string json)
    {
        var ed = new RichEditor { Document = TestHelpers.Doc(P("ORIGINAL")) };
        var before = ed.Document;

        Assert.Throws<JsonException>(() => ed.LoadJson(json));

        Assert.Same(before, ed.Document);
        Assert.Equal("ORIGINAL", ed.GetPlainText().Trim());
    }

    [AvaloniaFact]
    public void LoadJson_OfALiteralNull_IsStillAnEmptyDocument()
    {
        var ed = new RichEditor { Document = TestHelpers.Doc(P("ORIGINAL")) };
        ed.LoadJson("null");
        Assert.Equal("", ed.GetPlainText().Trim());
    }

    [AvaloniaFact]
    public async Task LoadJsonAsync_RejectsJsonThatIsNotADocument_AndKeepsTheOpenOne()
    {
        var ed = new RichEditor { Document = TestHelpers.Doc(P("ORIGINAL")) };
        var before = ed.Document;

        await Assert.ThrowsAsync<JsonException>(() => ed.LoadJsonAsync("{\"name\":\"pkg\"}"));

        Assert.Same(before, ed.Document);
    }

    private static MemoryStream Zip(string entry, string content)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        using (var s = new StreamWriter(z.CreateEntry(entry).Open()))
            s.Write(content);
        ms.Position = 0;
        return ms;
    }

    [AvaloniaFact]
    public async Task LoadPackageAsync_RejectsAZipWithNoDocument_AndKeepsTheOpenOne()
    {
        var ed = new RichEditor { Document = TestHelpers.Doc(P("ORIGINAL")) };
        var before = ed.Document;

        await Assert.ThrowsAsync<InvalidDataException>(() => ed.LoadPackageAsync(Zip("word/document.xml", "<w:document/>")));

        Assert.Same(before, ed.Document);
    }

    [AvaloniaFact]
    public async Task LoadPackageAsync_RejectsAPackageWhoseDocumentIsNotOurs_AndKeepsTheOpenOne()
    {
        var ed = new RichEditor { Document = TestHelpers.Doc(P("ORIGINAL")) };
        var before = ed.Document;

        await Assert.ThrowsAsync<JsonException>(() => ed.LoadPackageAsync(Zip("document.json", "{\"name\":\"pkg\"}")));

        Assert.Same(before, ed.Document);
    }

    // ---- a plain document starts from the HOST's page setup ---------------------------------------

    private static string A5LandscapeWithHeader()
    {
        var d = TestHelpers.Doc(P("A"));
        d.PageSetup = new PageSetup { PageSize = RichEditorPageSize.A5, Orientation = RichEditorPageOrientation.Landscape, Header = "HDR" };
        return DocumentSerializer.Serialize(d);
    }

    private static string Plain() => DocumentSerializer.Serialize(TestHelpers.Doc(P("plain B")));

    private static string Page(RichEditor ed) => $"{ed.PageSize}/{ed.PageOrientation}/'{ed.PageHeader}'";

    [AvaloniaFact]
    public void APlainDocument_StartsFromTheHostsSetup_NotThePreviousDocuments()
    {
        var ed = new RichEditor { Document = new FlowDocument() };
        ed.PageSize = RichEditorPageSize.A4; // the host's choice

        ed.LoadJson(A5LandscapeWithHeader());
        Assert.Equal("A5/Landscape/'HDR'", Page(ed)); // a document's own setup still applies

        ed.LoadJson(Plain());
        Assert.Equal("A4/Portrait/''", Page(ed));
        Assert.DoesNotContain("HDR", ed.ToJson());

        ed.LoadJson(A5LandscapeWithHeader());
        ed.Clear();
        Assert.Equal("A4/Portrait/''", Page(ed));
    }

    // Recorded per property: an unrelated property change must not promote the open document's setup.
    [AvaloniaFact]
    public void AnUnrelatedHostChange_DoesNotPromoteTheOpenDocumentsSetup()
    {
        var ed = new RichEditor { Document = new FlowDocument() };
        ed.LoadJson(A5LandscapeWithHeader());

        ed.DefaultFontSize = 12;
        ed.PageFooter = "HOST FOOTER";

        ed.LoadJson(Plain());
        Assert.Equal("Continuous/Portrait/''", Page(ed));
        Assert.Equal("HOST FOOTER", ed.PageFooter);
    }

    // The toolbar's paper picker edits the OPEN document; it must not become the host's default.
    [AvaloniaFact]
    public void TheToolbarsPaperPicker_EditsTheDocument_NotTheHostDefault()
    {
        var ed = new RichEditor { Document = new FlowDocument() };
        ed.PageSize = RichEditorPageSize.A4; // host
        var tb = new RichEditorToolbar { Target = ed, ToolbarLevel = ToolbarLevel.Maximum }; // page controls live at Maximum
        var combo = (ComboBox?)typeof(RichEditorToolbar).GetField("_paperCombo", NP)!.GetValue(tb);
        Assert.NotNull(combo);

        combo!.SelectedItem = combo.Items.OfType<ComboBoxItem>().First(i => i.Tag is RichEditorPageSize s && s == RichEditorPageSize.Letter);

        Assert.Equal(RichEditorPageSize.Letter, ed.PageSize);
        Assert.Contains("Letter", ed.ToJson());
        ed.Clear();
        Assert.Equal(RichEditorPageSize.A4, ed.PageSize);
    }

    // ---- AllowImages / AllowTables on inserted content --------------------------------------------

    private static RichEditor EditorAtEnd(string text = "start")
    {
        var p = P(text);
        var ed = new RichEditor { Document = TestHelpers.Doc(p) };
        PlaceCaret(ed, p, text.Length);
        return ed;
    }

    // [AvaloniaTheory], not [Theory]: outside the headless dispatcher the parser cannot build the pictures at
    // all, and this passed VACUOUSLY — no picture ever reached the adaptation it claims to test.
    [AvaloniaTheory]
    [InlineData("inline", "<p>AAA<img src=\"{0}\" width=\"16\" height=\"16\">BBB</p>")]
    [InlineData("block", "<p>AAA</p><img src=\"{0}\" width=\"300\" height=\"300\"><p>BBB</p>")]
    [InlineData("in a cell", "<p>AAA</p><table><tr><td>BBB<img src=\"{0}\" width=\"16\" height=\"16\"></td></tr></table>")]
    public void InsertHtml_WithImagesOff_DropsThePictures_AndKeepsTheText(string shape, string html)
    {
        _ = shape;
        var ed = EditorAtEnd();
        ed.AllowImages = false;

        ed.InsertHtml(string.Format(html, PngUri));

        Assert.Equal(0, Images(ed));
        Assert.Contains("AAA", Flat(ed));
        Assert.Contains("BBB", Flat(ed));
    }

    [AvaloniaFact]
    public void InsertHtml_WithTablesOff_UnwrapsTheTable_IntoItsCellsText()
    {
        var ed = EditorAtEnd();
        ed.AllowTables = false;

        ed.InsertHtml("<p>AAA</p><table><tr><td>C1</td><td>C2</td></tr><tr><td>C3</td><td></td></tr></table><p>BBB</p>");

        Assert.Equal(0, Tables(ed));
        string text = Flat(ed);
        int a = text.IndexOf("AAA"), c1 = text.IndexOf("C1"), c2 = text.IndexOf("C2"), c3 = text.IndexOf("C3"), b = text.IndexOf("BBB");
        Assert.True(a >= 0 && a < c1 && c1 < c2 && c2 < c3 && c3 < b, $"reading order lost: '{text}'");
    }

    // Content the flags remove entirely inserts nothing: no empty undo step, no modified flag. The small
    // picture is the case that needs the `emptied` check (it leaves a blank paragraph, not an empty list).
    [AvaloniaTheory]
    [InlineData(300)]
    [InlineData(16)]
    public void InsertHtml_OfOnlyAPicture_WithImagesOff_LeavesNoTrace(int size)
    {
        var ed = EditorAtEnd();
        ed.AllowImages = false;
        ed.MarkSaved();

        ed.InsertHtml($"<p><img src=\"{PngUri}\" width=\"{size}\" height=\"{size}\"></p>");

        Assert.False(ed.CanUndo);
        Assert.False(ed.IsModified);
        Assert.Equal("start", ed.GetPlainText().Trim());
    }

    [AvaloniaFact]
    public void InsertHtml_WithTheFlagsOn_KeepsPicturesAndTables()
    {
        var ed = EditorAtEnd();

        ed.InsertHtml($"<p>AAA</p><img src=\"{PngUri}\" width=\"300\" height=\"300\"><table><tr><td>C1</td></tr></table><p>BBB</p>");

        Assert.Equal(1, Images(ed));
        Assert.Equal(1, Tables(ed));
    }

    // Opening a document is not an insert: the flags restrict what can be added, not what a file may contain.
    [AvaloniaFact]
    public void LoadHtml_WithTheFlagsOff_StillOpensPicturesAndTables()
    {
        var ed = new RichEditor { AllowImages = false, AllowTables = false };

        ed.LoadHtml($"<p>AAA</p><img src=\"{PngUri}\" width=\"300\" height=\"300\"><table><tr><td>C1</td></tr></table>");

        Assert.Equal(1, Images(ed));
        Assert.Equal(1, Tables(ed));
    }
}
