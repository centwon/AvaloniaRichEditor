using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;

namespace AvaloniaRichEditor.Controls;

// One print page as a visual, for Avalonia's Skia bridge (DrawingContextHelper.RenderAsync renders a
// Visual, not a callback): SavePdf's vector path points it at each page in turn. It draws exactly what
// RenderPrintPage rasterizes — RichEditor.DrawPrintPage.
internal sealed class PrintPageVisual : Control
{
    private readonly RichEditor _editor;
    private readonly IReadOnlyList<double> _breaks;

    public PrintPageVisual(RichEditor editor, IReadOnlyList<double> breaks)
    {
        _editor = editor;
        _breaks = breaks;
    }

    public int PageIndex { get; set; }

    // Paper DIPs → the target's units (72/96 for PDF points), applied here rather than left to the bridge:
    // its dpi argument does not scale the drawing (measured — a 96-DIP page drawn onto a 72-pt PDF page ran
    // a third past the right edge).
    public double Scale { get; set; } = 1;

    public override void Render(DrawingContext context)
    {
        using var scaled = context.PushTransform(Avalonia.Matrix.CreateScale(Scale, Scale));
        _editor.DrawPrintPage(context, PageIndex, _breaks);
    }
}
