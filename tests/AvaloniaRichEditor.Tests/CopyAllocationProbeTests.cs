using System;
using System.Reflection;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// What copying a picture allocates, stage by stage — the WinUI peer's probe (2026-09-16), where one 10 MB picture
// allocated 352 MB. A measurement harness: skipped unless RICHEDITOR_PERF=1 (the same switch as the peer's).
//
// Copy here puts plain text and CF_HTML on the clipboard: the selection's HTML (the picture as base64), wrapped in
// the CF_HTML envelope, encoded to UTF-8 bytes. RTF is not part of copy, but ToRtf/export writes pictures as hex, so
// it is measured too. The OS clipboard itself is not called — the probe stops at what would be handed to it.
// Also appended to %TEMP%\richeditor_copy_probe.txt, since runner output capture differs by logger. Run:
//   $env:RICHEDITOR_PERF=1; dotnet test tests/AvaloniaRichEditor.Tests -c Release --filter CopyAllocationProbe
public class CopyAllocationProbeTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("RICHEDITOR_PERF") == "1";
    private const int Mb = 1024 * 1024;

    private void Log(string line)
    {
        output.WriteLine(line);
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "richeditor_copy_probe.txt"), line + "\n");
    }

    private static FlowDocument DocWithPicture(int bytes)
    {
        var raw = new byte[bytes];
        new Random(7).NextBytes(raw);
        raw[0] = 0xFF; raw[1] = 0xD8; raw[2] = 0xFF;
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "before" } } });
        var img = new ImageBlock { Width = 400, Height = 300 };
        img.SetImageData(raw, "image/jpeg");
        doc.Blocks.Add(img);
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "after" } } });
        return doc;
    }

    private static long Measure(Action body)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        body();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [AvaloniaFact]
    public void CopyingAPicture_AllocationsByStage()
    {
        if (!Enabled) return;
        const int size = 10 * Mb;
        var ed = new RichEditor();
        var doc = DocWithPicture(size);
        var buildHtml = typeof(RichEditor).GetMethod("BuildSelectionHtml", BindingFlags.NonPublic | BindingFlags.Instance)!;

        string? html = null, rtf = null;
        byte[]? payload = null;
        long aHtml = Measure(() => html = (string)buildHtml.Invoke(ed, [doc])!);
        long aCf = 0; // the copy path no longer makes the envelope a string (BuildCfHtmlBytes)
        long aUtf8 = Measure(() => payload = RichEditor.BuildCfHtmlBytes(html!));
        long aRtf = Measure(() => rtf = RtfDocumentFormatter.Write(doc));

        double Mib(long b) => b / (double)Mb;
        Log($"[upstream] picture: {Mib(size):F1} MB");
        Log($"  selection HTML  alloc {Mib(aHtml),7:F1} MB  result {Mib(html!.Length * 2L),6:F1} MB  ({aHtml / (double)size:F1}x)");
        Log($"  CF_HTML bytes   alloc {Mib(aUtf8),7:F1} MB  result {Mib(payload!.Length),6:F1} MB  ({aUtf8 / (double)size:F1}x)");
        long copy = aHtml + aCf + aUtf8;
        Log($"  COPY total alloc {Mib(copy):F1} MB ({copy / (double)size:F1}x)");
        Log($"  RTF export      alloc {Mib(aRtf),7:F1} MB  result {Mib(rtf!.Length * 2L),6:F1} MB  ({aRtf / (double)size:F1}x)");
    }
}
