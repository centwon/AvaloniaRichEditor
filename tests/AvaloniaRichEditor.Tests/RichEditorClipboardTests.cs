using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Paste-pipeline components that don't need a real clipboard: the CF_HTML header stripping
// (Windows "HTML Format" — a documented pitfall) and the HTML-insertion path that clipboard
// paste funnels into (ParseHtml + InsertParsedDocument via the public InsertHtml).
// The async clipboard acquisition itself (TryGetDataAsync ordering) remains uncovered.
public class RichEditorClipboardTests
{
    // ---- CF_HTML fragment extraction ----

    [Fact]
    public void ExtractHtmlFragment_StripsCfHtmlMarkers()
    {
        const string raw =
            "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000200\r\n" +
            "<html><body><!--StartFragment--><p>real <b>content</b></p><!--EndFragment--></body></html>";
        Assert.Equal("<p>real <b>content</b></p>", RichEditor.ExtractHtmlFragment(raw));
    }

    [Fact]
    public void ExtractHtmlFragment_CfHtmlHeaderWithoutMarkers_CutsToFirstTag()
    {
        const string raw = "Version:1.0\r\nStartHTML:64\r\n<html><p>x</p></html>";
        Assert.Equal("<html><p>x</p></html>", RichEditor.ExtractHtmlFragment(raw));
    }

    [Fact]
    public void ExtractHtmlFragment_PlainHtml_PassesThrough()
    {
        const string raw = "<p>mac/linux clipboard html has no CF_HTML header</p>";
        Assert.Equal(raw, RichEditor.ExtractHtmlFragment(raw));
    }

    // ---- TSV table-paste heuristic ----

    [Fact]
    public void LooksTabular_SpreadsheetGrid_True()
    {
        Assert.True(RichEditor.LooksTabular("a\tb\r\nc\td\r\n"));
        Assert.True(RichEditor.LooksTabular("a\t\tb")); // empty middle cell is still a grid
    }

    [Fact]
    public void LooksTabular_TabIndentedCode_False()
    {
        // Regression: "\tfoo" splits into ["", "foo"] and used to count as a 2-column row,
        // so pasting tab-indented code produced a bogus table.
        Assert.False(RichEditor.LooksTabular("\tvar x = 1;\n\treturn x;"));
        // A grid line plus a prose line (no tab) is not a grid either.
        Assert.False(RichEditor.LooksTabular("a\tb\nplain text line"));
    }

    // ---- HTML insertion at caret (the core of the external-HTML paste branch) ----

    [AvaloniaFact]
    public void InsertHtml_AppendsParsedContentWithFormatting()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>start</p>");
        ed.FocusDocumentEnd();

        ed.InsertHtml("<p>pasted <b>bold</b></p>");

        var text = ed.GetPlainText();
        Assert.Contains("start", text);
        Assert.Contains("pasted", text);
        var runs = ed.Document!.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Run>();
        Assert.Contains(runs, r => r.Text == "bold" && r.FontWeight == FontWeight.Bold);
    }

    [AvaloniaFact]
    public void InsertHtml_WithTable_InsertsTableBlock()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>before</p>");
        ed.FocusDocumentEnd();

        ed.InsertHtml("<table><tr><td>a</td><td>b</td></tr></table>");

        var tb = Assert.Single(ed.Document!.Blocks.OfType<TableBlock>());
        Assert.Equal(2, tb.Columns);
        Assert.Equal("a", tb.Cells[0][0].Text());
    }

    [AvaloniaFact]
    public void InsertHtml_SingleParagraph_MergesInlineAtCaret()
    {
        // Documented behavior: a single inline-only paragraph keeps the current line's flow
        // instead of starting a new block.
        var ed = new RichEditor();
        ed.LoadHtml("<p>start</p>");
        ed.FocusDocumentEnd();
        int before = ed.Document!.Blocks.Count;

        ed.InsertHtml("<p>extra</p>");

        Assert.Equal(before, ed.Document!.Blocks.Count);
        Assert.Contains("startextra", ed.GetPlainText());
    }

    [AvaloniaFact]
    public void InsertHtml_MultipleParagraphs_SplicesBlocks_AndIsUndoable()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>start</p>");
        ed.FocusDocumentEnd();
        int before = ed.Document!.Blocks.Count;

        ed.InsertHtml("<p>one</p><p>two</p>");
        Assert.Equal(before + 2, ed.Document!.Blocks.Count);

        ed.Undo();
        Assert.Equal(before, ed.Document!.Blocks.Count);
    }
}
