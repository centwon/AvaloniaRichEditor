using System.Collections.Generic;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Page view: the room a table or picture left at the bottom of a page when it moved whole to the next page is that page's
// last line. Mapped to the slice's very end, a pointer there sat in the moved object's top-edge grab zone — hovering showed
// the "move" cursor as if the room were the table — and a selected picture's frame was drawn across the room (2026-11-08).
public class PageRoomClickTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AClickInTheRoom_DoesNotReachTheMovedObject(bool picture, bool selected)
    {
        var ed = new RichEditor { ViewMode = RichEditorViewMode.Page, PageSize = RichEditorPageSize.A4 };
        var filler = new TableBlock(1, 1);
        filler.RowHeights.Clear(); filler.RowHeights.Add(ed.PaperContentHeight - 200);
        Block moved;
        if (picture) moved = new ImageBlock { Width = 300, Height = 300 };
        else
        {
            var tb = new TableBlock(1, 2);
            tb.RowHeights.Clear(); tb.RowHeights.Add(300);
            moved = tb;
        }
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(filler);
        doc.Blocks.Add(moved);
        ed.Document = doc;
        var host = InteractionHost.Create(ed, 1000, 2600);
        try
        {
            if (selected) { typeof(RichEditor).GetField("_selectedBlock", NP)!.SetValue(ed, moved); ed.InvalidateVisual(); }
            host.Render();
            var breaks = (List<double>)typeof(RichEditor).GetMethod("EnsurePageBreaks", NP)!.Invoke(ed, null)!;
            Assert.True(breaks.Count >= 2, "the second block should start a second page");
            var sliceEnd = ed.MapDocToView(new Point(40, breaks[1] - 0.01));
            var room = ed.TranslatePoint(new Point(sliceEnd.X, sliceEnd.Y + 40), host.Window)!.Value;

            host.Window.MouseMove(room);
            host.Pump();
            var move = typeof(RichEditor).GetProperty("MoveCursor", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            Assert.NotSame(move, ed.Cursor);

            host.Window.MouseDown(room, MouseButton.Left);
            host.Pump();
            host.Window.MouseUp(room, MouseButton.Left);
            host.Pump();

            Assert.False((bool)typeof(RichEditor).GetField("_isResizingImage", NP)!.GetValue(ed)!, "the click started a picture resize");
            Assert.Null(typeof(RichEditor).GetField("_selectedBlock", NP)!.GetValue(ed));
            Assert.Null(typeof(RichEditor).GetField("_caretBlock", NP)!.GetValue(ed));
        }
        finally { host.Dispose(); }
    }
}
