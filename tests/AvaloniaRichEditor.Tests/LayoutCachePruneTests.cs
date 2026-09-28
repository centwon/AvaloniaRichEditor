using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Paragraphs an edit removed stayed in the layout cache — with their TextLayouts, and through a layout's picture
// callback their InlineImages — until the cache passed 10,000 entries. Measured 2026-09-28 with the Tests.Render
// LayoutCacheProbe: a 2,000-paragraph document rewritten four times held 346 MB (76 MB after the prune), and
// 40 MB of pictures deleted by editing stayed alive. Every content edit now schedules a prune a second after the
// last one; the tests fire the timer's tick directly instead of waiting.
public class LayoutCachePruneTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Render(RichEditor ed, double w = 400)
    {
        ed.Measure(new Size(w, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, w, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)w, (int)Math.Max(1, Math.Min(4000, ed.DesiredSize.Height))));
        rtb.Render(ed);
        Dispatcher.UIThread.RunJobs();
    }

    private static IDictionary Cache(RichEditor ed) => (IDictionary)typeof(RichEditor).GetField("_layoutCache", NP)!.GetValue(ed)!;
    private static void SelectAll(RichEditor ed) => typeof(RichEditor).GetMethod("SelectAll", NP, Type.EmptyTypes)!.Invoke(ed, null);

    private static RichEditor Editor(int paragraphs, bool withPicture = false)
    {
        var doc = new FlowDocument();
        for (int i = 0; i < paragraphs; i++)
        {
            var p = new Paragraph { Inlines = { new Run { Text = $"paragraph {i} with enough words to be worth a layout" } } };
            if (withPicture && i == 5)
            {
                var img = new InlineImage { Width = 20, Height = 20 };
                img.SetImageData(new byte[] { 1, 2, 3, 4 }, "image/png");
                p.Inlines.Add(img);
            }
            doc.Blocks.Add(p);
        }
        var ed = new RichEditor { Document = doc };
        Render(ed);
        ed.RunScheduledLayoutCachePrune(); // loading is not what these tests are about
        return ed;
    }

    // Own frames, so no temporary in the test's frame keeps the target reachable.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WeakBlock(RichEditor ed, int index) => new(ed.Document!.Blocks[index]);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WeakPicture(RichEditor ed, int index)
        => new(((Paragraph)ed.Document!.Blocks[index]).Inlines.OfType<InlineImage>().Single());

    private static void FullGc() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

    [AvaloniaFact]
    public void ParagraphsAnEditRemoved_LeaveTheCache_OnTheScheduledPrune()
    {
        var ed = Editor(50);
        Assert.Equal(50, Cache(ed).Count);
        var removed = WeakBlock(ed, 7); // select all + type keeps paragraph 0 (it takes the text); 7 leaves

        SelectAll(ed);
        ed.InsertText("x");
        Render(ed);

        Assert.True(ed.LayoutCachePruneScheduled, "an edit's render did not schedule the prune");
        Assert.True(Cache(ed).Count > ed.Document!.Blocks.Count, "the edit left no dead entries — nothing to test");

        ed.RunScheduledLayoutCachePrune();
        FullGc();

        Assert.Equal(ed.Document!.Blocks.Count, Cache(ed).Count);
        Assert.False(removed.IsAlive, "a paragraph the edit removed is still reachable");
        Assert.False(ed.LayoutCachePruneScheduled);
    }

    [AvaloniaFact]
    public void APictureAnEditRemoved_IsNoLongerHeldByACachedLayout()
    {
        var ed = Editor(10, withPicture: true);
        var picture = WeakPicture(ed, 5);

        SelectAll(ed);
        ed.InsertText("x");
        Render(ed);
        ed.RunScheduledLayoutCachePrune();
        FullGc();

        // The undo snapshot holds a CLONE of the picture (sharing its bytes by design); the original element,
        // which the cached layout's picture callback captured, is what must go.
        Assert.False(picture.IsAlive, "the removed InlineImage is still reachable");
    }

    [AvaloniaFact]
    public void ThePrune_KeepsTheLiveLayouts_SoNothingReshapes()
    {
        var ed = Editor(20);
        var keep = (Paragraph)ed.Document!.Blocks[3];
        var before = Cache(ed)[keep];

        ed.Document!.Blocks.RemoveAt(10);
        ed.InsertText("y"); // an edit elsewhere (the caret sits in paragraph 0)
        Render(ed);
        ed.RunScheduledLayoutCachePrune();

        Assert.True(Cache(ed).Contains(keep));
        Assert.Equal(before, Cache(ed)[keep]); // the same entry — same TextLayout instance
    }

    [AvaloniaFact]
    public void ARenderWithoutAnEdit_SchedulesNothing()
    {
        var ed = Editor(5);
        Assert.False(ed.LayoutCachePruneScheduled);
        ed.InvalidateVisual();
        Render(ed); // a caret blink or a scroll repaint
        Assert.False(ed.LayoutCachePruneScheduled);
    }
}
