using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Demo;

// N6-6 measurement harness (`--bench`): loads photo-heavy documents (10/20/50/100 images with text
// between them) into a real window (Skia, compositor, ScrollViewer — same host shape as NativeEditor),
// measures save/load, full layout, scroll FPS (with and without per-frame invalidation), managed
// Render() time, and typing latency, then writes bench-results.txt and exits. Numbers feed the
// roadmap's N6-5 (draw culling go/no-go) and N6-6 (soft document-size limits) decisions.
internal static class BenchHarness
{
    public static bool Enabled;
    // --bench-text: large TEXT documents (hundreds of pages) instead of the image scenarios — gate ③
    // (large-document performance: layout/measure/scroll/typing latency + managed memory).
    public static bool TextMode;
}

// RichEditor with its managed Render() pass timed — the cost draw culling would cut.
internal class BenchEditor : RichEditor
{
    public readonly List<double> RenderMs = new();

    public override void Render(DrawingContext context)
    {
        var sw = Stopwatch.StartNew();
        base.Render(context);
        sw.Stop();
        RenderMs.Add(sw.Elapsed.TotalMilliseconds);
    }
}

internal class BenchWindow : Window
{
    private readonly BenchEditor _editor = new();
    private readonly ScrollViewer _scroller;
    private readonly StringBuilder _report = new();

    public BenchWindow()
    {
        Title = "RichEditor bench (N6-6) — running, do not interact";
        Width = 1000;
        Height = 800;
        _scroller = new ScrollViewer
        {
            Padding = new Thickness(12),
            Content = _editor,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Content = _scroller;
        Opened += async (_, _) => await RunAllAsync();
    }

    private async Task RunAllAsync()
    {
        string outFile = Path.Combine(Environment.CurrentDirectory,
            BenchHarness.TextMode ? "bench-text-results.txt" : "bench-results.txt");
        try
        {
            _report.AppendLine($"RichEditor bench — {DateTime.Now:yyyy-MM-dd HH:mm} | window {Width}x{Height} | {RuntimeInformation.OSDescription}");
            _report.AppendLine($"build: {(Debugger.IsAttached ? "debugger" : "standalone")}, config: {(IsReleaseBuild() ? "Release" : "Debug")}");
            _report.AppendLine();

            if (BenchHarness.TextMode)
            {
                _report.AppendLine("mode: large TEXT documents (gate ③) — A4 page view, mixed runs + headings + a table every 50 paragraphs");
                _report.AppendLine();
                foreach (int n in new[] { 1000, 3000, 6000 })
                    await RunTextScenarioAsync(n);
            }
            else
            {
                var images = Enumerable.Range(0, 4).Select(i => MakePng(800, 600, seed: 100 + i)).ToArray();
                _report.AppendLine($"image variants: 4x 800x600 PNG, {string.Join(", ", images.Select(b => $"{b.Length / 1024}KB"))}");
                _report.AppendLine();

                foreach (int count in new[] { 10, 20, 50, 100 })
                    await RunScenarioAsync(count, images);
            }

            _report.AppendLine("done.");
        }
        catch (Exception ex)
        {
            _report.AppendLine("FAILED: " + ex);
        }

        File.WriteAllText(outFile, _report.ToString());
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private async Task RunScenarioAsync(int imageCount, byte[][] images)
    {
        _report.AppendLine($"=== {imageCount} images ===");

        var doc = BuildDoc(imageCount, images);
        _editor.Document = doc;
        _scroller.Offset = default;
        await SettleAsync();

        int paragraphs = doc.Blocks.OfType<Paragraph>().Count();
        string plain = _editor.GetPlainText();
        _report.AppendLine($"doc: {doc.Blocks.Count} blocks ({paragraphs} paragraphs, {imageCount} images), {plain.Length:N0} chars, extent {_scroller.Extent.Height:N0}px");

        // Save (ToJson) — median of 3.
        var saveMs = Time(3, () => _ = _editor.ToJson());
        string json = _editor.ToJson();
        _report.AppendLine($"save ToJson:   median {Median(saveMs):F1} ms  (json {json.Length / 1024.0 / 1024.0:F1} MB)");

        // Load (LoadJson) — includes parse + document swap; first render comes after.
        var sw = Stopwatch.StartNew();
        _editor.LoadJson(json);
        sw.Stop();
        _report.AppendLine($"load LoadJson: {sw.Elapsed.TotalMilliseconds:F1} ms");
        await SettleAsync();

        // Full layout pass (MeasureContentHeight walk) — what every edit triggers.
        _editor.InvalidateMeasure();
        sw.Restart();
        _editor.UpdateLayout();
        sw.Stop();
        _report.AppendLine($"full layout:   {sw.Elapsed.TotalMilliseconds:F1} ms");

        // Isolated Document.Clone() — the ONLY cost delta-undo would remove. Warmed up (10 throwaway
        // iterations) so JIT doesn't pollute it, then median of 20. Compare against "full layout" above
        // and the first-keystroke number below: if clone << layout, delta undo can't fix the hitch.
        for (int w = 0; w < 10; w++) _ = doc.Clone();
        var cloneMs = Time(20, () => _ = doc.Clone());
        _report.AppendLine($"Document.Clone(): median {Median(cloneMs):F2} ms, max {cloneMs.Max():F2} ms  (delta-undo would remove this)");

        // Scroll pass A: compositor only (no managed re-render unless Avalonia decides to).
        // Editor unfocused → no caret-blink invalidations polluting the numbers.
        double extent = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        _editor.RenderMs.Clear();
        var (framesA, durA) = await AnimateScrollAsync(0, extent, TimeSpan.FromSeconds(2), invalidateEachFrame: false);
        _report.AppendLine($"scroll (composited):  {framesA / durA.TotalSeconds:F0} fps  ({_editor.RenderMs.Count} managed renders)");

        // Scroll pass B: InvalidateVisual every frame — worst case (typing/caret while scrolled),
        // and the scenario draw culling targets: full managed Render + rasterize per frame.
        _editor.RenderMs.Clear();
        var (framesB, durB) = await AnimateScrollAsync(extent, 0, TimeSpan.FromSeconds(2), invalidateEachFrame: true);
        var renders = _editor.RenderMs.ToList();
        _report.AppendLine($"scroll (invalidated): {framesB / durB.TotalSeconds:F0} fps  ({renders.Count} managed renders)");
        if (renders.Count > 0)
            _report.AppendLine($"Render() time: median {Median(renders):F1} ms, p95 {Percentile(renders, 95):F1} ms, max {renders.Max():F1} ms");

        // Typing latency at document end: first keystroke pays the undo Document.Clone() (fresh typing
        // run); the rest coalesce. UpdateLayout forces the measure walk each keystroke like a real frame.
        _editor.Focus();
        _editor.FocusDocumentEnd();
        await SettleAsync();
        var keyMs = new List<double>();
        for (int i = 0; i < 30; i++)
        {
            sw.Restart();
            _editor.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "가" });
            _editor.UpdateLayout();
            sw.Stop();
            keyMs.Add(sw.Elapsed.TotalMilliseconds);
        }
        _report.AppendLine($"typing: first keystroke {keyMs[0]:F1} ms (undo clone), rest median {Median(keyMs.Skip(1).ToList()):F1} ms, max {keyMs.Skip(1).Max():F1} ms");
        _report.AppendLine();

        // Unfocus so the caret-blink timer doesn't bleed into the next scenario.
        Focus();
        await SettleAsync();
    }

    // Gate ③: a large TEXT document (hundreds of pages). Measures the costs that actually scale with
    // document size — cold full layout, the warm per-caret-move re-measure (ComputePageBreaks +
    // cache-hit layouts), scroll FPS / managed Render() time, typing latency, and managed heap.
    private async Task RunTextScenarioAsync(int paragraphs)
    {
        _report.AppendLine($"=== {paragraphs} paragraphs ===");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long memBefore = GC.GetTotalMemory(forceFullCollection: true);

        var doc = BuildTextDoc(paragraphs);
        _editor.Document = doc;
        _scroller.Offset = default;
        await SettleAsync();

        int paras = doc.Blocks.OfType<Paragraph>().Count();
        int tables = doc.Blocks.OfType<TableBlock>().Count();
        int pages = _editor.GetPrintPageCount();
        string plain = _editor.GetPlainText();
        _report.AppendLine($"doc: {doc.Blocks.Count} blocks ({paras} paras, {tables} tables), {plain.Length:N0} chars, ~{pages} pages, extent {_scroller.Extent.Height:N0}px");

        // Cold full layout — content just changed, so the cache rebuilds every paragraph.
        _editor.InvalidateMeasure();
        var sw = Stopwatch.StartNew();
        _editor.UpdateLayout();
        sw.Stop();
        _report.AppendLine($"full layout (cold):  {sw.Elapsed.TotalMilliseconds:F1} ms");

        // Warm re-measure — a caret move re-runs MeasureOverride (ComputePageBreaks + cache-hit layouts)
        // with no content change; this is the per-interaction cost a BlockBox cache (G1 P2) would cut.
        var warm = Time(10, () => { _editor.InvalidateMeasure(); _editor.UpdateLayout(); });
        _report.AppendLine($"re-measure (warm):   median {Median(warm):F1} ms, max {warm.Max():F1} ms  (per caret-move cost)");

        // Managed heap held by the document + layout caches.
        long memAfter = GC.GetTotalMemory(forceFullCollection: true);
        _report.AppendLine($"managed heap:        {(memAfter - memBefore) / 1024.0 / 1024.0:F1} MB");

        // Scroll FPS (composited vs. invalidated every frame) + managed Render() time.
        double extent = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        _editor.RenderMs.Clear();
        var (fA, dA) = await AnimateScrollAsync(0, extent, TimeSpan.FromSeconds(2), invalidateEachFrame: false);
        _report.AppendLine($"scroll (composited):  {fA / dA.TotalSeconds:F0} fps  ({_editor.RenderMs.Count} managed renders)");
        _editor.RenderMs.Clear();
        var (fB, dB) = await AnimateScrollAsync(extent, 0, TimeSpan.FromSeconds(2), invalidateEachFrame: true);
        var renders = _editor.RenderMs.ToList();
        _report.AppendLine($"scroll (invalidated): {fB / dB.TotalSeconds:F0} fps  ({renders.Count} managed renders)");
        if (renders.Count > 0)
            _report.AppendLine($"Render() time: median {Median(renders):F1} ms, p95 {Percentile(renders, 95):F1} ms, max {renders.Max():F1} ms");

        // Typing latency at document end (UpdateLayout each keystroke forces the measure walk).
        _editor.Focus();
        _editor.FocusDocumentEnd();
        await SettleAsync();
        var keyMs = new List<double>();
        for (int i = 0; i < 30; i++)
        {
            sw.Restart();
            _editor.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "가" });
            _editor.UpdateLayout();
            sw.Stop();
            keyMs.Add(sw.Elapsed.TotalMilliseconds);
        }
        _report.AppendLine($"typing: first {keyMs[0]:F1} ms, rest median {Median(keyMs.Skip(1).ToList()):F1} ms, max {keyMs.Skip(1).Max():F1} ms");
        _report.AppendLine();

        Focus();
        await SettleAsync();
    }

    // A large text document: mostly mixed-format paragraphs, an h2 heading every 25 paragraphs, and a
    // small 2×3 table every 50 — structural variety so the layout walk isn't all identical paragraphs.
    private static FlowDocument BuildTextDoc(int paragraphs)
    {
        const string ko = "대형 문서 성능 측정용 문단입니다. 수백 페이지에서 레이아웃·측정·타이핑 지연과 메모리를 실측합니다. ";
        const string en = "The quick brown fox jumps over the lazy dog, exercising shaping and line breaking across many pages. ";
        var doc = new FlowDocument();
        for (int i = 0; i < paragraphs; i++)
        {
            if (i % 50 == 49)
            {
                var tb = new TableBlock(2, 3);
                for (int r = 0; r < 2; r++)
                    for (int c = 0; c < 3; c++)
                        ((Run)tb.Cells[r][c].Inlines[0]).Text = $"r{r}c{c} {en}";
                doc.Blocks.Add(tb);
                continue;
            }
            var p = new Paragraph();
            if (i % 25 == 0)
            {
                p.HeadingLevel = 2;
                p.Inlines.Add(new Run { Text = $"Section {i / 25}" });
            }
            else
            {
                p.Inlines.Add(new Run { Text = $"[{i}] " + ko, FontSize = 14 });
                p.Inlines.Add(new Run { Text = en, FontSize = 13, FontWeight = FontWeight.Bold });
                p.Inlines.Add(new Run { Text = ko, FontSize = 12, Foreground = Brushes.Gray });
            }
            doc.Blocks.Add(p);
        }
        return doc;
    }

    // ---- helpers ----------------------------------------------------------

    private async Task SettleAsync()
    {
        _editor.UpdateLayout();
        await Task.Delay(250);
    }

    private Task<(int frames, TimeSpan duration)> AnimateScrollAsync(double from, double to, TimeSpan duration, bool invalidateEachFrame)
    {
        var tcs = new TaskCompletionSource<(int, TimeSpan)>();
        int frames = 0;
        TimeSpan? start = null, last = null;
        void Frame(TimeSpan ts)
        {
            start ??= ts;
            last = ts;
            double t = (ts - start.Value).TotalMilliseconds / duration.TotalMilliseconds;
            if (t >= 1)
            {
                _scroller.Offset = new Vector(0, to);
                tcs.TrySetResult((frames, ts - start.Value));
                return;
            }
            frames++;
            _scroller.Offset = new Vector(0, from + (to - from) * t);
            if (invalidateEachFrame) _editor.InvalidateVisual();
            RequestAnimationFrame(Frame);
        }
        RequestAnimationFrame(Frame);
        return tcs.Task;
    }

    private static List<double> Time(int n, Action action)
    {
        var list = new List<double>(n);
        var sw = new Stopwatch();
        for (int i = 0; i < n; i++)
        {
            sw.Restart();
            action();
            sw.Stop();
            list.Add(sw.Elapsed.TotalMilliseconds);
        }
        return list;
    }

    private static double Median(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return s.Count == 0 ? 0 : s[s.Count / 2];
    }

    private static double Percentile(List<double> v, int p)
    {
        var s = v.OrderBy(x => x).ToList();
        return s.Count == 0 ? 0 : s[Math.Min(s.Count - 1, s.Count * p / 100)];
    }

    private static bool IsReleaseBuild()
    {
#if DEBUG
        return false;
#else
        return true;
#endif
    }

    // A "photo page": a few mixed-format paragraphs followed by one image block, repeated.
    private static FlowDocument BuildDoc(int imageCount, byte[][] images)
    {
        const string ko = "벤치마크 문단입니다. 이미지가 많은 문서에서 타이핑 지연과 스크롤 프레임, 저장 속도를 실측합니다. ";
        const string en = "The quick brown fox jumps over the lazy dog while measuring frame times and save costs. ";
        var doc = new FlowDocument();
        for (int i = 0; i < imageCount; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                var p = new Paragraph();
                p.Inlines.Add(new Run { Text = $"[{i + 1}-{j + 1}] " + ko + en, FontSize = 14 });
                p.Inlines.Add(new Run { Text = ko, FontSize = 14, FontWeight = FontWeight.Bold });
                p.Inlines.Add(new Run { Text = en, FontSize = 12, Foreground = Brushes.Gray });
                doc.Blocks.Add(p);
            }
            var ib = new ImageBlock { Width = 600, Height = 450 };
            ib.SetImageData(images[i % images.Length], "image/png");
            doc.Blocks.Add(ib);
        }
        return doc;
    }

    // Photo-ish PNG: smooth gradient (compresses) + noise speckle (keeps size honest, ~photo scale).
    private static byte[] MakePng(int w, int h, int seed)
    {
        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Premul);
        var rnd = new Random(seed);
        using (var fb = wb.Lock())
        {
            var row = new int[w];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int r = (x * 255 / w + seed * 37) & 0xFF;
                    int g = (y * 255 / h + seed * 73) & 0xFF;
                    int b = ((x + y) * 255 / (w + h)) & 0xFF;
                    int noise = rnd.Next(0, 24); // mild speckle so PNG stays photo-sized
                    r = Math.Min(255, r + noise); g = Math.Min(255, g + noise); b = Math.Min(255, b + noise);
                    row[x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                }
                Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, w);
            }
        }
        using var ms = new MemoryStream();
        wb.Save(ms);
        return ms.ToArray();
    }
}
