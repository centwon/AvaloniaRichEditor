using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A block image inside a table cell gets the same corner handle as a top-level one, but the drag was
// never driven through the window at that depth ??only the top-level case was. Reported from the demo:
// the handle does not resize, in a cell or in a cell of a nested table.
public class CellImageResizeInteractionTests
{
    // A decodable 1x1 PNG: the cell render path only registers a handle for an image it could decode
    // (`cimg.Image is { } bmp`), so a byte-less placeholder would never produce one.
    private static readonly byte[] Png = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static ImageBlock Image(double w = 60, double h = 40)
    {
        var img = new ImageBlock { Width = w, Height = h };
        img.SetImageData(Png, "image/png");
        return img;
    }

    private static InteractionHost Host(FlowDocument doc)
    {
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        var host = InteractionHost.Create(ed);
        host.Render(); // handles are recorded while painting
        return host;
    }

    // Table whose first cell holds an image, with wide columns so the cell does not clamp it.
    private static (FlowDocument doc, ImageBlock img) CellImageDoc()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 2);
        tb.ColumnWidths[0] = 300; tb.ColumnWidths[1] = 300;
        var img = Image();
        tb.Cells[0][0].Blocks.Add(img);
        doc.Blocks.Add(tb);
        return (doc, img);
    }

    // The same, one level deeper: outer cell -> nested table -> its cell holds the image.
    private static (FlowDocument doc, ImageBlock img) NestedCellImageDoc()
    {
        var doc = new FlowDocument();
        var outer = new TableBlock(1, 1);
        outer.ColumnWidths[0] = 400;
        var nested = new TableBlock(1, 1);
        nested.ColumnWidths[0] = 300;
        var img = Image();
        nested.Cells[0][0].Blocks.Add(img);
        outer.Cells[0][0].Blocks.Add(nested);
        doc.Blocks.Add(outer);
        return (doc, img);
    }

    [AvaloniaFact]
    public void AnImageInACell_HasAResizeHandle()
    {
        var (doc, img) = CellImageDoc();
        var host = Host(doc);
        Assert.Contains(host.ImageHandles, h => ReferenceEquals(h.img, img));
    }

    [AvaloniaFact]
    public void DraggingTheHandleOfAnImageInACell_ResizesIt()
    {
        var (doc, img) = CellImageDoc();
        var host = Host(doc);
        double before = img.Width;

        var handle = host.ImageHandles.First(h => ReferenceEquals(h.img, img));
        host.Drag(handle.rect.Center, handle.rect.Center + new Point(50, 0));

        Assert.True(img.Width > before + 20, $"width {img.Width} should have grown from {before}");
    }

    [AvaloniaFact]
    public void DraggingTheHandleOfAnImageInANestedCell_ResizesIt()
    {
        var (doc, img) = NestedCellImageDoc();
        var host = Host(doc);
        double before = img.Width;

        var handle = host.ImageHandles.First(h => ReferenceEquals(h.img, img));
        host.Drag(handle.rect.Center, handle.rect.Center + new Point(50, 0));

        Assert.True(img.Width > before + 20, $"width {img.Width} should have grown from {before}");
    }

    // The case the demo actually hits. InsertImage caps a picture to the DOCUMENT width, so an image
    // dropped into a cell is normally far wider than the cell and the cell render scales it down to fit
    // (CellImageSize). The handle therefore sits at the SCALED right edge, while the drag arithmetic
    // started from the declared width — so a drag of a few dozen px only moved the declared size within
    // the range that still clamps to the same scaled width, and the image did not move at all. Shrinking
    // it needed a drag longer than the difference between the two, which is why the handle looked dead.
    [AvaloniaFact]
    public void DraggingTheHandleOfAnImageWiderThanItsCell_ResizesItRelativeToWhatIsDrawn()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 2);
        tb.ColumnWidths[0] = 200; tb.ColumnWidths[1] = 200;
        var img = Image(w: 900, h: 600);   // far wider than the 200px column, as an inserted photo is
        tb.Cells[0][0].Blocks.Add(img);
        doc.Blocks.Add(tb);
        var host = Host(doc);

        var handle = host.ImageHandles.First(h => ReferenceEquals(h.img, img));
        double drawnBefore = handle.rect.Right - 9; // the handle sits at the drawn right edge

        // Drag the handle 60px to the LEFT: the image must get about 60px narrower than it is drawn.
        host.Drag(handle.rect.Center, handle.rect.Center - new Point(60, 0));
        host.Render();

        var after = host.ImageHandles.FirstOrDefault(h => ReferenceEquals(h.img, img));
        Assert.NotEqual(default, after);
        double drawnAfter = after.rect.Right - 9;
        Assert.True(drawnBefore - drawnAfter > 40,
            $"the drawn width must follow the drag ({drawnBefore} -> {drawnAfter})");
    }

    // Intended behaviour, pinned so a later change to the resize path does not alter it silently
    // (decision 2026-07-31): a cell caps how WIDE an image is drawn, but not how wide it is. Dragging
    // right grows the image up to the cell and then keeps growing the stored size with no visible
    // effect — CSS max-width semantics, so the picture takes the room back if the column is widened
    // later. The alternative (clamp the stored size to the cell, making the drag strictly WYSIWYG) was
    // considered and rejected: it would collapse an inserted photo's real size on the first drag.
    [AvaloniaFact]
    public void AnImageInACell_IsCappedWhenDrawnButKeepsItsLargerStoredSize()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 2);
        tb.ColumnWidths[0] = 200; tb.ColumnWidths[1] = 200;
        var img = Image(w: 150, h: 100);
        tb.Cells[0][0].Blocks.Add(img);
        doc.Blocks.Add(tb);
        var host = Host(doc);

        // The cell's content box is its column less the cell padding the render walk applies.
        const double cellInner = 200 - 10;

        var handle = host.ImageHandles.First(h => ReferenceEquals(h.img, img));
        Assert.Equal(150, handle.drawnW, 1);                    // fits so far: drawn at its declared size
        host.Drag(handle.rect.Center, handle.rect.Center + new Point(300, 0));
        host.Render();

        var after = host.ImageHandles.First(h => ReferenceEquals(h.img, img));
        // Drawn: grew to the cell and stopped there.
        Assert.True(after.drawnW > 150, $"drawn {after.drawnW} should have grown toward the cell edge");
        Assert.True(after.drawnW <= cellInner + 1, $"drawn {after.drawnW} must not exceed the cell {cellInner}");
        // Stored: kept growing past it, and surfaces once the column is widened.
        Assert.True(img.Width > after.drawnW + 50, $"stored {img.Width} vs drawn {after.drawnW}");

        var colHandle = host.ColumnHandles.First(h => h.colIndex == 0);
        host.Drag(colHandle.rect.Center, colHandle.rect.Center + new Point(250, 0));
        host.Render();

        double drawnWider = host.ImageHandles.First(h => ReferenceEquals(h.img, img)).drawnW;
        Assert.True(drawnWider > after.drawnW + 50,
            $"a wider column must let the stored size show ({after.drawnW} -> {drawnWider})");
    }

    // Growing the image has to grow the row that holds it on the same frame. A resize mutates the model
    // without going through an edit, so the frame runs as a "trusted" pass that hands back the cached
    // table geometry unless the enclosing chain is evicted ??the same defect class as the round 3
    // inline-table row resize.
    [AvaloniaFact]
    public void ResizingAnImageInACell_GrowsTheRowOnTheSameFrame()
    {
        var (doc, img) = CellImageDoc();
        var host = Host(doc);
        double heightBefore = host.Editor.DesiredSize.Height;

        // The corner handle drives off horizontal movement and keeps the aspect ratio, so a wider
        // image is also a taller one.
        var handle = host.ImageHandles.First(h => ReferenceEquals(h.img, img));
        host.Drag(handle.rect.Center, handle.rect.Center + new Point(120, 0));
        host.Editor.Measure(new Size(host.Editor.Bounds.Width, double.PositiveInfinity));

        Assert.True(host.Editor.DesiredSize.Height > heightBefore,
            $"the row must grow with the image ({host.Editor.DesiredSize.Height} vs {heightBefore})");
    }
}

