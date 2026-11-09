using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A table drawn from the empty area below the last line (2026-11-08: the preview follows the
// result). The table goes after the last line — the caret lands there — so the rubber band starts there too. It started
// under the pointer, and the table went in above where it was shown. As the WinUI control.
public class TableDrawEmptyAreaTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    [AvaloniaFact]
    public void PressedBelowTheLastLine_TheBandAndTheTableStartAtTheContentsBottom()
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "a" } } });
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "b" } } });
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        try
        {
            host.Render();
            var ed = host.Editor;
            double bottom = (double)typeof(RichEditor).GetMethod("MeasureContentHeight", NP)!.Invoke(ed, [ed.Bounds.Width])!;
            Assert.True(bottom < 150, $"content bottom {bottom} — the press below would not be in empty space");

            typeof(RichEditor).GetMethod("BeginTableDraw", NP)!.Invoke(ed, [2, 2]);
            host.Press(new Point(40, 300));
            var start = (Point)typeof(RichEditor).GetField("_tableDrawStart", NP)!.GetValue(ed)!;
            Assert.Equal(bottom, start.Y, 1);
            host.Move(new Point(160, 380), global::Avalonia.Input.RawInputModifiers.LeftMouseButton);
            host.Release(new Point(160, 380));

            var blocks = ed.Document!.Blocks;
            var tb = blocks.OfType<TableBlock>().Single();
            Assert.True(blocks.IndexOf(tb) > 1, "the table should follow the last line");
            Assert.Equal(380 - bottom, tb.RowHeights.Sum(), 1); // sized from where the band started
        }
        finally { host.Dispose(); }
    }
}
