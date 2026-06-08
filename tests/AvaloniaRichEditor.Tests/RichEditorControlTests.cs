using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Control-level tests on a headless Avalonia app: exercise the real RichEditor code paths
// (caret, editing, undo coalescing, HTML/JSON, accessibility) — not just the document model.
public class RichEditorControlTests
{
    private static string FirstParagraphText(RichEditor ed)
        => ((Paragraph)ed.Document!.Blocks.First(b => b is Paragraph)).Text();

    [AvaloniaFact]
    public void InsertText_AppendsAtCaret()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        ed.InsertText("XY");
        Assert.Equal("abcXY", FirstParagraphText(ed));
    }

    [AvaloniaFact]
    public void ToggleBold_ThenUndo_RevertsFormatting()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();

        ed.ToggleBold(); // applies to the caret paragraph's runs and pushes an undo checkpoint
        var p = (Paragraph)ed.Document!.Blocks.First(b => b is Paragraph);
        Assert.All(p.Inlines.OfType<Run>(), r => Assert.Equal(FontWeight.Bold, r.FontWeight));

        ed.Undo();
        var p2 = (Paragraph)ed.Document!.Blocks.First(b => b is Paragraph);
        Assert.All(p2.Inlines.OfType<Run>(), r => Assert.Equal(FontWeight.Normal, r.FontWeight));
    }

    [AvaloniaFact]
    public void InsertTable_ThenUndo_RestoresBlockCount()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        int before = ed.Document!.Blocks.Count;

        ed.InsertTable(2, 2);
        Assert.Contains(ed.Document!.Blocks, b => b is TableBlock);

        ed.Undo();
        Assert.Equal(before, ed.Document!.Blocks.Count);
        Assert.DoesNotContain(ed.Document!.Blocks, b => b is TableBlock);
    }

    [AvaloniaFact]
    public void Redo_ReappliesUndoneEdit()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        ed.InsertTable(2, 2);
        ed.Undo();
        Assert.DoesNotContain(ed.Document!.Blocks, b => b is TableBlock);

        ed.Redo();
        Assert.Contains(ed.Document!.Blocks, b => b is TableBlock);
    }

    [AvaloniaFact]
    public void LoadHtml_ThenToHtml_PreservesContent()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>hello <b>world</b></p>");
        var html = ed.ToHtml();
        Assert.Contains("hello", html);
        Assert.Contains("<b>", html);
    }

    [AvaloniaFact]
    public void ToJson_LoadJson_RoundTrips()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>data</p>");
        var json = ed.ToJson();

        var ed2 = new RichEditor();
        ed2.LoadJson(json);
        Assert.Equal("data", FirstParagraphText(ed2));
    }

    [AvaloniaFact]
    public void GetPlainText_ConcatenatesParagraphs()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>one</p><p>two</p>");
        var text = ed.GetPlainText();
        Assert.Contains("one", text);
        Assert.Contains("two", text);
    }

    [AvaloniaFact]
    public void Clear_LeavesEmptyDocument()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>stuff</p>");
        ed.Clear();
        Assert.Equal("", ed.GetPlainText().Trim());
    }
}
