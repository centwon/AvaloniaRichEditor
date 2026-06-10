using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// EditorMode presets + feature flags (roadmap N3.5). Verifies the preset->flag bundle, the guard
// behaviour (block-insert / find / paste path entry points), flag-override precedence, and the
// ReadOnly optimization (undo history cleared).
public class EditorModeTests
{
    [AvaloniaFact]
    public void Default_IsFull_WithAllFlagsEnabled()
    {
        var ed = new RichEditor();
        Assert.Equal(EditorMode.Full, ed.EditorMode);
        Assert.True(ed.AllowImages);
        Assert.True(ed.AllowTables);
        Assert.True(ed.AllowRichPaste);
        Assert.True(ed.AllowFindReplace);
        Assert.False(ed.IsReadOnly);
    }

    [AvaloniaFact]
    public void BasicPreset_DisablesRichFlags_ButStaysEditable()
    {
        var ed = new RichEditor();
        ed.EditorMode = EditorMode.Basic;
        Assert.False(ed.AllowImages);
        Assert.False(ed.AllowTables);
        Assert.False(ed.AllowRichPaste);
        Assert.False(ed.AllowFindReplace);
        Assert.False(ed.IsReadOnly);
    }

    [AvaloniaFact]
    public void ReadOnlyPreset_SetsIsReadOnly()
    {
        var ed = new RichEditor();
        ed.EditorMode = EditorMode.ReadOnly;
        Assert.True(ed.IsReadOnly);
    }

    [AvaloniaFact]
    public void Basic_BlocksTableInsert()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        ed.EditorMode = EditorMode.Basic;

        ed.InsertTable(2, 2);
        Assert.DoesNotContain(ed.Document!.Blocks, b => b is TableBlock);
    }

    [AvaloniaFact]
    public void Basic_StillAllowsTextInput()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        ed.EditorMode = EditorMode.Basic;

        ed.InsertText("XY");
        var p = (Paragraph)ed.Document!.Blocks.First(b => b is Paragraph);
        Assert.Equal("abcXY", p.Text());
    }

    [AvaloniaFact]
    public void Basic_DisablesFindReplace()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>hello hello</p>");
        ed.EditorMode = EditorMode.Basic;
        Assert.False(ed.FindNext("hello", matchCase: false));
        Assert.Equal(0, ed.ReplaceAll("hello", "x", matchCase: false));
    }

    [AvaloniaFact]
    public void IndividualFlag_OverridesPreset()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        ed.EditorMode = EditorMode.Basic; // turns tables off
        ed.AllowTables = true;            // ...then re-enables just tables

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
}
