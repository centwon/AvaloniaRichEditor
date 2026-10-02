using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

/// <summary>A heading's format lives on its runs (<c>HeadingStyle</c>, 2026-10-01 — the WinUI port's model
/// since 2026-09-13, brought here so one file is one document in both editors).
/// <para>Applying a level writes bold and the level's size onto every run — each time, the same level again
/// included; in between they are ordinary run attributes. Until then the renderer forced both: in an H1 the bold
/// toggle did nothing and 10pt could not be shown, and a port file whose heading text the user had un-bolded
/// came back bold here and lost its marker on save. The contract pinned here is "what the user changes in a
/// heading shows, and survives": undo, save/load, HTML, and the level changes and splits that rewrite a heading's
/// runs. Ported from the port's <c>ControlHeadingStyleTests</c>, plus the split that keeps the heading (a block
/// inserted mid-heading), which only this editor has.</para>
/// </summary>
public class HeadingStyleTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type T = typeof(RichEditor);

    private static Run Plain(string t) => new() { Text = t };

    private static Paragraph Para(int heading, params Inline[] inlines)
    {
        var p = new Paragraph { HeadingLevel = heading };
        foreach (var i in inlines) p.Inlines.Add(i);
        return p;
    }

    private static RichEditor Editor(params Paragraph[] paras)
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        foreach (var p in paras) doc.Blocks.Add(p);
        return new RichEditor { Document = doc };
    }

    private static List<Paragraph> Paras(RichEditor ed) => ed.Document!.Blocks.OfType<Paragraph>().ToList();

    private static void Caret(RichEditor ed, Paragraph p, int offset)
    {
        foreach (var f in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            T.GetField(f, NP)!.SetValue(ed, new TextPointer(p, offset));
    }

    private static void Select(RichEditor ed, Paragraph p, int a, int b)
    {
        T.GetField("_selectionStart", NP)!.SetValue(ed, new TextPointer(p, a));
        T.GetField("_selectionEnd", NP)!.SetValue(ed, new TextPointer(p, b));
        T.GetField("_caretPosition", NP)!.SetValue(ed, new TextPointer(p, b));
    }

    private static void Enter(RichEditor ed)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

    // The run holding the character AT offset.
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

    private static bool Bold(Run r) => r.FontWeight == FontWeight.Bold;

    // ---- the reported case ------------------------------------------------------------------------

    // In a heading, bold can be turned off and 10pt shows — both on the model and in the report (which is
    // the renderer's rule). Before, the toggle was a no-op and 10pt drew, and reported, at 20.
    [AvaloniaFact]
    public void InAHeading_BoldCanBeTurnedOff_AndTenPointShows()
    {
        var ed = Editor(Para(0, Plain("Title")));
        var p = Paras(ed)[0];
        Caret(ed, p, 2);
        ed.SetHeading(1);
        Assert.True(Bold(RunAt(p, 0)));             // the preset, written once
        Assert.Equal(20, RunAt(p, 0).FontSize);

        Select(ed, p, 0, 5);
        ed.ToggleBold();
        ed.SetFontSize(10);
        Caret(ed, p, 2);
        var f = ed.GetCaretFormat();
        Assert.False(f.Bold);
        Assert.Equal(10, f.FontSize);
    }

    // Applying a heading writes its preset onto every run — a different level, and the SAME level again, which
    // is how a heading's look comes back after the user changed its bold or size. In between, the user's
    // changes stand.
    [AvaloniaTheory]
    [InlineData(2, 16.0)]
    [InlineData(1, 20.0)] // the same level again
    public void ApplyingAHeading_WritesItsPresetOnEveryRun_TheSameLevelToo(int again, double size)
    {
        var ed = Editor(Para(0, Plain("Title rest")));
        var p = Paras(ed)[0];
        Caret(ed, p, 2);
        ed.SetHeading(1);
        Select(ed, p, 0, 5);
        ed.ToggleBold();
        ed.SetFontSize(12);
        Assert.False(Bold(RunAt(p, 0)));              // the changes stand...
        Assert.Equal(12, RunAt(p, 0).FontSize);

        Caret(ed, p, 7);
        ed.SetHeading(again);                         // ...until the heading is applied again
        foreach (int off in new[] { 0, 7 })
        {
            Assert.True(Bold(RunAt(p, off)), $"offset {off} is not bold after applying H{again}");
            Assert.Equal(size, RunAt(p, off).FontSize);
        }
    }

    // Back to body takes the preset off every run, a size the user chose in the heading included — the same
    // "applying a style applies its format" rule, the other way.
    [AvaloniaFact]
    public void BackToBody_TakesThePresetOffEveryRun()
    {
        var ed = Editor(Para(0, Plain("Title")));
        var p = Paras(ed)[0];
        Caret(ed, p, 2);
        ed.SetHeading(1);
        Select(ed, p, 0, 2);
        ed.SetFontSize(24);
        Caret(ed, p, 3);
        ed.SetHeading(0);
        foreach (int off in new[] { 0, 3 })
        {
            Assert.False(Bold(RunAt(p, off)));
            Assert.Equal(10, RunAt(p, off).FontSize);
        }
    }

    // ---- it survives ------------------------------------------------------------------------------

    // Undo restores a snapshot through the Document property, which is where old documents are converted.
    // The flag rides in the snapshot, so the restore does not re-bold text the user un-bolded.
    [AvaloniaFact]
    public void UndoAndRedo_DoNotReBoldAnUnboldedHeading()
    {
        var ed = Editor(Para(1, Plain("Title")));
        Select(ed, Paras(ed)[0], 0, 5);
        ed.ToggleBold();
        Assert.False(Bold(RunAt(Paras(ed)[0], 1)));
        ed.Undo();
        Assert.True(Bold(RunAt(Paras(ed)[0], 1)));
        ed.Redo();
        Assert.False(Bold(RunAt(Paras(ed)[0], 1)));
    }

    // Twice, because a marker that is written but not read (or read but not written) shows on the SECOND
    // load only.
    [AvaloniaFact]
    public void SaveAndLoad_KeepsAnUnboldedHeading_Twice()
    {
        var ed = Editor(Para(1, Plain("Title")));
        Select(ed, Paras(ed)[0], 0, 5);
        ed.ToggleBold();
        string json = DocumentSerializer.Serialize(ed.Document!);
        for (int i = 0; i < 2; i++)
        {
            var again = new RichEditor { Document = DocumentSerializer.Deserialize(json) };
            var r = RunAt(Paras(again)[0], 1);
            Assert.False(Bold(r), $"load {i + 1}: the un-bolded heading came back bold");
            Assert.Equal(20, r.FontSize);
            json = DocumentSerializer.Serialize(again.Document!);
        }
    }

    // A file from before this change has unstyled heading runs that the old renderer drew bold at the heading
    // size; opened in the editor it must still look that way. Built from a host model no editor has seen,
    // which also checks that such a model is saved WITHOUT the marker — and that the reader leaves it as it is
    // (the native formats read back exactly what was saved; the editor converts).
    [AvaloniaFact]
    public void AnOldFile_OpensWithItsHeadingsBoldAtTheHeadingSize()
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(Para(2, Plain("Old title")));
        string json = DocumentSerializer.Serialize(doc);
        Assert.DoesNotContain("HeadingFormat", json);

        var loaded = DocumentSerializer.Deserialize(json);
        Assert.False(Bold(RunAt(loaded.Blocks.OfType<Paragraph>().First(), 1)));

        var ed = new RichEditor { Document = loaded };
        var r = RunAt(Paras(ed)[0], 1);
        Assert.True(Bold(r));
        Assert.Equal(16, r.FontSize);
        Assert.Contains("\"HeadingFormat\":1", DocumentSerializer.Serialize(ed.Document!)); // converted once, and says so
    }

    // HTML: <h1> is bold and heading-sized to every reader, so un-bolded text and a 10pt run inside one must
    // say so. Twice, for the same reason as the JSON test.
    [AvaloniaFact]
    public void HtmlRoundTrip_KeepsUnboldedAndTenPointHeadingText_Twice()
    {
        var ed = Editor(Para(1, Plain("Title rest")));
        var p = Paras(ed)[0];
        Select(ed, p, 0, 5);
        ed.ToggleBold();
        ed.SetFontSize(10);
        string html = HtmlDocumentFormatter.ToHtml(ed.Document!);
        for (int i = 0; i < 2; i++)
        {
            var again = new RichEditor { Document = HtmlDocumentFormatter.ParseHtml(html) };
            var hp = Paras(again)[0];
            Assert.Equal(1, hp.HeadingLevel);
            Assert.False(Bold(RunAt(hp, 0)), $"pass {i + 1}: un-bolded heading text came back bold");
            Assert.Equal(10, RunAt(hp, 0).FontSize);
            Assert.True(Bold(RunAt(hp, 7)));
            Assert.Equal(20, RunAt(hp, 7).FontSize);
            html = HtmlDocumentFormatter.ToHtml(again.Document!);
        }
    }

    // RTF has no headings to read back, so only the writer is at stake: it added \b to every heading run.
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rtf_WritesAHeadingRunsBoldAsItIs(bool unbold)
    {
        var ed = Editor(Para(1, Plain("Title")));
        if (unbold) { Select(ed, Paras(ed)[0], 0, 5); ed.ToggleBold(); }
        string rtf = RtfDocumentFormatter.Write(ed.Document!);
        var group = Regex.Match(rtf, @"\{([^{}]*)Title").Groups[1].Value;
        Assert.Equal(!unbold, Regex.IsMatch(group, @"\\b(?![a-z])"));
    }

    // Parsing honours font-weight:normal, which the export above relies on — and which Google Docs puts on
    // the <b> it wraps every paste in; everything used to come in bold.
    [AvaloniaFact]
    public void Html_FontWeightNormal_UnboldsWhatAnEnclosingElementMadeBold()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<p><b style=\"font-weight:normal\">plain <span style=\"font-weight:700\">bold</span></b></p>");
        var p = doc.Blocks.OfType<Paragraph>().First();
        Assert.False(Bold(RunAt(p, 0)));
        Assert.True(Bold(RunAt(p, 6)));
    }

    // ---- the edits that rewrite a heading's runs --------------------------------------------------

    // Enter at a heading's end starts body text; in the middle the tail becomes body text and leaves the
    // heading's format behind; at the start the heading keeps its format on the empty run it is left with.
    [AvaloniaTheory]
    [InlineData(5)]
    [InlineData(2)]
    public void Enter_InAHeading_TheBodyParagraphIsBodyText(int at)
    {
        var ed = Editor(Para(1, Plain("Title")));
        Caret(ed, Paras(ed)[0], at);
        Enter(ed);
        ed.InsertText("x");
        var ps = Paras(ed);
        Assert.Equal(0, ps[1].HeadingLevel);
        Assert.False(Bold(RunAt(ps[1], 0)));
        Assert.Equal(10, RunAt(ps[1], 0).FontSize);
        Assert.True(Bold(RunAt(ps[0], 0)));        // the heading itself untouched
        Assert.Equal(20, RunAt(ps[0], 0).FontSize);
    }

    [AvaloniaFact]
    public void Enter_AtAHeadingsStart_LeavesAHeadingThatStillTypesAsHeading()
    {
        var ed = Editor(Para(1, Plain("Title")));
        Caret(ed, Paras(ed)[0], 0);
        Enter(ed);
        var ps = Paras(ed);
        Caret(ed, ps[0], 0);
        ed.InsertText("N");
        Assert.True(Bold(RunAt(Paras(ed)[0], 0)));
        Assert.Equal(20, RunAt(Paras(ed)[0], 0).FontSize);
    }

    // A heading with no text to take a format from types with the heading's preset, as the report says.
    [AvaloniaFact]
    public void TypingIntoAnEmptyHeading_WritesHeadingText()
    {
        var ed = Editor(new Paragraph { HeadingLevel = 2 });
        var p = Paras(ed)[0];
        Caret(ed, p, 0);
        var f = ed.GetCaretFormat();
        Assert.True(f.Bold);
        Assert.Equal(16, f.FontSize);
        ed.InsertText("Z");
        Assert.True(Bold(RunAt(Paras(ed)[0], 0)));
        Assert.Equal(16, RunAt(Paras(ed)[0], 0).FontSize);
    }

    // A block inserted mid-heading splits it, and the tail CONTINUES the heading: it keeps the level, and its
    // text the heading's format (the user's un-bolding included). Taking the format off as Enter does, then
    // restoring only the level, left a heading drawn as body text. Only this editor keeps the tail a heading.
    [AvaloniaFact]
    public void ABlockInsertedMidHeading_LeavesTheTailAHeadingInItsOwnFormat()
    {
        var ed = Editor(Para(1, Plain("Title")));
        var p = Paras(ed)[0];
        Select(ed, p, 3, 5);
        ed.ToggleBold();                              // "le" un-bolded
        Caret(ed, Paras(ed)[0], 3);
        ed.InsertDivider();
        var ps = Paras(ed);
        var tail = ps[^1];
        Assert.Equal("le", string.Concat(tail.Inlines.OfType<Run>().Select(r => r.Text)));
        Assert.Equal(1, tail.HeadingLevel);
        Assert.False(Bold(RunAt(tail, 0)));           // as the user left it
        Assert.Equal(20, RunAt(tail, 0).FontSize);
        Assert.True(Bold(RunAt(ps[0], 0)));
    }
}
