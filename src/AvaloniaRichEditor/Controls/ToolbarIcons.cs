using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace AvaloniaRichEditor.Controls;

// Built-in vector glyphs for RichEditorToolbar's default look — hand-drawn stroke icons on a 24×24
// canvas, scaled into a 16px box. Keeps the library icon-dependency-free (a host can still override
// any slot via RichEditorIcons.Provider, which wins over these). Only the symbol buttons are
// vectorized; letter-conventional ones (B/I/U/S, color "A") stay as styled text and Create returns
// null for them so the caller falls back to the glyph.
internal static class ToolbarIcons
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#3C4043"));

    // A layer is one path: stroke (outline) or fill (solid shapes like arrowheads/dots).
    private static Control Build(double box, params (string Data, bool Fill)[] layers)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var (data, fill) in layers)
        {
            var p = new Path { Data = Geometry.Parse(data) };
            if (fill)
            {
                p.Fill = Ink;
            }
            else
            {
                p.Stroke = Ink;
                p.StrokeThickness = 2;
                p.StrokeLineCap = PenLineCap.Round;
                p.StrokeJoin = PenLineJoin.Round;
            }
            canvas.Children.Add(p);
        }
        return new Viewbox { Width = box, Height = box, Child = canvas, Stretch = Stretch.Uniform };
    }

    /// <summary>Built-in vector for a toolbar slot, or null if that slot uses a styled-text glyph.</summary>
    public static Control? Create(RichEditorIcon kind) => kind switch
    {
        RichEditorIcon.FormatPainter => Build(16,
            ("M4 5 H15 V9 H4 Z", false),
            ("M15 7 H18 V11 H10.5 V13", false),
            ("M8.5 13 H12.5 V20 H8.5 Z", false)),

        RichEditorIcon.IndentIncrease => Build(16,
            ("M4 6 H20 M4 18 H20 M11 12 H20", false),
            ("M4 9 L8 12 L4 15 Z", true)),
        RichEditorIcon.IndentDecrease => Build(16,
            ("M4 6 H20 M4 18 H20 M11 12 H20", false),
            ("M8 9 L4 12 L8 15 Z", true)),

        RichEditorIcon.InsertTable => Build(16,
            ("M3 5 H21 V19 H3 Z M3 11 H21 M3 15 H21 M9 5 V19 M15 5 V19", false)),

        RichEditorIcon.InsertImage => Build(16,
            ("M3 5 H21 V19 H3 Z", false),
            ("M3 16 L9 11 L13 15 L16 12 L21 16", false),
            ("M9 10 m-1.6 0 a1.6 1.6 0 1 0 3.2 0 a1.6 1.6 0 1 0 -3.2 0 Z", true)),

        RichEditorIcon.InsertDivider => Build(16,
            ("M4 12 H20", false)),

        RichEditorIcon.Undo => Build(16,
            ("M5 11 H14 A4.5 4.5 0 0 1 14 20 H9", false),
            ("M5 11 L9 7.5 L9 14.5 Z", true)),
        RichEditorIcon.Redo => Build(16,
            ("M19 11 H10 A4.5 4.5 0 0 0 10 20 H15", false),
            ("M19 11 L15 7.5 L15 14.5 Z", true)),

        RichEditorIcon.Highlight => Build(16,
            ("M13 4 L20 11 L12 19 H6 L4 17 Z M6 19 L11 14", false)),

        _ => null,
    };

    /// <summary>Small downward chevron for dropdown buttons (e.g. the table picker).</summary>
    public static Control ChevronDown() => Build(10, ("M6 9 L12 15 L18 9", false));
}
