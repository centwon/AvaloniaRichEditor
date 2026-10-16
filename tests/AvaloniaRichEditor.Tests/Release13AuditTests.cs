using System;
using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The last audit before 1.3.0 (2026-10-17): the whole of src read again after rounds 34 and 35.
public class Release13AuditTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void SetCaret(RichEditor ed, TextPointer caret, TextPointer? anchor = null)
    {
        typeof(RichEditor).GetField("_caretPosition", NP)!.SetValue(ed, caret);
        typeof(RichEditor).GetField("_selectionStart", NP)!.SetValue(ed, anchor ?? new TextPointer(caret.Paragraph, caret.Offset));
        typeof(RichEditor).GetField("_selectionEnd", NP)!.SetValue(ed, new TextPointer(caret.Paragraph, caret.Offset));
    }

    // ---- a selection from a line's own text into its inline table -------------------------------------------

    // Round 35 fixed a selection reaching an inline table from the line ABOVE. One that starts in the table's own
    // line, before the table, still deleted from its start to the END of the line: the table and the text after it.
    [AvaloniaFact]
    public void DeletingFromALinesTextIntoItsOwnInlineTable_KeepsTheTableAndTheTextAfterIt()
    {
        var t = new TableBlock(1, 1);
        var cell = t.Cells[0][0].Para;
        cell.Inlines.Add(new Run { Text = "cellText" });
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "abc" }, new InlineTable { Table = t }, new Run { Text = "def" } } });
        var host = InteractionHost.Create(new RichEditor { Document = doc });
        var ed = host.Editor;
        var line = (Paragraph)ed.Document!.Blocks[0];
        SetCaret(ed, new TextPointer(line, 1));
        for (int i = 0; i < 2 + 1 + 4; i++) host.Key(Key.Right, RawInputModifiers.Shift); // "bc", into the cell, "cell"
        Assert.Same(cell, host.Caret.Paragraph); // precondition: the selection really ends inside the table
        Assert.Equal(4, host.Caret.Offset);

        host.Key(Key.Delete);

        Assert.Single(line.Inlines.OfType<InlineTable>());
        string text = ed.GetPlainText();
        Assert.StartsWith("a", text);
        Assert.Contains("Text", text);  // the rest of the cell
        Assert.Contains("def", text);   // the line's text after the table
        Assert.DoesNotContain("bc", text);
    }

    // ---- sizes a file states, summed past the range of a double ---------------------------------------------

    // Each value is finite — the reader refuses 1e400 since 2026-10-15 — but two of them add up to infinity, and an
    // infinite desired size is an exception out of Avalonia's layout pass: the window's next measure throws.
    [AvaloniaTheory]
    [InlineData("{\"Blocks\":[{\"Type\":\"Image\",\"Height\":1e308},{\"Type\":\"Image\",\"Height\":1e308}]}")]
    [InlineData("{\"Blocks\":[{\"MarginTop\":1e308,\"Inlines\":[{\"Text\":\"a\"}]},{\"MarginTop\":1e308,\"Inlines\":[{\"Text\":\"b\"}]}]}")]
    [InlineData("{\"Blocks\":[{\"Inlines\":[{\"Text\":\"a\",\"FontSize\":1e308}]}]}")]
    public void HugeButFiniteSizesInAFile_DoNotBreakTheLayoutPass(string json)
    {
        var host = InteractionHost.CreateWithView(new RichEditorView()); // in a ScrollViewer, as hosts have it
        host.Editor.LoadJson(json);
        host.Pump();
        host.Render();
        Assert.True(double.IsFinite(host.Editor.DesiredSize.Height));
        host.Dispose();
    }

    // Pasted HTML reaches the same sizes without any file: double.TryParse reads "Infinity" and "1e308", and every
    // number an <img>, a line-height or our own spacing marker states was taken as it came.
    private const string Png1x1 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    // Pictures arrive by paste; paragraph spacing only by opening a page — a paste merges its first and last
    // paragraphs into the caret's line and their spacing does not come along.
    [AvaloniaTheory]
    [InlineData("<p>a<img src=\"data:image/png;base64," + Png1x1 + "\" width=\"Infinity\" height=\"Infinity\">b</p>", true)]
    [InlineData("<img src=\"data:image/png;base64," + Png1x1 + "\" width=\"1e308\" height=\"1e308\"><img src=\"data:image/png;base64," + Png1x1 + "\" width=\"1e308\" height=\"1e308\">", true)]
    [InlineData("<p style=\"line-height:Infinity\">a</p>", false)]
    [InlineData("<p data-are-m=\"1e308,1e308,0\">a</p><p data-are-m=\"1e308,1e308,0\">b</p>", false)]
    public void HugeOrInfiniteSizesInHtml_DoNotBreakLayoutOrRendering(string html, bool paste)
    {
        var host = InteractionHost.CreateWithView(new RichEditorView());
        if (paste)
        {
            host.Editor.Clear(); // a document to insert into: a new editor has none, and InsertHtml then does nothing
            host.Editor.InsertHtml(html);
            Assert.True(host.Editor.CanUndo); // precondition: the HTML really went in
        }
        else host.Editor.LoadHtml(html);
        host.Pump();
        host.Render();
        Assert.True(double.IsFinite(host.Editor.DesiredSize.Height));
        host.Dispose();
    }

    // ---- ←/→ inside a table nested in an inline table's cell -------------------------------------------------

    // ←/→ find the next paragraph in one of two orders: the inline table's own (for a paragraph DIRECTLY in its cell)
    // or the document's navigation order, which leaves inline tables out. A paragraph in a block table nested in an
    // inline table's cell is in neither, so the caret stopped at its edge and the arrow did nothing.
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArrowKeys_LeaveATableNestedInAnInlineTablesCell(bool right)
    {
        var nested = new TableBlock(1, 1);
        var inner = nested.Cells[0][0].Para;
        inner.Inlines.Add(new Run { Text = "x" });
        var outer = new TableBlock(1, 1);
        var cell = outer.Cells[0][0];
        cell.Blocks.Clear();
        cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "before" } } });
        cell.Blocks.Add(nested);
        cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "after" } } });
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "ab" }, new InlineTable { Table = outer }, new Run { Text = "cd" } } });
        var host = InteractionHost.Create(new RichEditor { Document = doc });
        SetCaret(host.Editor, new TextPointer(inner, right ? 1 : 0));

        host.Key(right ? Key.Right : Key.Left);

        Assert.NotSame(inner, host.Caret.Paragraph);
        Assert.Equal(right ? "after" : "before", host.Caret.Paragraph?.Inlines.OfType<Run>().FirstOrDefault()?.Text);
    }

    // ---- tab-separated text pasted as a table ---------------------------------------------------------------

    // Every other importer bounds the table it builds (TableBlock.ImportColumns, round 35); the clipboard's
    // tab-separated text made rows x widest-row cells with no bound, so one line of tabs over many short lines
    // multiplied into millions of cells from a few hundred KB of plain text.
    [AvaloniaFact]
    public async System.Threading.Tasks.Task TabSeparatedPaste_IsBoundLikeEveryOtherImportedTable()
    {
        var host = InteractionHost.Create(new RichEditor { Document = new FlowDocument() });
        var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(host.Editor)!.Clipboard!;
        var text = new System.Text.StringBuilder();
        text.Append(string.Join('\t', Enumerable.Range(0, TableBlock.MaxImportColumns + 500).Select(i => "c")));
        text.Append("\nx\ty");
        await clipboard.SetTextAsync(text.ToString());

        await host.Editor.PasteFromClipboardAsync();
        host.Pump();

        var tb = host.Editor.Document!.Blocks.OfType<TableBlock>().Single();
        Assert.True(tb.Columns <= TableBlock.MaxImportColumns, $"{tb.Columns} columns");
        Assert.True((long)tb.Rows * tb.Columns <= TableBlock.MaxImportCells, $"{tb.Rows} x {tb.Columns} cells");
        Assert.Equal("c", tb.Cells[0][0].Para.Inlines.OfType<Run>().First().Text); // still the pasted table
    }

    // ---- a table of more than 10,000 cells ------------------------------------------------------------------

    // The layout cache pruned itself whenever it held more than 10,000 layouts — on EVERY build past that, walking
    // the whole document to find nothing to drop, because a table draws all its cells in one render and every one
    // is live. 20,000 cells took 77 s to draw; tables up to 250,000 cells are what the importers accept.
    [AvaloniaFact]
    public void ATableOfTwentyThousandCells_DrawsInSeconds()
    {
        var tb = new TableBlock(100, 200);
        foreach (var row in tb.Cells) foreach (var cell in row) cell.Para.Inlines.Add(new Run { Text = "c" });
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var sw = System.Diagnostics.Stopwatch.StartNew(); // before Create: showing the window is the first render
        var host = InteractionHost.Create(new RichEditor { Document = doc });
        host.Render();
        Assert.True(host.Editor.LayoutCacheCount > 10_000); // precondition: the render really built a layout per cell
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"{sw.Elapsed.TotalSeconds:0.0} s");
        host.Dispose();
    }

    // ---- a .flow whose document.json inflates without bound -----------------------------------------------------

    // Round 35 bounded the picture entries a package inflates (256 MB); the document itself was read whole, so a 379 KB
    // .flow inflated to 400 million characters and 1.9 GB (measured). Bounded the same (user decision, 2026-10-17):
    // past it the package is unreadable, as a damaged one is.
    [Fact]
    public void AFlowWhoseDocumentInflatesPastTheBound_IsRefusedLikeADamagedOne()
    {
        var ms = new System.IO.MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        using (var s = zip.CreateEntry("document.json", System.IO.Compression.CompressionLevel.Optimal).Open())
        {
            s.Write("{\"Blocks\":[]}"u8);
            var spaces = new byte[1 << 20];
            Array.Fill(spaces, (byte)' ');
            for (long w = 0; w <= AvaloniaRichEditor.Formatters.DocumentPackage.MaxDocumentBytes; w += spaces.Length) s.Write(spaces);
        }
        Assert.True(ms.Length < 2 * 1024 * 1024, $"{ms.Length} bytes"); // precondition: a small file
        ms.Position = 0;

        Assert.Throws<System.IO.InvalidDataException>(() => AvaloniaRichEditor.Formatters.DocumentPackage.Load(ms));
    }

    // ---- a BMP header claiming int.MinValue pixels -----------------------------------------------------------

    // Width and height are signed in a BMP header (negative height = top-down), so the reader takes the absolute
    // value — and Math.Abs(int.MinValue) throws. The image menus ask for the natural size outside any catch.
    [Fact]
    public void ABmpHeaderOfIntMinValue_IsReadAsUnknownSize_NotAnException()
    {
        var b = new byte[64];
        b[0] = (byte)'B'; b[1] = (byte)'M';
        BitConverter.GetBytes(int.MinValue).CopyTo(b, 18);
        BitConverter.GetBytes(int.MinValue).CopyTo(b, 22);
        var (w, h) = ImageInfo.GetPixelSize(b);
        Assert.True(w >= 0 && h >= 0);
        Assert.True(ImageInfo.TooLargeToDecode(b)); // 2^31 squared is no picture to decode
    }
}
