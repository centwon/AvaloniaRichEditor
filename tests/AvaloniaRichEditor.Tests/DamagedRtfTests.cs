using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Damaged RTF used to blank the open document: Parse() swallowed the exception and returned an empty
// FlowDocument, LoadRtf loaded that over whatever was open, and the next save wrote the blank over the
// original file. 1.0 had already established the opposite contract for JSON — LoadJson's own doc comment
// says a damaged file is REPORTED rather than read as an empty document — and RTF was simply left out of
// it. TryParse separates "damaged" from "genuinely empty"; LoadRtf keeps what is open.
//
// A control word's parameter is int.Parse'd, so a digit run too long for int aborts the parse mid-way.
// That is the fixture used throughout: valid RTF envelope, one unparseable value.
public class DamagedRtfTests
{
    private const string Damaged = @"{\rtf1\ansi\fs99999999999999999999 x\par}";
    private const string Valid = @"{\rtf1\ansi hello\par}";

    private static string AllText(FlowDocument d)
        => string.Concat(d.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).Select(r => r.Text));

    // ---- formatter ---------------------------------------------------------

    [Fact]
    public void TryParse_ReportsDamagedInput()
    {
        Assert.False(RtfDocumentFormatter.TryParse(Damaged, out var doc, out string? error));
        Assert.NotNull(error);
        Assert.Empty(doc.Blocks); // failure yields an empty document, never a half-read one
    }

    [Fact]
    public void TryParse_SucceedsOnValidInput()
    {
        Assert.True(RtfDocumentFormatter.TryParse(Valid, out var doc, out string? error));
        Assert.Null(error);
        Assert.Contains("hello", AllText(doc), StringComparison.Ordinal);
    }

    // An RTF that parses cleanly but carries nothing is a SUCCESS — conflating it with damage is the
    // very confusion this method exists to remove.
    [Fact]
    public void TryParse_TreatsAnEmptyDocumentAsSuccess()
    {
        Assert.True(RtfDocumentFormatter.TryParse(@"{\rtf1\ansi}", out _, out string? error));
        Assert.Null(error);
    }

    // The old contract stays: paste depends on it (an empty result falls through to HTML/plain text),
    // so changing Parse would reroute a working path.
    [Fact]
    public void Parse_StillReturnsEmptyOnDamagedInput()
        => Assert.Empty(RtfDocumentFormatter.Parse(Damaged).Blocks);

    // ---- control -----------------------------------------------------------

    // The defect this whole change exists for.
    [AvaloniaFact]
    public void LoadRtf_DamagedInput_KeepsTheOpenDocument()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>precious</p>");
        var before = ed.Document;

        ed.LoadRtf(Damaged);

        Assert.Same(before, ed.Document); // not even replaced by an equal document
        Assert.Contains("precious", AllText(ed.Document!), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void LoadRtf_ValidInput_ReplacesTheDocument()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>old</p>");
        ed.LoadRtf(Valid);
        Assert.Contains("hello", AllText(ed.Document!), StringComparison.Ordinal);
        Assert.DoesNotContain("old", AllText(ed.Document!), StringComparison.Ordinal);
    }

    // Nothing open means nothing to protect, and bailing would leave the editor inert (null Document,
    // no caret) — so an empty document is the better landing spot.
    [AvaloniaFact]
    public void LoadRtf_DamagedInput_WithNothingOpen_LoadsAnEmptyDocument()
    {
        var ed = new RichEditor();
        Assert.Null(ed.Document);
        ed.LoadRtf(Damaged);
        Assert.NotNull(ed.Document);
    }

    // Not-RTF keeps the documented "empty document" behaviour — it is not a damaged file, it is a
    // caller passing something that was never RTF.
    [AvaloniaFact]
    public void LoadRtf_NotRtf_LoadsAnEmptyDocument()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>old</p>");
        ed.LoadRtf("this is not rtf");
        Assert.Empty(AllText(ed.Document!));
    }

    // ---- diagnostics -------------------------------------------------------

    private static List<RichEditorFaultEventArgs> CaptureFaults(Action body)
    {
        var seen = new List<RichEditorFaultEventArgs>();
        void Handler(object? _, RichEditorFaultEventArgs e) { lock (seen) seen.Add(e); }
        RichEditorDiagnostics.Reset();
        RichEditorDiagnostics.Fault += Handler;
        try { body(); }
        finally { RichEditorDiagnostics.Fault -= Handler; RichEditorDiagnostics.Reset(); }
        return seen;
    }

    [Fact]
    public void Diagnostics_ReportsTheSwallowedParseFault()
    {
        var faults = CaptureFaults(() => RtfDocumentFormatter.Parse(Damaged));
        var f = Assert.Single(faults, e => e.File == "RtfDocumentFormatter.cs");
        Assert.NotNull(f.Exception);
        Assert.True(f.Line > 0);
        Assert.Contains(f.Exception.GetType().Name, f.ToString(), StringComparison.Ordinal);
    }

    // Several wired sites sit in render / caret-metrics paths, where a persistent fault would fire many
    // times a second and bury everything else.
    [Fact]
    public void Diagnostics_ReportsEachDistinctFaultOnce()
    {
        var faults = CaptureFaults(() =>
        {
            RtfDocumentFormatter.Parse(Damaged);
            RtfDocumentFormatter.Parse(Damaged);
            RtfDocumentFormatter.Parse(Damaged);
        });
        Assert.Single(faults, e => e.File == "RtfDocumentFormatter.cs");
    }

    [Fact]
    public void Diagnostics_ResetReArmsReporting()
    {
        var faults = CaptureFaults(() =>
        {
            RtfDocumentFormatter.Parse(Damaged);
            RichEditorDiagnostics.Reset();
            RtfDocumentFormatter.Parse(Damaged);
        });
        Assert.Equal(2, faults.Count(e => e.File == "RtfDocumentFormatter.cs"));
    }

    // The fallback has already run by the time the event fires; letting a handler's exception escape
    // would turn a handled fault into the crash the whole design avoids.
    [Fact]
    public void Diagnostics_SurvivesAThrowingHandler()
    {
        void Bad(object? _, RichEditorFaultEventArgs e) => throw new InvalidOperationException("boom");
        RichEditorDiagnostics.Reset();
        RichEditorDiagnostics.Fault += Bad;
        try { Assert.Empty(RtfDocumentFormatter.Parse(Damaged).Blocks); }
        finally { RichEditorDiagnostics.Fault -= Bad; RichEditorDiagnostics.Reset(); }
    }

    [Fact]
    public void Diagnostics_IsInertWithoutSubscribers()
    {
        RichEditorDiagnostics.Reset();
        Assert.Empty(RtfDocumentFormatter.Parse(Damaged).Blocks);
    }
}
