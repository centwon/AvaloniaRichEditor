using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Round 15. Two consecutive Shift+Enters put an EMPTY line inside a paragraph ("a\n\nb"), and laying
// that out froze the editor: Avalonia 12.0.x's text wrapper loops in PerformTextWrapping creating empty
// lines until the process runs out of memory. Reduced to a call with none of this library in it —
// `new TextLayout("a\n\nb", …, textWrapping: Wrap)` — it hangs on 12.0.1 through 12.0.5 and is fixed in
// 12.1. The dependency floor is 12.1 for this reason; these tests are what says so out loud.
//
// ⚠️ A regression here does not fail, it HANGS (an OOM loop is not catchable, so the test host dies and
// takes the run's report with it). If this file stops finishing, the Avalonia reference went backwards.
public class SoftBreakLayoutTests
{
    [AvaloniaFact]
    public void AnEmptySoftLine_LaysOutInsteadOfHanging()
    {
        var doc = TestHelpers.Doc(TestHelpers.Para(new Run { Text = "a\n\nb" }));
        var ed = new RichEditor { Document = doc };
        ed.Measure(new Size(700, double.PositiveInfinity));

        // Three lines of text, so the paragraph is taller than the two-line version.
        var two = new RichEditor { Document = TestHelpers.Doc(TestHelpers.Para(new Run { Text = "a\nb" })) };
        two.Measure(new Size(700, double.PositiveInfinity));
        Assert.True(ed.DesiredSize.Height > two.DesiredSize.Height,
            $"the blank line should occupy one ({ed.DesiredSize.Height} vs {two.DesiredSize.Height})");
    }

    // The same shape split across runs, which is what an importer built before round 15 coalesced them:
    // a run holding nothing but the break. It hit the same loop from the other direction.
    [AvaloniaFact]
    public void ARunHoldingOnlyTheSoftBreak_LaysOutInsteadOfHanging()
    {
        var doc = TestHelpers.Doc(TestHelpers.Para(
            new Run { Text = "a" }, new Run { Text = "\n" }, new Run { Text = "b" }));
        var ed = new RichEditor { Document = doc };
        ed.Measure(new Size(700, double.PositiveInfinity));

        Assert.True(ed.DesiredSize.Height > 0);
    }
}
