using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Dragging a table or a picture to move it, or with Ctrl to copy it (RichEditor.DragBlock.cs, from the WinUI
// port 2026-09-15). The table's left/top border already showed the move cursor and a press there only placed
// the block caret.
//
// The gesture is driven through the window (InteractionHost): press where the renderer put the border or the
// picture, move, release — with and without Ctrl — so the wiring the port can only check by eye is covered
// here. The drop RULE is pinned by shape through DropObject directly: a block dropped at a paragraph's start
// goes BEFORE it, at its end AFTER it, and splits it only in between — the rule that keeps a table dragged down
// and back from growing the document by a blank line per trip.
public class ObjectDragInteractionTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static InteractionHost Host(params Block[] blocks)
    {
        var doc = new FlowDocument();
        foreach (var b in blocks) doc.Blocks.Add(b);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        ed.MarkSaved(); // a raw Document assignment counts as an edit; start clean, as a Load* would
        var host = InteractionHost.Create(ed);
        host.Render(); // the handles the border is found by are recorded while painting
        return host;
    }

    private static Paragraph P(string text, params Inline[] more)
    {
        var p = new Paragraph { Inlines = { new Run { Text = text } } };
        foreach (var i in more) p.Inlines.Add(i);
        return p;
    }

    private static TableBlock Table()
    {
        var tb = new TableBlock(1, 2);
        for (int c = 0; c < 2; c++) ((Run)tb.Cells[0][c].Para.Inlines[0]).Text = $"c0{c}";
        return tb;
    }

    // A paragraph's text with its objects as [i] / [t], so a shape reads as the document does.
    private static string Text(Paragraph p) => string.Concat(p.Inlines.Select(i => i switch
    {
        Run r => r.Text ?? "",
        InlineImage => "[i]",
        InlineTable => "[t]",
        _ => "?",
    }));

    // A block list's shape: T = table, I = image, a paragraph by its text (∅ when empty).
    private static string Shape(IEnumerable<Block> blocks) => string.Join(",", blocks.Select(b => b switch
    {
        TableBlock => "T",
        ImageBlock => "I",
        Paragraph p => Text(p) is { Length: > 0 } s ? s : "∅",
        _ => "?",
    }));

    private static string Shape(RichEditor ed) => Shape(ed.Document!.Blocks);
    private static Paragraph Para(RichEditor ed, string text) => ed.Document!.Blocks.OfType<Paragraph>().First(p => Text(p) == text);
    private static TableBlock FirstTable(RichEditor ed) => ed.Document!.Blocks.OfType<TableBlock>().First();

    private static bool Drop(RichEditor ed, object obj, Paragraph p, int off, bool copy = false)
        => (bool)typeof(RichEditor).GetMethod("DropObject", NP)!.Invoke(ed, new object[] { obj, new TextPointer(p, off), copy })!;

    // A point that hit-tests to exactly (p, offset). Swept rather than computed, so it cannot drift from the
    // layout the renderer produced.
    private static Point PointAt(InteractionHost host, Paragraph p, int offset)
    {
        var hit = typeof(RichEditor).GetMethod("GetPositionFromPoint", NP)!;
        for (double y = 2; y < host.Editor.DesiredSize.Height; y += 3)
            for (double x = 2; x < host.Editor.Bounds.Width; x += 3)
            {
                var tp = (TextPointer)hit.Invoke(host.Editor, new object[] { new Point(x, y) })!;
                if (ReferenceEquals(tp.Paragraph, p) && tp.Offset == offset) return new Point(x, y);
            }
        throw new Xunit.Sdk.XunitException($"no point hit-tests to offset {offset} of \"{Text(p)}\"");
    }

    // The table's left border, just below its top edge — where the move cursor shows. Its x is the start of
    // the row handles (they span the table's width), its top the column handles' (they span its height).
    private static Point TableBorder(InteractionHost host, TableBlock tb)
    {
        var row = host.RowHandles.First(h => ReferenceEquals(h.tb, tb));
        var col = host.ColumnHandles.First(h => ReferenceEquals(h.tb, tb));
        return new Point(row.rect.Left, col.rect.Top + 4);
    }

    // A point on the picture itself.
    private static Point OnBlock(InteractionHost host, Block target)
    {
        var at = typeof(RichEditor).GetMethod("GetBlockAtPoint", NP)!;
        for (double y = 2; y < host.Editor.DesiredSize.Height; y += 3)
            for (double x = 12; x < host.Editor.Bounds.Width; x += 3)
                if (ReferenceEquals(at.Invoke(host.Editor, new object[] { new Point(x, y) }), target))
                    return new Point(x, y);
        throw new Xunit.Sdk.XunitException("no point lands on the block");
    }

    private static void DragTo(InteractionHost host, Point from, Point to, bool ctrl = false)
    {
        var mods = ctrl ? RawInputModifiers.Control : RawInputModifiers.None;
        host.Press(from, MouseButton.Left, mods);
        host.Move(to, RawInputModifiers.LeftMouseButton | mods);
        host.Release(to, MouseButton.Left, mods);
    }

    // ---- the gesture, through the window --------------------------------------------------------------

    [AvaloniaFact]
    public void DraggingATablesBorder_MovesTheTable_InOneUndoStep()
    {
        var host = Host(P("top"), Table(), P("mid"), P("end"));
        var ed = host.Editor;
        var tb = FirstTable(ed);

        DragTo(host, TableBorder(host, tb), PointAt(host, Para(ed, "end"), 0));

        Assert.Equal("top,mid,T,end", Shape(ed));
        Assert.Same(tb, ed.Document!.Blocks[2]);   // moved, not re-created
        Assert.Same(tb, host.CaretBlock);          // held as a border click holds it
        Assert.True(ed.IsModified);
        ed.Undo();
        Assert.Equal("top,T,mid,end", Shape(ed));
    }

    [AvaloniaFact]
    public void WithCtrlDownAtTheRelease_ItCopies()
    {
        var host = Host(P("top"), Table(), P("mid"), P("end"));
        var ed = host.Editor;
        var tb = FirstTable(ed);

        DragTo(host, TableBorder(host, tb), PointAt(host, Para(ed, "end"), 0), ctrl: true);

        Assert.Equal("top,T,mid,T,end", Shape(ed));
        Assert.Same(tb, ed.Document!.Blocks[1]);
        Assert.NotSame(tb, ed.Document!.Blocks[3]);
    }

    // What the border did before the feature must still be all a click does.
    [AvaloniaFact]
    public void AClickOnTheBorder_StillOnlyPlacesTheBlockCaret()
    {
        var host = Host(P("top"), Table(), P("mid"), P("end"));
        var ed = host.Editor;
        var tb = FirstTable(ed);

        host.Click(TableBorder(host, tb));

        Assert.Same(tb, host.CaretBlock);
        Assert.Equal("top,T,mid,end", Shape(ed));
        Assert.False(ed.CanUndo);
        Assert.False(ed.IsModified);
    }

    [AvaloniaFact]
    public void DraggingAPicture_MovesIt()
    {
        var host = Host(P("top"), new ImageBlock { Width = 120, Height = 60 }, P("mid"), P("end"));
        var ed = host.Editor;
        var img = ed.Document!.Blocks.OfType<ImageBlock>().Single();

        DragTo(host, OnBlock(host, img), PointAt(host, Para(ed, "end"), 0));

        Assert.Equal("top,mid,I,end", Shape(ed));
        Assert.Same(img, host.SelectedBlock);
    }

    // A picture too, not only a table: a viewer's border press returns early (it selects the table for Copy)
    // and never reaches the drag, so the table alone left the viewer gate untested — a falsification that
    // removed it stayed green. A picture press arms from the same code in a viewer as in an editor.
    [AvaloniaFact]
    public void AViewer_NeverMovesAnything()
    {
        var host = Host(P("top"), Table(), P("mid"), new ImageBlock { Width = 120, Height = 60 }, P("end"));
        var ed = host.Editor;
        var img = ed.Document!.Blocks.OfType<ImageBlock>().Single();
        ed.IsReadOnly = true;
        host.Render();

        DragTo(host, TableBorder(host, FirstTable(ed)), PointAt(host, Para(ed, "end"), 0));
        DragTo(host, OnBlock(host, img), PointAt(host, Para(ed, "top"), 0));

        Assert.Equal("top,T,mid,I,end", Shape(ed));
        Assert.False(ed.IsModified);
    }

    // A new document under a live drag: the release must not drop the old document's table into it.
    [AvaloniaFact]
    public void ADocumentSwapMidDrag_CancelsTheDrag()
    {
        var host = Host(P("top"), Table(), P("mid"), P("end"));
        var ed = host.Editor;
        var from = TableBorder(host, FirstTable(ed));
        var to = PointAt(host, Para(ed, "end"), 0);
        host.Press(from);
        host.Move(to, RawInputModifiers.LeftMouseButton);

        var other = new FlowDocument();
        other.Blocks.Add(P("other"));
        other.Blocks.Add(Table());
        other.Blocks.Add(P("doc"));
        ed.Document = other;
        host.Render();
        host.Release(to);

        Assert.Equal("other,T,doc", Shape(ed));
        Assert.False(ed.CanUndo);
    }

    // ---- where a block lands ------------------------------------------------------------------------

    [AvaloniaFact]
    public void DroppedAtAParagraphsEnd_TheBlockGoesAfterIt()
    {
        var ed = Host(P("top"), Table(), P("mid"), P("end")).Editor;
        Assert.True(Drop(ed, FirstTable(ed), Para(ed, "end"), 3));
        Assert.Equal("top,mid,end,T,∅", Shape(ed)); // the closing paragraph a block may not end on
    }

    [AvaloniaFact]
    public void DroppedInsideAParagraph_SplitsIt()
    {
        var ed = Host(P("top"), Table(), P("middle"), P("end")).Editor;
        Assert.True(Drop(ed, FirstTable(ed), Para(ed, "middle"), 3));
        Assert.Equal("top,mid,T,dle,end", Shape(ed));
    }

    // Right after the paragraph before it, or right before the paragraph after it: that is where it is.
    [AvaloniaFact]
    public void ADropWhereTheBlockAlreadyIs_IsNoEdit()
    {
        var ed = Host(P("top"), Table(), P("mid"), P("end")).Editor;
        var tb = FirstTable(ed);
        Assert.False(Drop(ed, tb, Para(ed, "mid"), 0));
        Assert.False(Drop(ed, tb, Para(ed, "top"), 3));
        Assert.Equal("top,T,mid,end", Shape(ed));
        Assert.False(ed.CanUndo);
        Assert.False(ed.IsModified);
    }

    // The accumulation class: a rule that leaves a paragraph behind per drop grows the document per trip.
    // The first trip may leave the closing paragraph a block needs; after that the shapes must repeat.
    [AvaloniaFact]
    public void DraggingATableDownAndBack_DoesNotGrowTheDocument()
    {
        var ed = Host(P("top"), Table(), P("mid"), P("end")).Editor;
        var tb = FirstTable(ed);
        var downs = new List<string>();
        var backs = new List<string>();
        for (int trip = 0; trip < 3; trip++)
        {
            Assert.True(Drop(ed, tb, Para(ed, "end"), 3));
            downs.Add(Shape(ed));
            Assert.True(Drop(ed, tb, Para(ed, "mid"), 0));
            backs.Add(Shape(ed));
        }
        Assert.All(downs, s => Assert.Equal(downs[0], s));
        Assert.All(backs, s => Assert.Equal(backs[0], s));
    }

    // A table moved into one of its own cells would have to contain itself. A copy is a clone, so it may.
    [AvaloniaFact]
    public void AMoveIntoItsOwnCell_IsRefused_ButACopyThereIsAllowed()
    {
        var ed = Host(P("top"), Table(), P("end")).Editor;
        var tb = FirstTable(ed);
        var cell = tb.Cells[0][0].Para;

        Assert.False(Drop(ed, tb, cell, 0));
        Assert.Equal("top,T,end", Shape(ed));
        Assert.False(ed.CanUndo);

        Assert.True(Drop(ed, tb, cell, 0, copy: true));
        Assert.Contains(tb.Cells[0][0].Blocks, b => b is TableBlock nested && !ReferenceEquals(nested, tb));
    }

    [AvaloniaFact]
    public void AnImage_MovesIntoACell_AndBackOut()
    {
        var ed = Host(P("top"), new ImageBlock { Width = 40, Height = 30 }, P("mid"), Table(), P("end")).Editor;
        var img = ed.Document!.Blocks.OfType<ImageBlock>().Single();
        var tb = FirstTable(ed);

        Assert.True(Drop(ed, img, tb.Cells[0][1].Para, 3)); // after "c01"
        Assert.Equal("top,mid,T,end", Shape(ed));
        Assert.Equal("c01,I,∅", Shape(tb.Cells[0][1].Blocks));

        // Before "end" is right after the table: two blocks may not touch (rule #5), so a blank line
        // separates them — the model's, not the drop's. It is there once; another trip adds none.
        Assert.True(Drop(ed, img, Para(ed, "end"), 0));
        Assert.Equal("top,mid,T,∅,I,end", Shape(ed));
        Assert.True(Drop(ed, img, tb.Cells[0][1].Para, 3));
        Assert.True(Drop(ed, img, Para(ed, "end"), 0));
        Assert.Equal("top,mid,T,∅,I,end", Shape(ed));
    }

    // ---- inline objects: one character each -------------------------------------------------------

    [AvaloniaFact]
    public void AnInlineImage_MovesLaterInItsOwnParagraph()
    {
        var ed = Host(P("ab", new InlineImage { Width = 16, Height = 16 }, new Run { Text = "cdef" }), P("end")).Editor;
        var p = ed.Document!.Blocks.OfType<Paragraph>().First();
        var img = p.Inlines.OfType<InlineImage>().Single();

        // Either side of itself is where it is.
        Assert.False(Drop(ed, img, p, 2));
        Assert.False(Drop(ed, img, p, 3));
        Assert.False(ed.CanUndo);

        // Between "c" and "d" (offset 4 counts the picture): once it is taken out ahead of the drop, that gap
        // is offset 3. MID-paragraph on purpose — at the very end an unshifted offset clamps to the same end
        // and the shift goes unobserved (the port's falsification caught exactly that).
        Assert.True(Drop(ed, img, p, 4));
        Assert.Equal("abc[i]def", Text(p));

        ed.Undo();
        Assert.Equal("ab[i]cdef", Text(ed.Document!.Blocks.OfType<Paragraph>().First()));
    }

    [AvaloniaFact]
    public void AnInlineTable_MovesToAnotherParagraph_ButNotIntoItself()
    {
        var ed = Host(P("host", new InlineTable { Table = Table() }), P("dest")).Editor;
        var it = Para(ed, "host[t]").Inlines.OfType<InlineTable>().Single();

        Assert.False(Drop(ed, it, it.Table.Cells[0][0].Para, 1)); // into its own cell

        Assert.True(Drop(ed, it, Para(ed, "dest"), 0));
        Assert.Equal("host,[t]dest", Shape(ed));
    }
}
