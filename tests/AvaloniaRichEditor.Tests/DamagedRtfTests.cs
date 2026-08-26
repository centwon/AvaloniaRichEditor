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
// The fixture used to be an oversized control-word parameter, which aborted the parse with an
// OverflowException. That is no longer damage: a parameter too wide for int is now read as absent, so
// one bad number in an otherwise fine file no longer costs the whole document (Word tolerates it too).
// TRUNCATION — unclosed groups — is what the fixture is now, and it is the damage files actually suffer.
public class DamagedRtfTests
{
    private const string Damaged = @"{\rtf1\ansi {\*\broken";
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

    // An oversized control-word parameter is TOLERATED now, not fatal: the keyword reads as
    // parameterless and the rest of the document still arrives. It used to abort the parse outright,
    // which cost a whole readable file for one bad number.
    [Fact]
    public void Parse_OversizedParameter_KeepsTheRestOfTheDocument()
        => Assert.Contains("x", AllText(RtfDocumentFormatter.Parse(@"{\rtf1\ansi\fs99999999999999999999 x\par}")),
                           StringComparison.Ordinal);

    [Fact]
    public void TryParse_OversizedParameter_IsNotDamage()
        => Assert.True(RtfDocumentFormatter.TryParse(@"{\rtf1\ansi\fs99999999999999999999 x\par}", out _, out _));

    // TRUNCATION is the damage that actually happens to files — a half-copied document, a download cut
    // short — and it does NOT abort the parse: the reader runs out of input and finalizes what it has,
    // which is indistinguishable from cleanly reading a SHORTER document. TryParse reported success and
    // LoadRtf replaced the open document with the remains.
    //
    // RTF is brace-balanced, so groups still open at the end are the giveaway.
    [Theory]
    [InlineData(@"{\rtf1\ansi {\*\broken")]              // truncated inside a nested group
    [InlineData(@"{\rtf1\ansi hello there")]             // truncated after readable text
    [InlineData(@"{\rtf1\ansi\trowd\cellx1000 a\cell")]  // truncated mid-table
    [InlineData(@"{\rtf1\ansi\b bold text\par")]         // no closing brace at all
    public void TryParse_ReportsTruncatedInput(string truncated)
    {
        Assert.False(RtfDocumentFormatter.TryParse(truncated, out var doc, out string? error));
        Assert.NotNull(error);
        Assert.Contains("truncated", error, StringComparison.Ordinal);
        Assert.Empty(doc.Blocks);
    }

    // Trailing junk braces are not truncation and stay tolerated, as before.
    [Fact]
    public void TryParse_ToleratesExtraClosingBraces()
        => Assert.True(RtfDocumentFormatter.TryParse(@"{\rtf1\ansi hi\par}}}}", out _, out _));

    // Parse stays lenient on truncation on purpose: for a clipboard fragment, whatever was readable
    // beats nothing. Only TryParse — which guards an open document — is strict.
    [Fact]
    public void Parse_StaysLenientOnTruncatedInput()
        => Assert.Contains("hello there", AllText(RtfDocumentFormatter.Parse(@"{\rtf1\ansi hello there")),
                           StringComparison.Ordinal);

    [AvaloniaFact]
    public void LoadRtf_TruncatedInput_KeepsTheOpenDocument()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>precious</p>");
        var before = ed.Document;

        ed.LoadRtf(@"{\rtf1\ansi {\*\broken");

        Assert.Same(before, ed.Document);
        Assert.Contains("precious", AllText(ed.Document!), StringComparison.Ordinal);
    }

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

    // These four are about the diagnostics CHANNEL, not about RTF: report-once, Reset re-arms, a
    // throwing handler is survivable, no subscriber costs nothing. They need any live swallow site, and
    // the RTF parser no longer has one (it tolerates every fault it used to abort on). A failed image
    // decode is the stable stand-in: these are plain [Fact]s, so there is no Avalonia platform and
    // `new Bitmap` genuinely throws — see ImageRawBytesTests, which relies on the same thing.
    private static byte[] Undecodable() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03, 0x04 };

    // A fresh block each time: ImageBlock latches _decodeFailed so one instance only ever faults once.
    private static void FailADecode()
    {
        var ib = new ImageBlock();
        ib.SetImageData(Undecodable(), "image/jpeg");
        Assert.Null(ib.Image);
    }

    [Fact]
    public void Diagnostics_ReportsTheSwallowedFault()
    {
        var faults = CaptureFaults(FailADecode);
        var f = Assert.Single(faults, e => e.File == "ImageBlock.cs");
        Assert.NotNull(f.Exception);
        Assert.True(f.Line > 0);
        Assert.Contains(f.Exception.GetType().Name, f.ToString(), StringComparison.Ordinal);
    }

    // Several wired sites sit in render / caret-metrics paths, where a persistent fault would fire many
    // times a second and bury everything else.
    [Fact]
    public void Diagnostics_ReportsEachDistinctFaultOnce()
    {
        var faults = CaptureFaults(() => { FailADecode(); FailADecode(); FailADecode(); });
        Assert.Single(faults, e => e.File == "ImageBlock.cs");
    }

    [Fact]
    public void Diagnostics_ResetReArmsReporting()
    {
        var faults = CaptureFaults(() =>
        {
            FailADecode();
            RichEditorDiagnostics.Reset();
            FailADecode();
        });
        Assert.Equal(2, faults.Count(e => e.File == "ImageBlock.cs"));
    }

    // The fallback has already run by the time the event fires; letting a handler's exception escape
    // would turn a handled fault into the crash the whole design avoids.
    [Fact]
    public void Diagnostics_SurvivesAThrowingHandler()
    {
        void Bad(object? _, RichEditorFaultEventArgs e) => throw new InvalidOperationException("boom");
        RichEditorDiagnostics.Reset();
        RichEditorDiagnostics.Fault += Bad;
        try { FailADecode(); }
        finally { RichEditorDiagnostics.Fault -= Bad; RichEditorDiagnostics.Reset(); }
    }

    [Fact]
    public void Diagnostics_IsInertWithoutSubscribers()
    {
        RichEditorDiagnostics.Reset();
        FailADecode();
    }
}
