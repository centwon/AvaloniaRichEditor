using System;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Edits that change the DOCUMENT'S HEIGHT must invalidate measure, or the hosting ScrollViewer keeps
// the extent from before the edit — content grows past the bottom of the scrollable range and stays
// unreachable until some later, unrelated edit happens to re-measure.
//
// Most structural edits get this for free: they end in ResetCaretBlink(), which calls NotifyStatus(),
// which calls InvalidateMeasure(). The table row/column operations and the image size PRESETS are the
// two families that reach neither — they only called InvalidateVisual(), which repaints without
// re-measuring. (The image resize DRAG was never affected; it invalidates on release.)
//
// Each test asserts BOTH halves: the control is marked for re-measure, and re-measuring actually
// reports a different height — so the flag is not just set on an edit that changed nothing.
public class LayoutInvalidationTests
{
    private const double Width = 800;

    // A real 1x1 PNG. The headless platform stubs the decode, which is all these tests need: the size
    // presets read Width/Height when they are set, and only fall back to the bitmap's own size.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static double Measure(RichEditor ed)
    {
        ed.Measure(new Size(Width, double.PositiveInfinity));
        return ed.DesiredSize.Height;
    }

    private static void Invoke(RichEditor ed, string method, params object?[] args)
        => typeof(RichEditor)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ed, args);

    // Row/column structure has no editor-level public API — only the context menu reaches it — so this
    // drives the same private entry points the menu items call (as DocumentInvariantFuzzTests does).
    private static (RichEditor ed, TableBlock tb) TableEditor(int rows = 2, int cols = 2)
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        var tb = new TableBlock(rows, cols);
        doc.Blocks.Add(tb);
        ed.Document = doc;
        ed.FocusDocumentEnd();
        return (ed, tb);
    }

    private static RichEditor ImageEditor(out ImageBlock img)
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        img = new ImageBlock { Width = 100, Height = 100 };
        img.SetImageData(Png, "image/png");
        doc.Blocks.Add(img);
        ed.Document = doc;
        ed.FocusDocumentEnd();
        return ed;
    }

    private static RichEditor InlineImageEditor(out InlineImage img)
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        var p = new Paragraph();
        img = new InlineImage { Width = 100, Height = 100 };
        img.SetImageData(Png, "image/png");
        p.Inlines.Add(new Run { Text = "before " });
        p.Inlines.Add(img);
        doc.Blocks.Add(p);
        ed.Document = doc;
        ed.FocusDocumentEnd();
        return ed;
    }

    // ---- table row / column ------------------------------------------------

    [AvaloniaFact]
    public void TableInsertRow_InvalidatesMeasure()
    {
        var (ed, tb) = TableEditor();
        double before = Measure(ed);
        Assert.True(ed.IsMeasureValid);

        Invoke(ed, "TableInsertRow", tb, 1);

        Assert.False(ed.IsMeasureValid);
        Assert.True(Measure(ed) > before, "a new row must make the document taller");
    }

    [AvaloniaFact]
    public void TableDeleteRow_InvalidatesMeasure()
    {
        var (ed, tb) = TableEditor(rows: 3);
        double before = Measure(ed);

        Invoke(ed, "TableDeleteRow", tb, 1);

        Assert.False(ed.IsMeasureValid);
        Assert.True(Measure(ed) < before, "a deleted row must make the document shorter");
    }

    // Columns keep their own widths, so ADDING one only makes the table wider — and measure reports the
    // AVAILABLE width, never the content's, so the reported size is genuinely unchanged here. Only the
    // flag is asserted: the invalidation is for the cases where a column does move height (deleting the
    // tall column below, and page-break recomputation in paged mode, which happens inside MeasureOverride).
    [AvaloniaFact]
    public void TableInsertColumn_InvalidatesMeasure()
    {
        var (ed, tb) = TableEditor(rows: 1, cols: 1);
        double before = Measure(ed);

        Invoke(ed, "TableInsertColumn", tb, 1);

        Assert.False(ed.IsMeasureValid);
        Assert.Equal(before, Measure(ed), 3); // documents the no-height-change case, not an oversight
    }

    // Deleting the column that holds the tall cell shrinks the row, so here the height really moves.
    [AvaloniaFact]
    public void TableDeleteColumn_InvalidatesMeasure()
    {
        var (ed, tb) = TableEditor(rows: 1, cols: 2);
        tb.Cells[0][0].Para.Inlines.Add(new Run { Text = new string('x', 200) });
        double before = Measure(ed);

        Invoke(ed, "TableDeleteColumn", tb, 0);

        Assert.False(ed.IsMeasureValid);
        Assert.True(Measure(ed) < before, "losing the tall column must make the document shorter");
    }

    // ---- image size presets ------------------------------------------------

    [AvaloniaFact]
    public void ScaleImageSize_InvalidatesMeasure()
    {
        var ed = ImageEditor(out var img);
        double before = Measure(ed);

        Invoke(ed, "ScaleImageSize", img, 2.0);

        Assert.Equal(200, img.Height, 3);
        Assert.False(ed.IsMeasureValid);
        Assert.True(Measure(ed) > before, "a doubled picture must make the document taller");
    }

    [AvaloniaFact]
    public void ResetImageSize_InvalidatesMeasure()
    {
        var ed = ImageEditor(out var img);
        double before = Measure(ed);

        Invoke(ed, "ResetImageSize", img);

        Assert.False(ed.IsMeasureValid);
        Assert.True(Measure(ed) < before, "natural size is smaller than the 100pt display size here");
    }

    [AvaloniaFact]
    public void ScaleInlineImageSize_InvalidatesMeasure()
    {
        var ed = InlineImageEditor(out var img);
        double before = Measure(ed);

        Invoke(ed, "ScaleInlineImageSize", img, 2.0);

        Assert.Equal(200, img.Height, 3);
        Assert.False(ed.IsMeasureValid);
        Assert.True(Measure(ed) > before, "a taller inline image grows its line box");
    }

    [AvaloniaFact]
    public void ResetInlineImageSize_InvalidatesMeasure()
    {
        var ed = InlineImageEditor(out var img);
        double before = Measure(ed);

        Invoke(ed, "ResetInlineImageSize", img);

        Assert.False(ed.IsMeasureValid);
        Assert.True(Measure(ed) < before, "the line box shrinks back to the text's own height");
    }
}
