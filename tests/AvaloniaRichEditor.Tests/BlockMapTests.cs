using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// P3 (2026-11-02): the hit-tests visit only the blocks near the point, found in the block map the measure walk
// builds, instead of walking from the first block. The map is an optimisation and must never change an answer, so
// every hit-test is asked the same questions twice — with the map, and with it switched off (the walk from the top,
// which is what the code did before) — over a document of every block kind, at points all the way down it.
public class BlockMapTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type T = typeof(RichEditor);

    private static Paragraph P(string text, double marginBottom = 0)
    {
        var p = TestHelpers.Para(new Run { Text = text });
        p.MarginBottom = marginBottom;
        return p;
    }

    private static RichEditor Editor()
    {
        string longText = string.Join(" ", Enumerable.Repeat("wrapping words across several lines", 12));
        var table = new TableBlock(2, 2);
        foreach (var row in table.Cells) foreach (var cell in row) { cell.Blocks.Clear(); cell.Blocks.Add(P("cell")); }
        var spaced = new TableBlock(1, 2) { MarginTop = 24 };
        var link = TestHelpers.Para(new Run { Text = "a " }, new Run { Text = "link", NavigateUri = "https://example.com" }, new Run { Text = " b" });
        var img = new ImageBlock { Width = 120, Height = 60 };
        var blocks = new List<Block>
        {
            P("first"), P(longText, 12), P(""), table, P("after table"), link, img, P("after image", 6),
            new DividerBlock(), P(longText), spaced, P(""), P("last"),
        };
        for (int i = 0; i < 6; i++) { blocks.Add(P("filler " + i, i % 3 * 4)); blocks.Add(P(longText)); }
        var ed = new RichEditor { Document = TestHelpers.Doc(blocks.ToArray()) };
        ed.Measure(new Size(700, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, 700, ed.DesiredSize.Height));
        // A fresh document counts as an edit until a render takes it: the map is (rightly) not used meanwhile.
        T.GetField("_textChangedPending", NP)!.SetValue(ed, false);
        return ed;
    }

    private static object? Call(RichEditor ed, string name, params object[] args)
        => T.GetMethod(name, NP)!.Invoke(ed, args);

    private static string Describe(object? r) => r switch
    {
        null => "null",
        TextPointer tp => $"{tp.Paragraph?.GetHashCode()}:{tp.Offset}:{tp.AtLineEnd}",
        double d => d.ToString("R"),
        // The table geometry (a private type): where its rows land, which is what a wrong start would move.
        _ when r!.GetType().Name == "TableLayout" => string.Join("/", (double[])r.GetType().GetField("RowY")!.GetValue(r)!),
        System.Runtime.CompilerServices.ITuple t => string.Join(",", Enumerable.Range(0, t.Length).Select(i => Describe(t[i]))),
        _ => $"{r.GetType().Name}#{r.GetHashCode()}",
    };

    // Each question, with the map and without it.
    private static (string with, string without) Both(RichEditor ed, Func<object?> ask)
    {
        var valid = T.GetField("_blockMapValid", NP)!;
        string with = Describe(ask());
        valid.SetValue(ed, false);
        try { return (with, Describe(ask())); }
        finally { valid.SetValue(ed, true); }
    }

    [AvaloniaFact]
    public void EveryHitTest_AnswersAlike_WithAndWithoutTheMap()
    {
        var ed = Editor();
        Assert.True((bool)T.GetMethod("BlockMapUsable", NP)!.Invoke(ed, new object[] { ed.Bounds.Width })!,
            "the map is not in use here, so this would compare the old walk with itself");

        double height = ed.DesiredSize.Height;
        int asked = 0;
        var failures = new List<string>();
        foreach (double x in new[] { 2.0, 12, 60, 300, 690 })
            for (double y = -20; y < height + 40; y += 3)
            {
                var pt = new Point(x, y);
                foreach (var name in new[] { "GetPositionFromPoint", "GetBlockAtPoint", "GetLinkRunAtPoint", "TableLeftOrTopBorderAtPoint" })
                {
                    var (with, without) = Both(ed, () => Call(ed, name, pt));
                    asked++;
                    if (with != without) failures.Add($"{name}({x}, {y}): map {with}, walk {without}");
                }
                var (w2, wo2) = Both(ed, () => Call(ed, "BlockAtY", y));
                if (w2 != wo2) failures.Add($"BlockAtY({y}): map {w2}, walk {wo2}");
            }
        foreach (var block in ed.Document!.Blocks)
        {
            if (block is Paragraph para)
            {
                var (w, wo) = Both(ed, () => Call(ed, "NextBlockTop", para));
                if (w != wo) failures.Add($"NextBlockTop: map {w}, walk {wo}");
            }
            else if (block is TableBlock tb)
            {
                var (w, wo) = Both(ed, () => Call(ed, "GetTableRect", tb));
                if (w != wo) failures.Add($"GetTableRect: map {w}, walk {wo}");
            }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} of {asked}+ answers differ:\n" + string.Join("\n", failures.GetRange(0, Math.Min(10, failures.Count))));
    }

    // The map is a by-product of a measure: any change that moves a block invalidates the measure, and until the
    // next one the walks go from the top again.
    [AvaloniaFact]
    public void AnEditOrAnInvalidatedMeasure_StopsTheMapUntilTheNextMeasure()
    {
        var ed = Editor();
        var usable = T.GetMethod("BlockMapUsable", NP)!;
        bool Usable() => (bool)usable.Invoke(ed, new object[] { ed.Bounds.Width })!;
        Assert.True(Usable());

        ed.InvalidateMeasure();
        Assert.False(Usable());
        ed.Measure(new Size(700, double.PositiveInfinity));
        Assert.True(Usable());

        T.GetField("_textChangedPending", NP)!.SetValue(ed, true); // what every edit raises before it changes anything
        Assert.False(Usable());
    }
}
