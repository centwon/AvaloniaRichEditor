using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

// What the per-paragraph TextLayout cache holds — the WinUI peer's question (2026-09-28). There the render
// path caches only what it DRAWS and measurement builds throwaway layouts; here Measure, Render and hit-testing
// share one cache, so it holds every paragraph of the document, pruned only past 10,000 entries — and a
// Dictionary<Paragraph, …> keeps a paragraph an edit removed until then.
// Real Skia (this project), because the no-op backend's shaping would say nothing about a TextLayout's size.
// A measuring harness: skipped unless RICHEDITOR_PERF=1. Also appended to %TEMP%\richeditor_layout_probe.txt.
//   $env:RICHEDITOR_PERF=1; dotnet test tests/AvaloniaRichEditor.Tests.Render -c Release --filter LayoutCacheProbe
public class LayoutCacheProbeTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("RICHEDITOR_PERF") == "1";
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const double Mb = 1024 * 1024;

    private void Log(string line)
    {
        output.WriteLine(line);
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "richeditor_layout_probe.txt"), line + "\n");
    }

    // The same paragraph shape as the peer's MemBaseline harness: mixed Hangul/Latin, inline formatting, wraps.
    private static string BigHtml(int paras, int from = 0)
    {
        var sb = new StringBuilder();
        for (int i = from; i < from + paras; i++)
            sb.Append("<p>문단 ").Append(i)
              .Append(" — <b>굵게</b> <i>기울임</i> <u>밑줄</u> <s>취소선</s> ")
              .Append("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod ")
              .Append("tempor incididunt ut labore et dolore magna aliqua. ")
              .Append("한글과 영문이 충분히 섞인 긴 문장으로 컨트롤 폭에서 자동 줄바꿈을 유발합니다.</p>");
        return sb.ToString();
    }

    // Measure + arrange + one real render of an 800x600 viewport, then the dispatcher's queue.
    private static void Settle(RichEditor ed)
    {
        ed.Measure(new Size(800, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, 800, Math.Max(600, ed.DesiredSize.Height)));
        using (var rtb = new RenderTargetBitmap(new PixelSize(800, 600))) rtb.Render(ed);
        Dispatcher.UIThread.RunJobs();
    }

    private static long Managed()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        return GC.GetTotalMemory(true);
    }

    private static IDictionary Cache(RichEditor ed) => (IDictionary)typeof(RichEditor).GetField("_layoutCache", NP)!.GetValue(ed)!;
    private static IDictionary TableCache(RichEditor ed) => (IDictionary)typeof(RichEditor).GetField("_tableLayoutCache", NP)!.GetValue(ed)!;
    private static void Invoke(RichEditor ed, string name) => typeof(RichEditor).GetMethod(name, NP, Type.EmptyTypes)!.Invoke(ed, null);

    // Taken in their own frames: a temporary in the test method's frame kept the target reachable (the peer's
    // LayoutCostTests met the same trap), and "still alive" would then measure the test, not the editor.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference WeakBlock(RichEditor ed, int index) => new(ed.Document!.Blocks[index]);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference WeakPictureBytes(RichEditor ed, int index)
        => new(((InlineImage)((Paragraph)ed.Document!.Blocks[index]).Inlines[1]).RawBytes);

    private static long Priv() { var p = Process.GetCurrentProcess(); p.Refresh(); return p.PrivateMemorySize64; }

    [AvaloniaFact]
    public void A_WhatTheCacheCosts_ByDocumentSize()
    {
        if (!Enabled) return;
        Log($"=== [upstream] layout cache, {DateTime.Now:yyyy-MM-dd HH:mm}");
        foreach (int n in new[] { 200, 2000, 8000 })
        {
            long empty = Managed();
            var doc = HtmlDocumentFormatter.ParseHtml(BigHtml(n));
            long model = Managed() - empty;
            var ed = new RichEditor { Document = doc };
            Settle(ed);
            long withCache = Managed();
            long privWith = Priv();
            int count = Cache(ed).Count;
            Cache(ed).Clear();
            TableCache(ed).Clear();
            long without = Managed();
            long cache = withCache - without;
            Settle(ed); // refill, then time the prune walk with nothing to drop — what an edit would pay
            var prune = typeof(RichEditor).GetMethod("PruneLayoutCaches", NP)!;
            prune.Invoke(ed, null);
            var sw = Stopwatch.StartNew();
            for (int k = 0; k < 20; k++) prune.Invoke(ed, null);
            double pruneMs = sw.Elapsed.TotalMilliseconds / 20;
            Log($"  {n,5} paras: model {model / Mb,6:F1} MB | cache holds {count,5} layouts = {cache / Mb,6:F1} MB " +
                $"({cache / (double)n / 1024:F1} KB/para, {cache / (double)Math.Max(1, model):F1}x the model) | Priv {privWith / Mb:F0} MB | prune walk {pruneMs:F2} ms");
            GC.KeepAlive(ed);
        }
    }

    // What an app sees: the editor in a ScrollViewer in an 800x600 window, which is what render culling keys on
    // (A renders with no scroller, so every paragraph counts as visible). Loaded at the top, then paged to the end.
    [AvaloniaFact]
    public void D_InAWindow_LoadedThenScrolledToTheEnd()
    {
        if (!Enabled) return;
        // This app has no theme (the pixel tests want none), and an untemplated ScrollViewer has no viewport,
        // so culling would see nothing. Only for this probe, and taken out again.
        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        try { RunWindowScenario(); }
        finally { Application.Current!.Styles.Remove(theme); }
    }

    private void RunWindowScenario()
    {
        foreach (int n in new[] { 2000, 8000 })
        {
            long empty = Managed();
            var ed = new RichEditor { Document = HtmlDocumentFormatter.ParseHtml(BigHtml(n)) };
            var scroller = new Avalonia.Controls.ScrollViewer { Content = ed };
            var window = new Avalonia.Controls.Window { Width = 800, Height = 600, Content = scroller };
            window.Show();
            void Frame()
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                using var rtb = new RenderTargetBitmap(new PixelSize(800, 600));
                rtb.Render(window);
            }
            var sw = Stopwatch.StartNew();
            Frame();
            double loadMs = sw.Elapsed.TotalMilliseconds;
            long loaded = Managed() - empty;
            int atTop = Cache(ed).Count;
            Log($"    (scroller extent {scroller.Extent.Height:F0}, viewport {scroller.Viewport.Height:F0}, editor {ed.Bounds.Height:F0})");

            sw.Restart();
            int pages = 0;
            while (scroller.Offset.Y + scroller.Viewport.Height < scroller.Extent.Height - 1 && pages < 100_000)
            {
                scroller.Offset = new Vector(0, scroller.Offset.Y + scroller.Viewport.Height * 0.9);
                Frame();
                pages++;
            }
            double scrollMs = sw.Elapsed.TotalMilliseconds;
            long scrolled = Managed() - empty;
            Log($"  window {n,5} paras: load+first frame {loadMs,6:F0} ms, managed {loaded / Mb,6:F1} MB, cache {atTop,5} | " +
                $"scrolled {pages} pages in {scrollMs:F0} ms ({scrollMs / Math.Max(1, pages):F1} ms/page), managed {scrolled / Mb,6:F1} MB, cache {Cache(ed).Count,5}");
            window.Content = null; // never Close(): the last headless top-level takes the dispatcher with it
        }
    }

    [AvaloniaFact]
    public void B_ParagraphsAnEditRemoved_StayInTheCache()
    {
        if (!Enabled) return;
        const int n = 2000, cycles = 6;
        var ed = new RichEditor { Document = HtmlDocumentFormatter.ParseHtml(BigHtml(n)) };
        Settle(ed);
        // Blocks[1]: select all + type keeps the FIRST paragraph (it takes the "x"), so it is the second that leaves.
        var firstWeak = WeakBlock(ed, 1);
        Log($"  graveyard: {n} paras, each cycle = select all + type 'x' (same document instance) + insert {n} new paras");
        for (int c = 0; c < cycles; c++)
        {
            Invoke(ed, "SelectAll");
            ed.InsertText("x");
            Settle(ed);
            ed.InsertHtml(BigHtml(n, (c + 1) * n));
            Settle(ed);
            ed.RunScheduledLayoutCachePrune(); // the pause after the edit: what the 1 s timer does
            long m = Managed();
            int live = ed.Document!.Blocks.Count;
            Log($"    cycle {c}: blocks in doc {live,5} | cache {Cache(ed).Count,5} | managed {m / Mb,6:F1} MB | removed paragraph alive: {firstWeak.IsAlive}");
        }
        long before = Managed();
        typeof(RichEditor).GetMethod("PruneLayoutCaches", NP)!.Invoke(ed, null);
        long after = Managed();
        Log($"    PruneLayoutCaches now: cache {Cache(ed).Count} | frees {(before - after) / Mb:F1} MB | removed paragraph alive: {firstWeak.IsAlive}");
    }

    [AvaloniaFact]
    public void C_PicturesAnEditRemoved_AreHeldThroughTheCachedLayout()
    {
        if (!Enabled) return;
        const int pictures = 20, size = 2 * 1024 * 1024;
        var doc = new FlowDocument();
        var rnd = new Random(3);
        for (int i = 0; i < pictures; i++)
        {
            var raw = new byte[size];
            rnd.NextBytes(raw);
            var img = new InlineImage { Width = 40, Height = 30 };
            img.SetImageData(raw, "image/jpeg"); // undecodable on purpose: the retention is of the BYTES
            doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = $"picture {i} " }, img } });
        }
        var ed = new RichEditor { Document = doc };
        Settle(ed);
        var weakBytes = WeakPictureBytes(ed, 10);
        long loaded = Managed();

        Invoke(ed, "SelectAll");
        ed.InsertText("x");
        Settle(ed);
        ed.RunScheduledLayoutCachePrune(); // the pause after the edit
        typeof(RichEditor).GetField("_undoManager", NP)!.SetValue(ed, Activator.CreateInstance(
            typeof(RichEditor).GetField("_undoManager", NP)!.FieldType)); // history is budgeted separately; take it out of the picture
        long deleted = Managed();
        bool aliveAfterEdit = weakBytes.IsAlive;

        Cache(ed).Clear();
        long cleared = Managed();
        Log($"  pictures: {pictures} x {size / Mb:F0} MB inline, removed by select all + type (undo history dropped)");
        Log($"    loaded {loaded / Mb,6:F1} MB | after the edit {deleted / Mb,6:F1} MB (bytes alive: {aliveAfterEdit}) | cache cleared {cleared / Mb,6:F1} MB (bytes alive: {weakBytes.IsAlive})");
    }
}
