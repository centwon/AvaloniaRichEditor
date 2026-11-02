using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

// P3 of the 2026-11-01 audit: the interactive hot paths walk the document from its first block — a hover runs three
// such walks (table border, nested-table border, link), a click or drag one (GetPositionFromPoint), and every caret
// move re-measures the control (NotifyStatus -> InvalidateMeasure). Each step per block is a cache lookup, so whether
// that is ever felt depends on the document's size: measured here before anything is changed. Real Skia (this
// project), so the cached heights are real shaping. A measuring harness: skipped unless RICHEDITOR_PERF=1; also
// appended to %TEMP%\richeditor_layout_probe.txt.
public class InteractionCostProbeTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("RICHEDITOR_PERF") == "1";
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type T = typeof(RichEditor);

    private void Log(string line)
    {
        output.WriteLine(line);
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "richeditor_layout_probe.txt"), line + "\n");
    }

    private static string Html(int paras)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < paras; i++)
            sb.Append("<p>문단 ").Append(i).Append(" — <b>굵게</b> <i>기울임</i> <a href=\"https://example.com\">링크</a> ")
              .Append("Lorem ipsum dolor sit amet, consectetur adipiscing elit. 한글과 영문이 섞인 문장입니다.</p>");
        return sb.ToString();
    }

    // Microseconds per call, over enough calls to be steady (after a warm-up).
    private static double Micros(Action a, int n = 200)
    {
        for (int i = 0; i < 10; i++) a();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) a();
        return sw.Elapsed.TotalMilliseconds * 1000 / n;
    }

    private static void Press(RichEditor ed, Key key)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    [AvaloniaFact]
    public void P3_WhatTheDocumentWalksCost_ByDocumentSize()
    {
        if (!Enabled) return;
        Log($"=== [upstream] P3 interaction walks, {DateTime.Now:yyyy-MM-dd HH:mm} (µs per call; point near the END of the document)");
        var trust = T.GetField("_trustLayoutCache", NP)!;
        var posFromPoint = T.GetMethod("GetPositionFromPoint", NP, new[] { typeof(Point) })!;
        var border = T.GetMethod("TableLeftOrTopBorderAtPoint", NP, new[] { typeof(Point) })!;
        var nested = T.GetMethod("NestedTableBorderAtPoint", NP, new[] { typeof(Point) })!;
        var link = T.GetMethod("GetLinkRunAtPoint", NP, new[] { typeof(Point) })!;

        foreach (int n in new[] { 1000, 4000, 16000 })
        {
            var ed = new RichEditor { Document = HtmlDocumentFormatter.ParseHtml(Html(n)) };
            var size = new Size(800, double.PositiveInfinity);
            ed.Measure(size);
            ed.Arrange(new Rect(0, 0, 800, ed.DesiredSize.Height));
            using (var rtb = new RenderTargetBitmap(new PixelSize(800, 600))) rtb.Render(ed);
            Dispatcher.UIThread.RunJobs();

            var bottom = new Point(200, ed.DesiredSize.Height - 30);
            var args = new object[] { bottom };

            // As the hover path runs them: under a trusted cache, three walks.
            double hover = Micros(() =>
            {
                trust.SetValue(ed, true);
                try { border.Invoke(ed, args); nested.Invoke(ed, args); link.Invoke(ed, args); }
                finally { trust.SetValue(ed, false); }
            });
            // As a drag-select move runs it (trusted) and as a click does (not trusted: every paragraph re-hashed).
            double dragHit = Micros(() =>
            {
                trust.SetValue(ed, true);
                try { posFromPoint.Invoke(ed, args); }
                finally { trust.SetValue(ed, false); }
            });
            double clickHit = Micros(() => posFromPoint.Invoke(ed, args), 50);

            // A caret move near the end: the key handler, then the re-measure its NotifyStatus asks for.
            var last = (Paragraph)ed.Document!.Blocks[^2];
            foreach (var f in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
                T.GetField(f, NP)!.SetValue(ed, new TextPointer(last, 3));
            bool down = true;
            double key = Micros(() => { Press(ed, down ? Key.Down : Key.Up); down = !down; }, 100);
            double remeasure = Micros(() => { ed.InvalidateMeasure(); ed.Measure(size); }, 50);

            Log($"  {n,6} paras: hover (3 walks) {hover,8:F0} | drag hit-test {dragHit,8:F0} | click hit-test {clickHit,8:F0} " +
                $"| arrow key {key,8:F0} + re-measure {remeasure,8:F0}");
        }
    }
}
