using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

/// <summary>The caret format report — <see cref="RichEditor.GetCaretFormat"/>, what the toolbar shows.
/// <para>The contract that makes it checkable: the report is the toolbar's claim about the text the NEXT
/// KEYSTROKE writes, and about what is on screen. Measured 2026-09-12, after the WinUI port found the same
/// root there, it broke both ways here too:</para>
/// <list type="bullet">
/// <item>Beside an image, insertion and report had separate rules and disagreed at 6 caret positions — the
/// report fell back to the paragraph's FIRST run, typing wrote a plain new run or joined the run after the
/// image. One rule now (<c>TypingSource</c>): the nearest text before the caret skipping objects, else after.</item>
/// <item>A heading is drawn bold; the report said "not bold", and Ctrl+B flipped a hidden flag with no
/// visible change. Now reported as drawn, and a toggle that cannot change what is shown does nothing.</item>
/// <item>Typing at a link's end — or its start — extended the link; now it writes plain text (Word).</item>
/// <item>ClearFormatting in a heading wrote the host's DefaultFontSize as an explicit size: H1 20 → 14.</item>
/// </list>
/// <para>Ported from the peer's <c>ControlCaretFormatTests</c>. Not ported: the link-blue report (links are
/// not recoloured here) and <c>CurrentLinkUri</c> (no such API here).</para></summary>
public class CaretFormatReportTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type T = typeof(RichEditor);
    private const string Url = "https://a.example/";

    // ---- plumbing ---------------------------------------------------------------------------------

    private static Run Plain(string t) => new() { Text = t };
    private static Run Bold(string t) => new() { Text = t, FontWeight = FontWeight.Bold };
    private static Run Ital(string t) => new() { Text = t, FontStyle = FontStyle.Italic, FontSize = 14, Foreground = Brushes.Red };
    private static Run Mono(string t) => new() { Text = t, FontFamily = "Consolas", Background = Brushes.Yellow };
    private static Run Strike(string t) => new()
    {
        Text = t, FontSize = 12,
        TextDecorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Strikethrough } },
    };
    private static Run Link(string t) => new() { Text = t, NavigateUri = Url };
    private static InlineImage Img() => new() { Width = 10, Height = 10 };

    private static RichEditor Editor(params Paragraph[] paras)
    {
        var doc = new FlowDocument();
        foreach (var p in paras) doc.Blocks.Add(p);
        return new RichEditor { Document = doc };
    }

    private static Paragraph Para(int heading, params Inline[] inlines)
    {
        var p = new Paragraph { HeadingLevel = heading };
        foreach (var i in inlines) p.Inlines.Add(i);
        return p;
    }

    private static Paragraph Para(params Inline[] inlines) => Para(0, inlines);

    private static List<Paragraph> Paras(RichEditor ed) => ed.Document!.Blocks.OfType<Paragraph>().ToList();

    private static void Caret(RichEditor ed, Paragraph p, int offset)
    {
        foreach (var f in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            T.GetField(f, NP)!.SetValue(ed, new TextPointer(p, offset));
    }

    private static void Select(RichEditor ed, Paragraph a, int ao, Paragraph b, int bo)
    {
        T.GetField("_selectionStart", NP)!.SetValue(ed, new TextPointer(a, ao));
        T.GetField("_selectionEnd", NP)!.SetValue(ed, new TextPointer(b, bo));
        T.GetField("_caretPosition", NP)!.SetValue(ed, new TextPointer(b, bo));
    }

    private static Run RunAt(Paragraph p, int offset)
    {
        int pos = 0;
        foreach (var inl in p.Inlines)
        {
            int len = inl is Run r0 ? (r0.Text?.Length ?? 0) : 1;
            if (inl is Run r && offset >= pos && offset < pos + len) return r;
            pos += len;
        }
        throw new InvalidOperationException($"no run holds offset {offset}");
    }

    private static int Length(Paragraph p) => p.Inlines.Sum(i => i is Run r ? r.Text?.Length ?? 0 : 1);

    private static bool HasDecoration(Run r, TextDecorationLocation loc)
        => r.TextDecorations?.Any(d => d.Location == loc) ?? false;

    // What a run looks like on screen, written out here rather than borrowed from the product, so the
    // oracle is independent of the rules under test (no headings in the sweep — see the heading tests).
    private static string Look(Run r) => string.Join(" ",
        r.FontWeight == FontWeight.Bold ? "bold" : "-",
        r.FontStyle == FontStyle.Italic ? "italic" : "-",
        (r.TextDecorations == null ? !string.IsNullOrEmpty(r.NavigateUri) : HasDecoration(r, TextDecorationLocation.Underline)) ? "under" : "-",
        HasDecoration(r, TextDecorationLocation.Strikethrough) ? "strike" : "-",
        $"{r.FontSize}pt", r.FontFamily ?? "(family)", Hex(r.Foreground), Hex(r.Background));

    private static string Look(RichEditor.CaretFormat f) => string.Join(" ",
        f.Bold ? "bold" : "-", f.Italic ? "italic" : "-", f.Underline ? "under" : "-", f.Strike ? "strike" : "-",
        $"{f.FontSize}pt", f.FontFamily ?? "(family)", Hex(f.Foreground), Hex(f.Background));

    private static string Hex(IBrush? b) => b is ISolidColorBrush s ? s.Color.ToString() : "none";

    // ---- the report predicts the keystroke --------------------------------------------------------

    private static readonly (string Name, Func<Paragraph> Make)[] Layouts =
    {
        ("bold italic", () => Para(Bold("BB"), Ital("II"))),
        ("[img] bold italic", () => Para(Img(), Bold("BB"), Ital("II"))),
        ("bold [img]", () => Para(Bold("BB"), Img())),
        ("bold [img] italic", () => Para(Bold("BB"), Img(), Ital("II"))),
        ("bold [img][img] italic", () => Para(Bold("BB"), Img(), Img(), Ital("II"))),
        ("italic [img] bold", () => Para(Ital("II"), Img(), Bold("BB"))),
        ("mono [img] strike", () => Para(Mono("MM"), Img(), Strike("SS"))),
        ("[img] only", () => Para(Img())),
        ("styled empty run", () => Para(Bold(""))),
        ("link plain", () => Para(Link("ab"), Plain(" cd"))),
        ("plain link plain", () => Para(Plain("ab "), Link("cd"), Plain(" ef"))),
        ("[img] link", () => Para(Img(), Link("cd"))),
    };

    // At EVERY caret position of every layout: the format the toolbar reports is the format the character
    // typed there gets. Before: "[img] bold italic" at 0, "bold [img]" at 3, "bold [img] italic" at 3,
    // "bold [img][img] italic" at 3 and 4, "italic [img] bold" at 3.
    [AvaloniaFact]
    public void AtEveryCaretPosition_TheReportIsTheFormatOfTheCharacterTypedThere()
    {
        var wrong = new List<string>();
        foreach (var (name, make) in Layouts)
        {
            int len = Length(make());
            for (int off = 0; off <= len; off++)
            {
                var ed = Editor(make());
                Caret(ed, Paras(ed)[0], off);
                string reported = Look(ed.GetCaretFormat());
                ed.InsertText("Z");
                string typed = Look(RunAt(Paras(ed)[0], off));
                if (reported != typed) wrong.Add($"[{name}] at {off}: reported {reported} | typed {typed}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    // The rule itself: the nearest text BEFORE the caret, skipping images; with none before, the nearest
    // after. Before, "bold [img]|" typed plain and "bold [img]|italic" typed italic.
    [AvaloniaTheory]
    [InlineData("bold [img]|", 3, true)]
    [InlineData("bold [img]|italic", 3, true)]
    [InlineData("|[img] bold", 0, true)]
    [InlineData("[img]|bold", 1, true)]
    [InlineData("italic [img]|bold", 3, false)]
    public void TypingBesideAnImage_TakesTheNearestTextBefore_ElseAfter(string layout, int caret, bool bold)
    {
        var ed = Editor(layout switch
        {
            "bold [img]|" => Para(Bold("BB"), Img()),
            "bold [img]|italic" => Para(Bold("BB"), Img(), Ital("II")),
            "|[img] bold" => Para(Img(), Bold("BB")),
            "[img]|bold" => Para(Img(), Bold("BB")),
            _ => Para(Ital("II"), Img(), Bold("BB")),
        });
        Caret(ed, Paras(ed)[0], caret);
        ed.InsertText("Z");
        var z = RunAt(Paras(ed)[0], caret);
        Assert.Equal(bold, z.FontWeight == FontWeight.Bold);
        Assert.Equal(!bold, z.FontStyle == FontStyle.Italic);
    }

    // ---- links ------------------------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData(4, false)] // the link's end
    [InlineData(0, false)] // its start
    [InlineData(2, true)]  // inside
    public void TypingAtALinksEdge_WritesPlainText_InsideContinuesIt(int caret, bool inLink)
    {
        var ed = Editor(Para(Link("link"), Plain(" after")));
        Caret(ed, Paras(ed)[0], caret);
        Assert.Equal(inLink, ed.GetCaretFormat().Underline);

        ed.InsertText("Z");
        Assert.Equal(inLink ? Url : null, RunAt(Paras(ed)[0], caret).NavigateUri);
        string linked = string.Concat(Paras(ed)[0].Inlines.OfType<Run>().Where(r => r.NavigateUri == Url).Select(r => r.Text));
        Assert.Equal(inLink ? "liZnk" : "link", linked);
    }

    [AvaloniaFact]
    public void TypingAtTheSeamOfASplitLink_ContinuesIt()
    {
        var head = Link("li");
        head.FontWeight = FontWeight.Bold;
        var ed = Editor(Para(head, Link("nk"), Plain(" after")));
        Caret(ed, Paras(ed)[0], 2);
        ed.InsertText("Z");
        Assert.Equal(Url, RunAt(Paras(ed)[0], 2).NavigateUri);
    }

    [AvaloniaFact]
    public void TypingOnAtALinksEnd_StaysInOnePlainRun()
    {
        var ed = Editor(Para(Link("link")));
        Caret(ed, Paras(ed)[0], 4);
        ed.InsertText("Z");
        ed.InsertText("Y");
        var p = Paras(ed)[0];
        Assert.Same(RunAt(p, 4), RunAt(p, 5));
        Assert.Equal("ZY", RunAt(p, 4).Text);
        Assert.Null(RunAt(p, 4).NavigateUri);
    }

    // Ctrl+U inside a plain link: drawn underlined whatever the flag says — nothing to change, no undo step.
    [AvaloniaFact]
    public void CtrlU_OnALink_DoesNothing()
    {
        var ed = Editor(Para(Link("link"), Plain(" after")));
        Caret(ed, Paras(ed)[0], 2);
        ed.ToggleUnderline();
        Assert.False(ed.CanUndo);
        Assert.Null(RunAt(Paras(ed)[0], 1).TextDecorations);
        Assert.True(ed.GetCaretFormat().Underline);
    }

    // At a link's end the caret is in the link's WORD, and that word is what Ctrl+U styles — all of it
    // drawn underlined, so nothing to do. The typed-text format (plain there) must not decide it: judged by
    // that, the toggle set a hidden underline flag on the link.
    [AvaloniaFact]
    public void CtrlU_AtALinksEnd_LeavesTheLinkAlone()
    {
        var ed = Editor(Para(Link("link"), Plain(" after")));
        Caret(ed, Paras(ed)[0], 4);
        ed.ToggleUnderline();
        Assert.False(ed.CanUndo);
        Assert.Null(RunAt(Paras(ed)[0], 1).TextDecorations);
    }

    // A link that carries another decoration is NOT drawn underlined, so there the toggle is real.
    [AvaloniaFact]
    public void CtrlU_OnAStruckLink_Underlines()
    {
        var link = Strike("link");
        link.NavigateUri = Url;
        var ed = Editor(Para(link, Plain(" after")));
        Caret(ed, Paras(ed)[0], 2);
        Assert.False(ed.GetCaretFormat().Underline);
        ed.ToggleUnderline();
        Assert.True(HasDecoration(RunAt(Paras(ed)[0], 1), TextDecorationLocation.Underline));
    }

    [AvaloniaFact]
    public void CtrlU_OverALinkAndPlainText_UnderlinesThePlainText()
    {
        var ed = Editor(Para(Link("link"), Plain(" after")));
        var p = Paras(ed)[0];
        Select(ed, p, 0, p, 10);
        ed.ToggleUnderline();
        Assert.True(HasDecoration(RunAt(Paras(ed)[0], 6), TextDecorationLocation.Underline));
    }

    // ---- headings ---------------------------------------------------------------------------------

    [AvaloniaFact]
    public void AHeading_IsReportedBold()
    {
        var ed = Editor(Para(1, Plain("Title")));
        Caret(ed, Paras(ed)[0], 2);
        Assert.True(ed.GetCaretFormat().Bold);
    }

    [AvaloniaFact]
    public void CtrlB_OnAHeading_DoesNothing()
    {
        var ed = Editor(Para(1, Plain("Title")));
        var p = Paras(ed)[0];
        Select(ed, p, 0, p, 5);
        ed.ToggleBold();
        Assert.False(ed.CanUndo);
        Assert.Equal(FontWeight.Normal, RunAt(Paras(ed)[0], 1).FontWeight);
    }

    [AvaloniaFact]
    public void CtrlB_AtAnEmptyCaretInAHeading_ArmsNothing()
    {
        var ed = Editor(Para(1, Plain("Title ")));
        Caret(ed, Paras(ed)[0], 6);
        ed.ToggleBold();
        Assert.Null(T.GetField("_pendingCaretStyles", NP)!.GetValue(ed));
        ed.InsertText("Z");
        Assert.Equal(FontWeight.Normal, RunAt(Paras(ed)[0], 6).FontWeight);
    }

    [AvaloniaFact]
    public void CtrlB_OverAHeadingAndBodyText_TogglesTheBody()
    {
        var ed = Editor(Para(1, Plain("Title")), Para(Plain("text")));
        var ps = Paras(ed);
        Select(ed, ps[0], 0, ps[1], 4);
        ed.ToggleBold();
        Assert.Equal(FontWeight.Bold, RunAt(Paras(ed)[1], 1).FontWeight);
        ps = Paras(ed);
        Select(ed, ps[0], 0, ps[1], 4);
        ed.ToggleBold();
        Assert.Equal(FontWeight.Normal, RunAt(Paras(ed)[1], 1).FontWeight);
    }

    // ---- ClearFormatting (reached through the context menu; private here) --------------------------

    private static void ClearFormatting(RichEditor ed) => T.GetMethod("ClearFormatting", NP)!.Invoke(ed, null);

    [AvaloniaFact]
    public void ClearFormatting_KeepsAHeadingAtItsSize_AndClearsBodyTextToTheHostDefault()
    {
        var ed = Editor(Para(1, Plain("Title")), Para(new Run { Text = "text", FontSize = 12 }));
        ed.DefaultFontSize = 14;
        var ps = Paras(ed);
        Select(ed, ps[0], 0, ps[1], 4);
        ClearFormatting(ed);
        ps = Paras(ed);
        Caret(ed, ps[0], 3);
        Assert.Equal(20, ed.GetCaretFormat().FontSize);
        Caret(ed, ps[1], 3);
        Assert.Equal(14, ed.GetCaretFormat().FontSize);
    }

    [AvaloniaFact]
    public void APendingClearFormattingInAHeading_PreviewsTheHeadingSize()
    {
        var ed = Editor(Para(1, Plain("Title ")));
        ed.DefaultFontSize = 14;
        Caret(ed, Paras(ed)[0], 6);
        ClearFormatting(ed);
        Assert.Equal(20, ed.GetCaretFormat().FontSize);
        ed.InsertText("Z");
        Caret(ed, Paras(ed)[0], 7);
        Assert.Equal(20, ed.GetCaretFormat().FontSize);
    }
}
