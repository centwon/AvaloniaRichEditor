using System.Collections.Generic;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The events a host builds its chrome on — SelectionChanged (Copy and Delete enabled, a status bar's selection)
// and IsModifiedChanged (the "*" in a title, "save changes?") — fire where the state they announce changes.
// Measured in the WinUI port 2026-09-14, whose code here is the same as this one's: the selection snapshot
// compared only the endpoints, so selecting an object (which leaves the caret where it was) and F5 in an empty
// cell or over an already-selected range raised no SelectionChanged; IsModifiedChanged was raised inside the edit,
// before it mutated — a handler saw the old text — and every open raised it twice ("modified", then not).
public class HostEventTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Set(RichEditor ed, string field, object? value)
        => typeof(RichEditor).GetField(field, NP)!.SetValue(ed, value);

    private static void Caret(RichEditor ed, Paragraph p, int offset)
    {
        foreach (var f in new[] { "_selectionStart", "_selectionEnd", "_caretPosition" }) Set(ed, f, new TextPointer(p, offset));
    }

    private sealed class Count
    {
        public int Selection, Modified;
        public Count(RichEditor ed)
        {
            ed.SelectionChanged += (_, _) => Selection++;
            ed.IsModifiedChanged += (_, _) => Modified++;
        }
    }

    private static Paragraph Para(string text) => new Paragraph { Inlines = { new Run { Text = text } } };

    private static InteractionHost Host(params Block[] blocks)
    {
        var doc = new FlowDocument();
        foreach (var b in blocks) doc.Blocks.Add(b);
        return InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
    }

    // Paints — Render is where the pending events flush, and the flush POSTS them — then runs the dispatcher, so
    // they land now. InteractionHost.Render pumps before painting, which left each step's events to be counted by
    // the next step: the first draft of these tests passed on the setup's own caret event.
    private static void Settle(InteractionHost host) { host.Render(); host.Pump(); }

    // ---- SelectionChanged (each test settles before counting and after acting) -------------------------------

    [AvaloniaFact]
    public void SelectingAnImage_RaisesSelectionChanged_ThoughTheCaretStays()
    {
        var p = Para("ab");
        var img = new ImageBlock { Width = 40, Height = 40 };
        using var host = Host(p, img, Para(""));
        Caret(host.Editor, p, 1);
        Settle(host);
        var n = new Count(host.Editor);

        host.Select(img); // what a click on it sets
        Settle(host);

        Assert.Equal(1, n.Selection);
    }

    // The control for the snapshot: painting again with nothing changed raises nothing — with an object selected,
    // so a snapshot whose object slot compared unequal to itself would be caught.
    [AvaloniaFact]
    public void SelectionChanged_IsNotRaised_WhenNothingChanged()
    {
        var p = Para("ab");
        var img = new ImageBlock { Width = 40, Height = 40 };
        using var host = Host(p, img, Para(""));
        Caret(host.Editor, p, 1);
        host.Select(img);
        Settle(host);
        var n = new Count(host.Editor);

        Settle(host);
        Settle(host);

        Assert.Equal(0, n.Selection);
    }

    // A click on a table's border holds the table with the block caret — the caret's text position stays.
    [AvaloniaFact]
    public void HoldingATableByItsBorder_RaisesSelectionChanged()
    {
        var p = Para("ab");
        var tb = new TableBlock(1, 2);
        using var host = Host(p, tb, Para(""));
        Caret(host.Editor, p, 1);
        Settle(host);
        var n = new Count(host.Editor);

        Set(host.Editor, "_caretBlock", tb);
        Settle(host);

        Assert.Equal(1, n.Selection);
    }

    // An empty cell's one-cell block is the caret itself: (p, 0) to (p, 0).
    [AvaloniaFact]
    public void F5_InAnEmptyCell_RaisesSelectionChanged()
    {
        var tb = new TableBlock(2, 2);
        ((Run)tb.Cells[0][0].Para.Inlines[0]).Text = "";
        using var host = Host(Para(""), tb, Para(""));
        Caret(host.Editor, tb.Cells[0][0].Para, 0);
        Settle(host);
        var n = new Count(host.Editor);

        host.Key(Key.F5);
        Settle(host);

        Assert.Equal(1, n.Selection);
    }

    // Ctrl+A's first stage in a cell selects exactly its text; F5 marks the same range as a cell block — Delete now
    // empties the cell, Copy takes a 1×1 table. Same endpoints, different selection.
    [AvaloniaFact]
    public void F5_OverTheSameTextRange_RaisesSelectionChanged()
    {
        var tb = new TableBlock(2, 2);
        ((Run)tb.Cells[0][0].Para.Inlines[0]).Text = "xyz";
        using var host = Host(Para(""), tb, Para(""));
        var cp = tb.Cells[0][0].Para;
        Set(host.Editor, "_selectionStart", new TextPointer(cp, 0));
        Set(host.Editor, "_selectionEnd", new TextPointer(cp, 3));
        Set(host.Editor, "_caretPosition", new TextPointer(cp, 3));
        Settle(host);
        var n = new Count(host.Editor);

        host.Key(Key.F5);
        Settle(host);

        Assert.Equal(1, n.Selection);
    }

    // ---- IsModifiedChanged ------------------------------------------------------------------------------------

    // A host that saves or snapshots on "modified" has to see the edit it is told about.
    [AvaloniaFact]
    public void IsModifiedChanged_ComesAfterTheEdit_AndSeesIt()
    {
        var p = Para("ab");
        using var host = Host(p);
        host.Editor.MarkSaved();
        Caret(host.Editor, p, 2);
        Settle(host);
        var seen = new List<string>();
        host.Editor.IsModifiedChanged += (_, _) => seen.Add($"{host.Editor.IsModified}:{((Run)p.Inlines[0]).Text}");

        host.Type("X");

        Assert.Equal(new[] { "True:abX" }, seen);
    }

    [AvaloniaFact]
    public void LoadingIntoACleanEditor_RaisesNoIsModifiedChanged()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>first</p>");
        Dispatcher.UIThread.RunJobs();
        var n = new Count(ed);

        ed.LoadHtml("<p>abc</p>");
        Dispatcher.UIThread.RunJobs();

        Assert.False(ed.IsModified);
        Assert.Equal(0, n.Modified);
    }

    // The control: over a MODIFIED document a load does change the flag, and says so once.
    [AvaloniaFact]
    public void LoadingOverAModifiedDocument_RaisesIsModifiedChangedOnce()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>ab</p>");
        ed.FocusDocumentEnd();
        ed.InsertText("X");
        Dispatcher.UIThread.RunJobs();
        Assert.True(ed.IsModified);
        var n = new Count(ed);

        ed.LoadHtml("<p>abc</p>");
        Dispatcher.UIThread.RunJobs();

        Assert.False(ed.IsModified);
        Assert.Equal(1, n.Modified);
    }

    // Pinned as documented: assigning Document directly is an edit; only Load* and Clear start clean.
    [AvaloniaFact]
    public void ARawDocumentAssignment_IsAChange()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>ab</p>");
        Dispatcher.UIThread.RunJobs();
        var n = new Count(ed);

        ed.Document = new FlowDocument();
        Dispatcher.UIThread.RunJobs();

        Assert.True(ed.IsModified);
        Assert.Equal(1, n.Modified);
    }
}
