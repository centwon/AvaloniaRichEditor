using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Picture resize handles (from the WinUI port, 2026-09-19): the corner keeps the proportions, the middle of
// the right edge changes only the width, the middle of the bottom edge only the height. Before, the corner
// was the only handle, so a picture's proportions could not be changed at all without a file. Driven through
// the window, like the other resize drags.
public class PictureResizeHandleInteractionTests
{
    private static readonly byte[] Png = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static ImageBlock Picture(double w, double h)
    {
        var img = new ImageBlock { Width = w, Height = h };
        img.SetImageData(Png, "image/png");
        return img;
    }

    private static InteractionHost Host(FlowDocument doc, ImageBlock? select = null)
    {
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        var host = InteractionHost.Create(ed);
        host.Render();
        if (select != null) host.Select(select);
        return host;
    }

    private static (InteractionHost host, ImageBlock img) TopLevelPicture(double w = 200, double h = 150)
    {
        var doc = new FlowDocument();
        var img = Picture(w, h);
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "top" } } });
        doc.Blocks.Add(img);
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "end" } } });
        return (Host(doc, img), img);
    }

    private static Rect HandleOf(InteractionHost host, ImageBlock img, string grip)
        => host.ImageHandles.Single(h => ReferenceEquals(h.img, img) && h.grip == grip).rect;

    [AvaloniaFact]
    public void ASelectedPicture_HasACornerAndTwoEdgeHandles_CornerFirst()
    {
        var (host, img) = TopLevelPicture();
        var grips = host.ImageHandles.Where(h => ReferenceEquals(h.img, img)).Select(h => h.grip).ToArray();
        Assert.Equal(new[] { "Corner", "Right", "Bottom" }, grips);
    }

    [AvaloniaTheory]
    [InlineData("Right", 60, 40, 260, 150)]   // width only; the vertical movement is ignored
    [InlineData("Bottom", 40, 50, 200, 200)]  // height only; the horizontal movement is ignored
    [InlineData("Corner", 100, -70, 300, 225)] // proportions kept (4:3), driven by the horizontal movement
    public void EachHandle_ChangesItsOwnSides_AsOneUndoStep(string grip, double dx, double dy, double w, double h)
    {
        var (host, img) = TopLevelPicture();
        var from = HandleOf(host, img, grip).Center;

        host.Drag(from, from + new Point(dx, dy));

        Assert.Equal((w, h), (img.Width, img.Height));
        host.Editor.Undo();
        var back = host.Editor.Document!.Blocks.OfType<ImageBlock>().Single();
        Assert.Equal((200.0, 150.0), (back.Width, back.Height));
    }

    // A cell draws a picture scaled down to its width, and the drag starts from what is DRAWN. An edge handle
    // writes both sides from the drawn size — writing only the height would leave the declared 400px width,
    // which the cell scales down again, so the edge would move a fraction of the pointer's distance.
    [AvaloniaFact]
    public void InACell_TheBottomEdgeFollowsThePointer()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 1);
        tb.ColumnWidths[0] = 150;
        var img = Picture(400, 300);
        tb.Cells[0][0].Blocks.Add(img);
        doc.Blocks.Add(tb);
        var host = Host(doc, img);
        var before = host.CellImageRects.Single(c => ReferenceEquals(c.img, img)).rect;
        Assert.True(before.Width < 400, $"the cell did not scale the picture down (drawn {before.Width})");

        var from = HandleOf(host, img, "Bottom").Center;
        host.Drag(from, from + new Point(0, 30));
        host.Render();

        var after = host.CellImageRects.Single(c => ReferenceEquals(c.img, img)).rect;
        Assert.Equal(before.Width, after.Width, 1);
        Assert.Equal(before.Height + 30, after.Height, 1);
    }

    [AvaloniaFact]
    public void AnInlinePicture_HasTheEdgeHandlesToo()
    {
        var doc = new FlowDocument();
        var inline = new InlineImage { Width = 40, Height = 30 };
        inline.SetImageData(Png, "image/png");
        var p = new Paragraph { Inlines = { new Run { Text = "before " }, inline, new Run { Text = " after" } } };
        doc.Blocks.Add(p);
        var host = Host(doc);
        typeof(RichEditor).GetField("_selectedInline", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(host.Editor, ((Paragraph, InlineImage)?)(p, inline));
        host.Render();

        var right = host.InlineImageHandles.Single(h => ReferenceEquals(h.img, inline) && h.grip == "Right").rect;
        host.Drag(right.Center, right.Center + new Point(20, 15));

        Assert.Equal((60.0, 30.0), (inline.Width, inline.Height));
    }

    [AvaloniaTheory]
    [InlineData("Right", StandardCursorType.SizeWestEast)]
    [InlineData("Bottom", StandardCursorType.SizeNorthSouth)]
    [InlineData("Corner", StandardCursorType.BottomRightCorner)]
    public void HoveringAHandle_ShowsItsCursor(string grip, StandardCursorType expected)
    {
        var (host, img) = TopLevelPicture();
        host.Move(HandleOf(host, img, grip).Center);

        // The editor caches one Cursor per type (a native handle), so identity is the comparison.
        var cache = (System.Collections.IDictionary)typeof(RichEditor)
            .GetField("_cursorCache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.Same(cache[expected], host.Editor.Cursor);
    }

    [AvaloniaFact]
    public void AViewer_DoesNotResize()
    {
        var (host, img) = TopLevelPicture();
        var right = HandleOf(host, img, "Right").Center;
        host.Editor.IsReadOnly = true;

        host.Drag(right, right + new Point(60, 0));

        Assert.Equal((200.0, 150.0), (img.Width, img.Height));
    }
}
