using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// In page view, dragging a row's bottom edge down until the row no longer fits its page made the page flicker
// (2026-11-08): the row moved to the next page, the page breaks moved with it, and the same screen point mapped to another
// document Y — the travel measured in document space shrank, the row fitted again, and the next move pushed it over.
// Measured on the surface, the row grows by exactly the pointer's travel, whatever the pages do.
public class RowResizePageViewTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    [AvaloniaFact]
    public void ARowDraggedPastThePage_GrowsSteadily()
    {
        var ed = new RichEditor { ViewMode = RichEditorViewMode.Page, PageSize = RichEditorPageSize.A4 };
        var tb = new TableBlock(2, 2);
        tb.RowHeights.Clear();
        tb.RowHeights.Add(ed.PaperContentHeight - 260);
        tb.RowHeights.Add(100);
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(tb);
        ed.Document = doc;
        var host = InteractionHost.Create(ed, 1000, 2600); // tall enough that the whole drag stays in the window
        try
        {
            host.Render();
            var bounds = (List<(Rect rect, TableBlock tb, int rowIndex, double height)>)typeof(RichEditor).GetField("_rowBoundaries", NP)!.GetValue(ed)!;
            var edge = bounds.Single(b => ReferenceEquals(b.tb, tb) && b.rowIndex == 1).rect;
            // Inside the first column, clear of the column edge; document → surface → control → window.
            var control = ed.MapDocToView(new Point(edge.X + 40, edge.Y + edge.Height / 2));
            var at = ed.TranslatePoint(control, host.Window)!.Value;
            double start = tb.RowHeights[1];

            host.Window.MouseDown(at, MouseButton.Left);
            host.Pump();
            var heights = new List<double>();
            for (int k = 1; k <= 16; k++)
            {
                host.Window.MouseMove(new Point(at.X, at.Y + 20 * k), RawInputModifiers.LeftMouseButton);
                host.Pump();
                host.Render();
                heights.Add(tb.RowHeights[1]);
            }
            host.Window.MouseUp(new Point(at.X, at.Y + 320), MouseButton.Left);
            host.Pump();

            for (int k = 1; k <= 16; k++)
                Assert.True(Math.Abs(heights[k - 1] - (start + 20 * k)) < 1,
                    $"move {k}: row {heights[k - 1]}, expected {start + 20 * k} — all: {string.Join(", ", heights)}");
            Assert.True(ed.GetPrintPageCount() >= 2, "the row never reached the next page");
        }
        finally { host.Dispose(); }
    }
}
