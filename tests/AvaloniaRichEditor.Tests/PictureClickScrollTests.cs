using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Clicking a block picture while the caret was far away sent the view to the caret (2026-11-08): the click
// selects the picture and leaves the caret where it was, and the caret scroll that every caret change asks for then
// brought THAT into view. Headless rendering shows no scrolling, so these count the bring-into-view requests the
// control raises instead — the event its scroll owner acts on.
public class PictureClickScrollTests
{
    private static readonly byte[] Png = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static ImageBlock Picture()
    {
        var img = new ImageBlock { Width = 60, Height = 40 };
        img.SetImageData(Png, "image/png");
        return img;
    }

    private static Paragraph P(string text) => new() { Inlines = { new Run { Text = text } } };

    // A picture at the top (top-level, and one in a table cell) and the caret parked in the last of many paragraphs.
    private static (InteractionHost host, ImageBlock top, ImageBlock inCell, int[] requests) Setup()
    {
        var doc = new FlowDocument();
        var top = Picture();
        doc.Blocks.Clear();
        doc.Blocks.Add(P("first"));
        doc.Blocks.Add(top);
        var tb = new TableBlock(1, 1);
        tb.ColumnWidths[0] = 300;
        var inCell = Picture();
        tb.Cells[0][0].Blocks.Add(inCell);
        doc.Blocks.Add(tb);
        for (int i = 0; i < 80; i++) doc.Blocks.Add(P("line " + i));

        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        var host = InteractionHost.Create(ed);
        ed.FocusDocumentEnd();
        host.Render();
        host.Pump();

        var requests = new int[1];
        ed.AddHandler(Control.RequestBringIntoViewEvent, (_, _) => requests[0]++);
        return (host, top, inCell, requests);
    }

    private static Rect PictureRect(InteractionHost host, ImageBlock img, bool inCell)
    {
        if (inCell) return host.CellImageRects.First(ci => ReferenceEquals(ci.img, img)).rect;
        // A top-level picture registers no rect until it is selected; its block box is where GetBlockAtPoint looks.
        host.Select(img);
        host.Render();
        var r = host.ImageHandles.First(h => ReferenceEquals(h.img, img)).rect;
        host.Select(null);
        host.Render();
        return r;
    }

    [AvaloniaTheory]
    [InlineData(false, MouseButton.Left)]
    [InlineData(true, MouseButton.Left)]
    [InlineData(false, MouseButton.Right)]
    [InlineData(true, MouseButton.Right)]
    public void ClickingAPicture_SelectsIt_WithoutScrollingToTheOldCaret(bool inCell, MouseButton button)
    {
        var (host, top, cellPicture, requests) = Setup();
        var img = inCell ? cellPicture : top;
        var caretBefore = host.Caret;
        var rect = PictureRect(host, img, inCell);
        requests[0] = 0;

        host.Click(rect.Center - new Point(5, 5), button);
        host.Render();
        host.Pump();

        Assert.Same(img, host.SelectedBlock);
        Assert.Equal(caretBefore, host.Caret); // the caret stays where it was...
        Assert.Equal(0, requests[0]);         // ...and the view is not carried to it
    }

    // The control: clicking text does ask for the caret to be brought into view, so a zero above is the fix and not
    // a probe that never fires.
    [AvaloniaFact]
    public void ClickingText_StillBringsTheCaretIntoView()
    {
        var (host, _, _, requests) = Setup();
        host.Click(new Point(30, 10));
        host.Render();
        host.Pump();
        Assert.True(requests[0] > 0);
    }
}
