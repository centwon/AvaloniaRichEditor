using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Feature flags (roadmap N3.5). Capability is expressed directly through IsReadOnly + the Allow* flags
// (the EditorMode preset bundle was removed). Verifies the flag defaults, the guard behaviour (block-insert
// / find / paste path entry points), and the ReadOnly optimization (undo history cleared).
public class FeatureFlagTests
{
    // Turns off the rich-content flags while staying editable (the former "Basic" preset, set directly).
    private static void MakeBasic(RichEditor ed)
    {
        ed.AllowImages = false;
        ed.AllowTables = false;
        ed.AllowRichPaste = false;
        ed.AllowFindReplace = false;
    }

    [AvaloniaFact]
    public void Default_HasAllFlagsEnabled_AndIsEditable()
    {
        var ed = new RichEditor();
        Assert.True(ed.AllowImages);
        Assert.True(ed.AllowTables);
        Assert.True(ed.AllowRichPaste);
        Assert.True(ed.AllowFindReplace);
        Assert.False(ed.IsReadOnly);
    }

    [AvaloniaFact]
    public void ClearingRichFlags_KeepsEditable()
    {
        var ed = new RichEditor();
        MakeBasic(ed);
        Assert.False(ed.AllowImages);
        Assert.False(ed.AllowTables);
        Assert.False(ed.AllowRichPaste);
        Assert.False(ed.AllowFindReplace);
        Assert.False(ed.IsReadOnly);
    }

    [AvaloniaFact]
    public void AllowTablesFalse_BlocksTableInsert()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        MakeBasic(ed);

        ed.InsertTable(2, 2);
        Assert.DoesNotContain(ed.Document!.Blocks, b => b is TableBlock);
    }

    [AvaloniaFact]
    public void ClearedFlags_StillAllowTextInput()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        MakeBasic(ed);

        ed.InsertText("XY");
        var p = (Paragraph)ed.Document!.Blocks.First(b => b is Paragraph);
        Assert.Equal("abcXY", p.Text());
    }

    [AvaloniaFact]
    public void AllowFindReplaceFalse_DisablesFindReplace()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>hello hello</p>");
        MakeBasic(ed);
        Assert.False(ed.FindNext("hello", matchCase: false));
        Assert.Equal(0, ed.ReplaceAll("hello", "x", matchCase: false));
    }

    [AvaloniaFact]
    public void IndividualFlag_TakesEffect()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        MakeBasic(ed);        // tables off
        ed.AllowTables = true; // ...then re-enable just tables

        ed.InsertTable(2, 2);
        Assert.Contains(ed.Document!.Blocks, b => b is TableBlock);
    }

    [AvaloniaFact]
    public void ReadOnly_ClearsUndoHistory()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        ed.InsertTable(2, 2); // pushes an undo checkpoint
        Assert.True(ed.CanUndo);

        ed.IsReadOnly = true;
        Assert.False(ed.CanUndo);
    }

    [AvaloniaFact]
    public void ReadOnly_BlocksInsertHtml()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.IsReadOnly = true;
        int before = ed.Document!.Blocks.Count;

        ed.InsertHtml("<p>injected</p>"); // a programmatic mutation must be refused while read-only

        Assert.Equal(before, ed.Document.Blocks.Count);
        Assert.DoesNotContain("injected", ed.GetPlainText());
    }
}
