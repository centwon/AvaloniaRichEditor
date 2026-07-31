using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Long-session behaviour: what the editor still holds on to after the document it belonged to is gone.
// Stale references are two problems at once — they keep the old document alive, and the commands that
// act on them operate on blocks that are no longer in the tree.
public class LifetimeAndStateTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static T Field<T>(RichEditor ed, string name)
        => (T)typeof(RichEditor).GetField(name, NP)!.GetValue(ed)!;

    private static void SetField(RichEditor ed, string name, object? value)
        => typeof(RichEditor).GetField(name, NP)!.SetValue(ed, value);

    private static void Press(RichEditor ed, Key key)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    private static (FlowDocument doc, ImageBlock img) DocWithImage()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "a" } } });
        var img = new ImageBlock { Width = 40, Height = 40 };
        doc.Blocks.Add(img);
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "b" } } });
        return (doc, img);
    }

    // Selecting a block and then loading a different document must not leave the selection pointing
    // into the old one: the old document stays reachable, and Delete acts on a block that is not there.
    [AvaloniaFact]
    public void ReplacingTheDocument_ClearsTheSelectedBlock()
    {
        var ed = new RichEditor();
        var (docA, img) = DocWithImage();
        ed.Document = docA;
        SetField(ed, "_selectedBlock", img);

        ed.Document = new FlowDocument();

        Assert.Null(Field<Block?>(ed, "_selectedBlock"));
    }

    [AvaloniaFact]
    public void ReplacingTheDocument_ClearsTheBlockCaret()
    {
        var ed = new RichEditor();
        var (docA, img) = DocWithImage();
        ed.Document = docA;
        SetField(ed, "_caretBlock", img);

        ed.Document = new FlowDocument();

        Assert.Null(Field<Block?>(ed, "_caretBlock"));
    }

    [AvaloniaFact]
    public void ReplacingTheDocument_ClearsTheSelectedInlineImage()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        var host = new Paragraph();
        var im = new InlineImage { Width = 16, Height = 16 };
        host.Inlines.Add(im);
        doc.Blocks.Add(host);
        ed.Document = doc;
        SetField(ed, "_selectedInline", (host, im));

        ed.Document = new FlowDocument();

        Assert.Null(typeof(RichEditor).GetField("_selectedInline", NP)!.GetValue(ed));
    }

    // Cell-selection mode holds the table it belongs to.
    [AvaloniaFact]
    public void ReplacingTheDocument_LeavesCellSelectionMode()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        var tb = new TableBlock(2, 2);
        doc.Blocks.Add(tb);
        ed.Document = doc;
        SetField(ed, "_cellSelMode", true);
        SetField(ed, "_cellSelTable", tb);

        ed.Document = new FlowDocument();

        Assert.False(Field<bool>(ed, "_cellSelMode"));
        Assert.Null(Field<TableBlock?>(ed, "_cellSelTable"));
    }

    // Pressing Delete with a stale selection must not damage the new document.
    [AvaloniaFact]
    public void DeleteAfterReplacingTheDocument_DoesNotTouchTheNewOne()
    {
        var ed = new RichEditor();
        var (docA, img) = DocWithImage();
        ed.Document = docA;
        SetField(ed, "_selectedBlock", img);

        var docB = new FlowDocument();
        docB.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "kept" } } });
        ed.Document = docB;
        Press(ed, Key.Delete);

        Assert.Contains(ed.Document!.Blocks.OfType<Paragraph>(), p => p.Text() == "kept");
    }

    // Both layout caches key on model identity, so a block deleted while editing lingers in them until
    // the cache is swept. That is by design (the sweep is amortized), but the ceiling has to actually
    // hold: an editing session that churns tables must not grow the geometry cache forever. The
    // paragraph cache prunes dead entries at 10000; the table cache clears wholesale at 2000.
    [AvaloniaFact]
    public void TableChurn_KeepsTheGeometryCacheBounded()
    {
        var ed = new RichEditor();
        ed.Document = new FlowDocument();
        ed.FocusDocumentEnd();

        // Enough rounds to push past the cache's ceiling; every round leaves a dead TableBlock behind.
        for (int i = 0; i < 2400; i++)
        {
            ed.InsertTable(2, 2);
            ed.Measure(new Avalonia.Size(800, double.PositiveInfinity));
            var tb = ed.Document!.Blocks.OfType<TableBlock>().FirstOrDefault();
            if (tb != null)
            {
                SetField(ed, "_selectedBlock", tb);
                Press(ed, Key.Delete);
            }
            ed.Measure(new Avalonia.Size(800, double.PositiveInfinity));
        }

        var tableCache = Field<System.Collections.IDictionary>(ed, "_tableLayoutCache");
        Assert.True(tableCache.Count <= 2001,
            $"the table geometry cache grew past its ceiling ({tableCache.Count})");
        var paraCache = Field<System.Collections.IDictionary>(ed, "_layoutCache");
        Assert.True(paraCache.Count <= 10001,
            $"the paragraph layout cache grew past its ceiling ({paraCache.Count})");
    }
}
