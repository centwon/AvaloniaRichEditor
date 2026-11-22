using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

/// <summary>SavePdf's vector path (2026-09-12).
/// <para>SavePdf used to write one RGB image per page: a PDF with no text in it. Pages are now drawn by the
/// editor's renderer into Skia's PDF backend through Avalonia's Skia bridge — measured by a probe first:
/// text arrives as text (Type0 fonts, a ToUnicode map, no images). Skia embeds each font WHOLE, though (its
/// native build has no subsetter): one page with a line of Korean was 7.7 MB, 7.45 MB of it Malgun Gothic.
/// So the fonts are then cut to the glyphs used with HarfBuzz's hb_subset (PdfFontSubsetter), keeping glyph
/// ids.</para>
/// <para>This project renders with real Skia, so the vector path runs here; the main project's headless
/// drawing takes SavePdf's raster fallback.</para></summary>
public class VectorPdfTests
{
    private static readonly FontFamily Inter = new("avares://Avalonia.Fonts.Inter/Assets#Inter");
    private const string Html = "<p>Editor text <b>bold</b> 한글 문장</p><table border=1><tr><td>cell A</td><td>셀 B</td></tr></table>";

    private static byte[] SavePdf(string html)
    {
        var ed = new RichEditor { DefaultFontFamily = Inter };
        ed.LoadHtml(html);
        using var ms = new MemoryStream();
        ed.SavePdf(ms);
        return ms.ToArray();
    }

    // ---- a small reader for what Skia (and the subsetter) write ------------------------------------

    private static string Latin(byte[] b) => Encoding.Latin1.GetString(b);

    // Object number → (dictionary, raw stream bytes or null), located through the xref table — which also
    // proves the xref is right: every offset must land on "N 0 obj".
    private static Dictionary<int, (string Dict, byte[]? Data)> Objects(byte[] pdf)
    {
        string t = Latin(pdf);
        int startxref = int.Parse(Regex.Match(t, @"startxref\s+(\d+)\s+%%EOF\s*$").Groups[1].Value, CultureInfo.InvariantCulture);
        var head = Regex.Match(t.Substring(startxref), @"^xref\s+0\s+(\d+)\s+");
        Assert.True(head.Success, "no classic xref at startxref");
        int count = int.Parse(head.Groups[1].Value, CultureInfo.InvariantCulture);
        var result = new Dictionary<int, (string, byte[]?)>();
        for (int n = 1; n < count; n++)
        {
            int off = int.Parse(t.AsSpan(startxref + head.Length + n * 20, 10), CultureInfo.InvariantCulture);
            var m = Regex.Match(t.Substring(off, 40), @"^(\d+) 0 obj\s*");
            Assert.True(m.Success && int.Parse(m.Groups[1].Value) == n, $"xref entry {n} points at \"{t.Substring(off, 12)}\"");
            int body = off + m.Length;
            int endobj = t.IndexOf("endobj", body, StringComparison.Ordinal);
            int stream = t.IndexOf("stream", body, StringComparison.Ordinal);
            if (stream >= 0 && stream < endobj)
            {
                string dict = t.Substring(body, stream - body);
                int len = int.Parse(Regex.Match(dict, @"/Length (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
                int data = stream + 6 + (t[stream + 6] == '\r' ? 2 : 1);
                result[n] = (dict, pdf.AsSpan(data, len).ToArray());
            }
            else result[n] = (t.Substring(body, endobj - body), null);
        }
        return result;
    }

    private static byte[] Inflate(byte[] data)
    {
        using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        z.CopyTo(outMs);
        return outMs.ToArray();
    }

    // Every character a ToUnicode map can give back: what a viewer's search and copy see.
    private static HashSet<char> MappedText(Dictionary<int, (string Dict, byte[]? Data)> objs)
    {
        var chars = new HashSet<char>();
        foreach (var (dict, data) in objs.Values)
        {
            if (data == null || !dict.Contains("FlateDecode")) continue;
            string s;
            try { s = Latin(Inflate(data)); } catch (InvalidDataException) { continue; }
            if (!s.Contains("begincmap")) continue;
            // bfchar and bfrange blocks read apart: one pattern for both misreads two <src> <dst> pairs in a
            // row as a range (it dropped 한 that way).
            foreach (Match block in Regex.Matches(s, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(block.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                    chars.Add((char)int.Parse(m.Groups[2].Value.Substring(0, 4), NumberStyles.HexNumber));
            foreach (Match block in Regex.Matches(s, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(block.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                {
                    int lo = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber), hi = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
                    int dst = int.Parse(m.Groups[3].Value.Substring(0, 4), NumberStyles.HexNumber);
                    for (int k = 0; k <= hi - lo; k++) chars.Add((char)(dst + k));
                }
        }
        return chars;
    }

    private static ushort U16(byte[] b, int o) => (ushort)(b[o] << 8 | b[o + 1]);
    private static uint U32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

    // A TrueType program's glyph count (maxp) and how many glyphs carry an outline (non-empty loca span).
    private static (int glyphs, int outlined) GlyphStats(byte[] ttf)
    {
        var tables = new Dictionary<string, int>();
        for (int i = 0, n = U16(ttf, 4); i < n; i++)
            tables[Encoding.ASCII.GetString(ttf, 12 + i * 16, 4)] = (int)U32(ttf, 12 + i * 16 + 8);
        int numGlyphs = U16(ttf, tables["maxp"] + 4);
        bool longLoca = U16(ttf, tables["head"] + 50) == 1;
        int loca = tables["loca"], outlined = 0;
        for (int g = 0; g < numGlyphs; g++)
        {
            long a = longLoca ? U32(ttf, loca + g * 4) : U16(ttf, loca + g * 2) * 2L;
            long b = longLoca ? U32(ttf, loca + (g + 1) * 4) : U16(ttf, loca + (g + 1) * 2) * 2L;
            if (b > a) outlined++;
        }
        return (numGlyphs, outlined);
    }

    // ---- tests ------------------------------------------------------------------------------------

    // The page is text, not a picture: no image objects, embedded TrueType fonts, and every character of
    // the document — Latin, bold, Hangul, table cells — can be read back through ToUnicode (search, copy).
    [AvaloniaFact]
    public void SavePdf_WritesText_ThatAViewerCanSearch()
    {
        var pdf = SavePdf(Html);
        string raw = Latin(pdf);
        Assert.DoesNotContain("/Subtype /Image", raw);
        Assert.Contains("/FontFile2", raw);

        var mapped = MappedText(Objects(pdf));
        var missing = "Editortextbold한글문장cellA셀B".Where(c => !mapped.Contains(c)).ToList();
        Assert.True(missing.Count == 0, "not searchable: " + string.Concat(missing));
    }

    // The fonts are cut to the glyphs used — by id, so the content's codes stay valid: each program keeps
    // its full glyph count (RETAIN_GIDS) while only a handful of glyphs still have outlines. Before, the
    // same document was 7.7 MB with Malgun Gothic embedded whole.
    [AvaloniaFact]
    public void SavePdf_EmbedsOnlyTheGlyphsItUses()
    {
        var pdf = SavePdf(Html);
        TestContext.Current.TestOutputHelper?.WriteLine($"vector PDF: {pdf.Length:N0} bytes");
        Assert.True(pdf.Length < 100_000, $"PDF is {pdf.Length:N0} bytes (measured 17,904 after subsetting, 7.7 MB before)");

        var objs = Objects(pdf);
        var programs = objs.Where(kv => objs.Values.Any(o => Regex.IsMatch(o.Dict, $@"/FontFile2 {kv.Key} 0 R"))).ToList();
        Assert.NotEmpty(programs);
        foreach (var (number, (dict, data)) in programs)
        {
            var ttf = Inflate(data!);
            Assert.Equal(ttf.Length, int.Parse(Regex.Match(dict, @"/Length1 (\d+)").Groups[1].Value, CultureInfo.InvariantCulture));
            var (glyphs, outlined) = GlyphStats(ttf);
            TestContext.Current.TestOutputHelper?.WriteLine($"font {number}: {outlined} outlined of {glyphs} glyphs");
            Assert.True(outlined > 0, $"font {number}: no outlines left");
            Assert.True(glyphs > outlined * 10, $"font {number}: {outlined} outlined of {glyphs} glyphs — not subset");
        }
    }

    // ---- the page fits the paper ----------------------------------------------------------------------

    // A PDF page is measured in points (1/72 in) and the editor draws in DIPs (1/96 in), so the page must be
    // drawn at 72/96. Seen in a saved PDF before this test: the scale had been left to the Skia bridge's dpi
    // argument, which does not scale the drawing, and every line ran a third past the right edge. The same
    // per-page routine SavePdf uses draws onto a raster surface the size of an A4 page in points; the ink
    // must start at the left margin and end inside the right one — and not be drawn twice as small either.
    // The editor's page geometry is internal to the library: read it whether it is a property, a field or
    // a constant.
    private static double Geometry(RichEditor ed, string name)
    {
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var t = typeof(RichEditor);
        object? v = t.GetProperty(name, any)?.GetValue(ed) ?? t.GetField(name, any)?.GetValue(ed);
        return Convert.ToDouble(v ?? throw new MissingMemberException(nameof(RichEditor), name), CultureInfo.InvariantCulture);
    }

    [AvaloniaFact]
    public void TheVectorPage_FitsThePaper()
    {
        var ed = new RichEditor { DefaultFontFamily = Inter, PageSize = RichEditorPageSize.A4 };
        ed.LoadHtml("<p>" + string.Concat(Enumerable.Repeat("Justified text spreads to both margins over a couple of lines. ", 6)) + "</p>");
        var breaks = (IReadOnlyList<double>)typeof(RichEditor)
            .GetMethod("ComputePageBreaks", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ed, new object[] { Geometry(ed, "PaperContentWidth"), Geometry(ed, "PaperContentHeight") })!;

        const int W = 595, H = 842; // A4 in points
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(W, H, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul));
        surface.Canvas.Clear(SkiaSharp.SKColors.White);
        typeof(RichEditor).GetMethod("DrawVectorPage", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ed, new object[] { surface.Canvas, 0, breaks });

        using var image = surface.Snapshot();
        using var pixmap = image.PeekPixels();
        var vector = InkColumns(pixmap.GetPixelSpan().ToArray(), W, H);

        // The reference: the same page rasterized by the same editor at 72 dpi — a bitmap the size of the
        // paper in points. The vector page must put its ink where that one does (the left edge is the page
        // margin plus the editor's content inset, so a formula would be the wrong oracle).
        var bmp = ed.RenderPrintPage(0, dpi: 72);
        int rw = bmp.PixelSize.Width, rh = bmp.PixelSize.Height;
        var buf = new byte[rw * rh * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { bmp.CopyPixels(new Avalonia.PixelRect(0, 0, rw, rh), handle.AddrOfPinnedObject(), buf.Length, rw * 4); }
        finally { handle.Free(); }
        var raster = InkColumns(buf, rw, rh);

        double margin = Geometry(ed, "PagePadRight") * 72 / 96;
        Assert.True(raster.Right > 0 && vector.Right > 0, "nothing drawn");
        Assert.True(vector.Right < W - margin + 2, $"ink runs to x={vector.Right}, past the right margin at {W - margin:0}");
        // A share of the column rather than a pixel count: the content width is 794 - 2 x 56.7 DIP once
        // margins are millimetres, and the two paths land that fraction differently — the vector page
        // draws at point scale, the raster one rasterises at 72 dpi — by an amount that also depends on
        // the platform's glyph rasterisation (measured: 4 px on Windows, 6 on Linux, which a fixed ±5
        // turned into a red CI). The claim held here is that both put the text in the same place on the
        // paper; the margin check above is what catches ink running off it.
        double slack = Math.Max(4, 0.02 * (W - 2 * margin));
        Assert.True(Math.Abs(vector.Left - raster.Left) <= slack && Math.Abs(vector.Right - raster.Right) <= slack,
            $"vector ink spans {vector.Left}..{vector.Right}, the rasterized page {raster.Left}..{raster.Right} (slack {slack:0.#})");
    }

    // The leftmost and rightmost columns holding dark ink.
    private static (int Left, int Right) InkColumns(byte[] bgra, int w, int h)
    {
        int left = w, right = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (bgra[i] < 128 && bgra[i + 1] < 128 && bgra[i + 2] < 128) { left = Math.Min(left, x); right = Math.Max(right, x); }
            }
        return (left, right);
    }

    // ---- the property that matters: nothing shown lost its outline ----------------------------------

    private static int Outline(byte[] ttf, int glyph)
    {
        var tables = new Dictionary<string, int>();
        for (int i = 0, n = U16(ttf, 4); i < n; i++)
            tables[Encoding.ASCII.GetString(ttf, 12 + i * 16, 4)] = (int)U32(ttf, 12 + i * 16 + 8);
        bool longLoca = U16(ttf, tables["head"] + 50) == 1;
        int loca = tables["loca"];
        long a = longLoca ? U32(ttf, loca + glyph * 4) : U16(ttf, loca + glyph * 2) * 2L;
        long b = longLoca ? U32(ttf, loca + (glyph + 1) * 4) : U16(ttf, loca + (glyph + 1) * 2) * 2L;
        return (int)(b - a);
    }

    private static int? Ref(string dict, string key) =>
        Regex.Match(dict, key + @"\s*\[?\s*(\d+) 0 R") is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    // BaseFont → its TrueType program, following Type0 → CIDFont → FontDescriptor → FontFile2.
    private static Dictionary<string, byte[]> FontPrograms(Dictionary<int, (string Dict, byte[]? Data)> objs)
    {
        var result = new Dictionary<string, byte[]>();
        foreach (var (dict, _) in objs.Values)
        {
            if (!dict.Contains("/Subtype /Type0")) continue;
            string name = Regex.Match(dict, @"/BaseFont /(\S+)").Groups[1].Value;
            if (Ref(dict, "/DescendantFonts") is int cid && Ref(objs[cid].Dict, "/FontDescriptor") is int fd
                && Ref(objs[fd].Dict, "/FontFile2") is int ff)
                result[name] = Inflate(objs[ff].Data!);
        }
        return result;
    }

    // BaseFont → the glyph ids the content streams show with it (Tf selects the font, hex strings carry
    // 2-byte glyph ids).
    private static Dictionary<string, HashSet<int>> ShownGlyphs(Dictionary<int, (string Dict, byte[]? Data)> objs)
    {
        var baseFont = new Dictionary<string, string>();
        foreach (var (dict, _) in objs.Values)
            foreach (Match res in Regex.Matches(dict, @"/Font\s*<<([^<>]*)>>"))
                foreach (Match r in Regex.Matches(res.Groups[1].Value, @"/(\S+?)\s+(\d+) 0 R"))
                    baseFont[r.Groups[1].Value] = Regex.Match(objs[int.Parse(r.Groups[2].Value)].Dict, @"/BaseFont /(\S+)").Groups[1].Value;
        var shown = new Dictionary<string, HashSet<int>>();
        foreach (var (dict, data) in objs.Values)
        {
            if (data == null || !dict.Contains("FlateDecode") || dict.Contains("/Length1")) continue;
            string s;
            try { s = Latin(Inflate(data)); } catch (InvalidDataException) { continue; }
            if (!s.Contains(" Tf")) continue;
            string? font = null;
            foreach (Match m in Regex.Matches(s, @"/(?<name>[^\s/<>\[\]()]+)\s+[-\d.]+\s+Tf|<(?<hex>[0-9A-Fa-f\s]*)>"))
            {
                if (m.Groups["name"].Success) { font = baseFont.GetValueOrDefault(m.Groups["name"].Value); continue; }
                if (font == null) continue;
                string hex = Regex.Replace(m.Groups["hex"].Value, @"\s", "");
                var set = shown.TryGetValue(font, out var h) ? h : shown[font] = new HashSet<int>();
                for (int k = 0; k + 4 <= hex.Length; k += 4) set.Add(int.Parse(hex.Substring(k, 4), NumberStyles.HexNumber));
            }
        }
        return shown;
    }

    // A dropped outline prints as a blank, so this is the property subsetting must keep: every glyph a page
    // shows has, after subsetting, the outline it had before — checked font by font against the same PDF
    // unsubset. (Glyphs that never had one, like the space, stay empty.)
    [AvaloniaFact]
    public void EveryShownGlyph_KeepsItsOutline()
    {
        var ed = new RichEditor { DefaultFontFamily = Inter };
        ed.LoadHtml(Html);
        var whole = (byte[])typeof(RichEditor).GetMethod("RenderVectorPdf", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(ed, null)!;
        var subset = (byte[])typeof(RichEditor).Assembly.GetType("AvaloniaRichEditor.Formatters.PdfFontSubsetter")!
            .GetMethod("Subset", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { whole })!;
        Assert.True(subset.Length < whole.Length / 10, $"not subset: {subset.Length:N0} of {whole.Length:N0} bytes");

        var before = FontPrograms(Objects(whole));
        var after = FontPrograms(Objects(subset));
        var shown = ShownGlyphs(Objects(whole));
        Assert.True(shown.Count >= 2, "fonts shown: " + string.Join(", ", shown.Keys));
        foreach (var (font, glyphs) in shown)
        {
            var drawn = DrawnFace(before[font], font);
            var lost = glyphs.Where(g => Outline(drawn, g) > 0 && Outline(after[font], g) == 0).ToList();
            Assert.True(lost.Count == 0, $"{font}: glyphs {string.Join(",", lost)} lost their outlines");
        }
    }

    // A character no installed font has is drawn with the font's .notdef glyph (id 0) — the box the screen shows.
    // hb_subset drops .notdef's outline unless asked to keep it (NOTDEF_OUTLINE), so the PDF printed a BLANK where the
    // screen showed a box. Ubuntu CI met it with Hangul (no CJK font on the runner).
    // Tested on the subsetter itself, not through a document: WHICH character reaches .notdef depends on the OS's
    // fallback — the first draft used unassigned U+0378, and macOS drew even that with a fallback font (its premise
    // check caught it on CI) — but how the subsetter treats glyph 0 does not.
    [AvaloniaFact]
    public void TheMissingCharacterBox_KeepsItsOutlineThroughSubsetting()
    {
        var ed = new RichEditor { DefaultFontFamily = Inter };
        ed.LoadHtml("<p>box here</p>");
        var whole = (byte[])typeof(RichEditor).GetMethod("RenderVectorPdf", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(ed, null)!;
        var inter = FontPrograms(Objects(whole)).First(kv => kv.Key.Contains("Inter")).Value; // embedded whole
        Assert.True(Outline(inter, 0) > 0, "Inter's .notdef has no outline to keep — the premise failed");

        // Only .notdef is asked for. The control counts rather than naming glyphs: hb_subset keeps a composite's
        // components, and a first draft's "not asked for" glyphs (1-4) survived — Inter's .notdef keeps a few others.
        var harfBuzz = typeof(RichEditor).Assembly.GetType("AvaloniaRichEditor.Formatters.PdfFontSubsetter")!
            .GetNestedType("HarfBuzz", BindingFlags.NonPublic)!;
        var cut = (byte[]?)harfBuzz.GetMethod("Subset", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { inter, 0, new HashSet<int> { 0 } });

        Assert.NotNull(cut);
        Assert.True(Outline(cut!, 0) > 0, ".notdef lost its outline — a missing character's box prints as a blank");
        // The control: the rest is cut, so the check above is not met by a subsetter that keeps everything.
        var (_, whole0) = GlyphStats(inter);
        var (_, kept) = GlyphStats(cut!);
        Assert.True(kept <= 16 && kept * 10 < whole0, $"{kept} of {whole0} outlines survived asking for .notdef alone — nothing was subset");
    }

    // Paging carries over: two sheets of content, two PDF pages.
    [AvaloniaFact]
    public void SavePdf_WritesOnePagePerSheet()
    {
        var pdf = SavePdf(string.Concat(Enumerable.Range(0, 80).Select(i => $"<p>line {i}</p>")));
        string raw = Latin(pdf);
        int pages = Regex.Matches(raw, @"/Type /Page\b(?!s)").Count;
        Assert.True(pages >= 2, $"pages: {pages}");
        Assert.Contains($"/Count {pages}", raw);
        Objects(pdf); // the xref is intact
    }

    // The subsetter touches nothing it does not recognise: a file that is not Skia's shape (here the raster
    // writer's PDF, and plain garbage) comes back as the very same bytes.
    [AvaloniaFact]
    public void TheSubsetter_LeavesAnythingElseAlone()
    {
        var subset = typeof(RichEditor).Assembly.GetType("AvaloniaRichEditor.Formatters.PdfFontSubsetter")!
            .GetMethod("Subset", BindingFlags.Public | BindingFlags.Static)!;
        var garbage = Encoding.ASCII.GetBytes("not a pdf at all");
        Assert.Same(garbage, subset.Invoke(null, new object[] { garbage }));

        using var ms = new MemoryStream();
        typeof(RichEditor).Assembly.GetType("AvaloniaRichEditor.Formatters.PdfWriter")!
            .GetMethod("Write", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { ms, 100.0, 100.0, 1, (Func<int, (int, int, byte[])>)(_ => (1, 1, new byte[] { 255, 255, 255 })) });
        var raster = ms.ToArray();
        Assert.Same(raster, subset.Invoke(null, new object[] { raster }));
    }

    // ---- ToUnicode ranges (2026-11-22) ---------------------------------------------------------------

    // A bfrange line is <lo> <hi> <dst>, or <lo> <hi> [<dst> …]. Reading two values a match took <dst> and the next
    // line's <lo> for a range, and a destination of three UTF-16 units overflowed int.Parse. The test document above
    // has no bfrange at all, which is why the tests above never met it.
    [Theory]
    [InlineData("<0003> <0005> <0041>", new[] { 3, 4, 5 })] // the control: one line was always read right
    [InlineData("<0003> <0005> <0041>\n<0100> <0101> <0061>\n<0200> <0200> <AC00>", new[] { 3, 4, 5, 0x100, 0x101, 0x200 })]
    [InlineData("<0003> <0005> [<0041> <0042> <0043>]\n<0100> <0101> <0061>", new[] { 3, 4, 5, 0x100, 0x101 })]
    [InlineData("<0003> <0005> [<0041>\n<0042>\n<0043>]\n<0100> <0101> <0061>", new[] { 3, 4, 5, 0x100, 0x101 })]
    [InlineData("<0003> <0003> <006600660069>\n<0100> <0101> <0061>", new[] { 3, 0x100, 0x101 })]
    public void ToUnicodeRanges_GiveOnlyTheirSourceGlyphs(string lines, int[] expected)
    {
        var glyphs = new HashSet<int>();
        typeof(RichEditor).Assembly.GetType("AvaloniaRichEditor.Formatters.PdfFontSubsetter")!
            .GetMethod("AddCMapSources", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { $"1 beginbfrange\n{lines}\nendbfrange\n", glyphs });
        Assert.Equal(expected, glyphs.OrderBy(g => g));
    }

    private static int BfRangeLines(Dictionary<int, (string Dict, byte[]? Data)> objs) => objs.Values
        .Where(o => o.Data != null && o.Dict.Contains("FlateDecode") && !o.Dict.Contains("/Length1"))
        .SelectMany(o => Regex.Matches(Latin(Inflate(o.Data!)), @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
        .Sum(block => block.Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length);

    // A document whose ToUnicode has ranges, with the bundled font so it runs on every OS: each font keeps no more
    // outlines than subsetting it to the glyphs the page shows does.
    [AvaloniaFact]
    public void AFontWithToUnicodeRanges_KeepsOnlyTheShownOutlines()
    {
        var ed = new RichEditor { DefaultFontFamily = Inter };
        ed.LoadHtml("<p>ABCDEFGHIJKLM abcdefghijklm 0123456789 nopqrstuvwxyz</p>");
        var whole = (byte[])typeof(RichEditor).GetMethod("RenderVectorPdf", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(ed, null)!;
        var subsetter = typeof(RichEditor).Assembly.GetType("AvaloniaRichEditor.Formatters.PdfFontSubsetter")!;
        var subset = (byte[])subsetter.GetMethod("Subset", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { whole })!;

        var objs = Objects(whole);
        Assert.True(BfRangeLines(objs) >= 2, "fewer than two bfrange lines — the premise failed");
        var harfBuzz = subsetter.GetNestedType("HarfBuzz", BindingFlags.NonPublic)!.GetMethod("Subset", BindingFlags.Public | BindingFlags.Static)!;
        var before = FontPrograms(objs);
        var after = FontPrograms(Objects(subset));
        var shown = ShownGlyphs(objs);
        Assert.NotEmpty(shown);
        foreach (var (font, glyphs) in shown)
        {
            var needed = (byte[])harfBuzz.Invoke(null, new object[] { DrawnFace(before[font], font), 0, new HashSet<int>(glyphs) { 0 } })!;
            int most = GlyphStats(needed).outlined, kept = GlyphStats(after[font]).outlined;
            Assert.True(kept <= most, $"{font}: {kept} outlines kept, {most} needed for the glyphs shown");
        }
    }

    private static readonly string MalgunFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");

    // The size that was seen: a few paragraphs of Korean in Malgun Gothic, regular and bold, carried nearly all of
    // both fonts.
    [AvaloniaFact]
    public void AFewKoreanParagraphs_StaySmall()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && File.Exists(MalgunFile), "needs Windows' Malgun Gothic");
        var ed = new RichEditor { DefaultFontFamily = new FontFamily("Malgun Gothic") };
        ed.LoadHtml("<p>가나다라마바사아자차카타파하</p>"
            + "<p>오늘 회의에서는 다음 분기의 예산과 일정을 정했습니다. 각 부서는 금요일까지 계획서를 제출해 주시기 바랍니다.</p>"
            + "<p><b>문의: 총무과 02-1234-5678, 담당자에게 연락</b></p>");
        using var ms = new MemoryStream();
        ed.SavePdf(ms);
        var pdf = ms.ToArray();

        var objs = Objects(pdf);
        Assert.Contains(FontPrograms(objs).Keys, name => name.Contains("Malgun"));
        Assert.True(BfRangeLines(objs) >= 2, "fewer than two bfrange lines — the premise failed");
        Assert.True(pdf.Length < 100_000, $"PDF is {pdf.Length:N0} bytes (measured 28,772 subset to the glyphs used, 6,959,570 before)");
    }

    private static MethodInfo SubsetterMethod(string name) =>
        typeof(RichEditor).Assembly.GetType("AvaloniaRichEditor.Formatters.PdfFontSubsetter")!
            .GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

    // ---- font collections (.ttc) --------------------------------------------------------------------

    private static bool IsCollection(byte[] program) => Encoding.ASCII.GetString(program, 0, 4) == "ttcf";

    // A .ttc of whole fonts: the header, then each font with its table offsets moved to where it lies.
    private static byte[] Collection(params byte[][] fonts)
    {
        static void Put(byte[] b, int o, long v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o), (uint)v);
        int at = 12 + 4 * fonts.Length;
        var all = new byte[at + fonts.Sum(f => (f.Length + 3) & ~3)];
        Encoding.ASCII.GetBytes("ttcf").CopyTo(all, 0);
        Put(all, 4, 0x00010000);
        Put(all, 8, fonts.Length);
        for (int i = 0; i < fonts.Length; i++)
        {
            Put(all, 12 + 4 * i, at);
            fonts[i].CopyTo(all, at);
            for (int t = 0, n = U16(fonts[i], 4); t < n; t++)
                Put(all, at + 12 + 16 * t + 8, U32(fonts[i], 12 + 16 * t + 8) + at);
            at += (fonts[i].Length + 3) & ~3;
        }
        return all;
    }

    // The same PDF with one font program replaced, as Skia would have written it for a font out of a collection.
    private static byte[] WithProgram(byte[] pdf, string font, byte[] program)
    {
        var objs = Objects(pdf);
        var type0 = objs.Values.First(o => o.Dict.Contains("/Subtype /Type0") && o.Dict.Contains("/BaseFont /" + font)).Dict;
        int file = Ref(objs[Ref(objs[Ref(type0, "/DescendantFonts")!.Value].Dict, "/FontDescriptor")!.Value].Dict, "/FontFile2")!.Value;
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Fastest, leaveOpen: true)) z.Write(program);
        string dict = Regex.Replace(objs[file].Dict, @"/Length \d+", $"/Length {packed.Length}");
        objs[file] = (Regex.Replace(dict, @"/Length1 \d+", $"/Length1 {program.Length}"), packed.ToArray());

        using var ms = new MemoryStream();
        void Put(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        Put("%PDF-1.4\n");
        string xref = $"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n";
        foreach (var (number, (body, data)) in objs.OrderBy(o => o.Key))
        {
            xref += $"{ms.Position:D10} 00000 n \n";
            Put($"{number} 0 obj\n{body.Trim()}\n");
            if (data != null) { Put("stream\n"); ms.Write(data); Put("\nendstream\n"); }
            Put("endobj\n");
        }
        long start = ms.Position;
        string t = Latin(pdf);
        int trailer = t.LastIndexOf("trailer", StringComparison.Ordinal);
        Put(xref + t.Substring(trailer, t.LastIndexOf("startxref", StringComparison.Ordinal) - trailer) + $"startxref\n{start}\n%%EOF\n");
        return ms.ToArray();
    }

    // The face a collection's text was drawn with, as a plain font (the helpers above read one): its directory put in
    // front of the file. The fallback font of a test document is the OS's choice, and it can be a collection.
    private static byte[] DrawnFace(byte[] program, string font)
    {
        if (!IsCollection(program)) return program;
        int face = (int)SubsetterMethod("FaceIndex").Invoke(null, new object[] { program, font })!;
        Assert.True(face >= 0, $"{font}: no single face of that name in its collection");
        int directory = (int)U32(program, 12 + 4 * face), tables = U16(program, directory + 4), size = 12 + 16 * tables;
        var plain = new byte[size + program.Length];
        Array.Copy(program, directory, plain, 0, size);
        program.CopyTo(plain, size);
        for (int t = 0; t < tables; t++)
        {
            int offset = 12 + 16 * t + 8;
            BinaryPrimitives.WriteUInt32BigEndian(plain.AsSpan(offset), U32(plain, offset) + (uint)size);
        }
        return plain;
    }

    // Skia embeds a collection as the whole file and nowhere says which face it drew with, so a viewer takes the
    // first: Nirmala UI Bold printed with the regular's outlines (measured 2026-11-22). The face is the one whose
    // PostScript name is the descriptor's FontName, and what it is cut to is what that font alone is cut to.
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void ACollection_IsCutToTheFaceTheTextWasDrawnWith(int position)
    {
        var ed = new RichEditor { DefaultFontFamily = Inter };
        ed.LoadHtml("<p>regular text <b>Bold Text</b></p>");
        var whole = (byte[])typeof(RichEditor).GetMethod("RenderVectorPdf", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(ed, null)!;
        var programs = FontPrograms(Objects(whole));
        string bold = programs.Keys.Single(k => k.EndsWith("Inter-Bold")), regular = programs.Keys.Single(k => k.EndsWith("Inter-Regular"));
        var faces = position == 0 ? new[] { programs[bold], programs[regular] } : new[] { programs[regular], programs[bold] };

        var subset = SubsetterMethod("Subset");
        var alone = FontPrograms(Objects((byte[])subset.Invoke(null, new object[] { whole })!))[bold];
        var cut = FontPrograms(Objects((byte[])subset.Invoke(null, new object[] { WithProgram(whole, bold, Collection(faces)) })!))[bold];
        Assert.False(IsCollection(cut), "the collection was embedded whole");
        Assert.Equal(alone, cut);
    }

    // No face of that name, or two: which one was drawn with cannot be told, and a guess could print other glyphs.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACollectionWhoseFaceCannotBeTold_IsLeftAsItCame(bool twice)
    {
        var ed = new RichEditor { DefaultFontFamily = Inter };
        ed.LoadHtml("<p>regular text <b>Bold Text</b></p>");
        var whole = (byte[])typeof(RichEditor).GetMethod("RenderVectorPdf", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(ed, null)!;
        var programs = FontPrograms(Objects(whole));
        string bold = programs.Keys.Single(k => k.EndsWith("Inter-Bold")), regular = programs.Keys.Single(k => k.EndsWith("Inter-Regular"));
        var collection = twice ? Collection(programs[bold], programs[bold]) : Collection(programs[regular], programs[regular]);

        var after = FontPrograms(Objects((byte[])SubsetterMethod("Subset").Invoke(null, new object[] { WithProgram(whole, bold, collection) })!));
        Assert.Equal(collection, after[bold]);
        Assert.True(after[regular].Length < programs[regular].Length / 2, "the other font was not cut");
    }

    [AvaloniaTheory]
    [InlineData("Batang")]
    [InlineData("Gulim")]
    public void ADocumentInACollectionFont_IsASmallPdf(string family)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), family == "Batang" ? "batang.ttc" : "gulim.ttc")), "needs Windows' Korean collections");
        var ed = new RichEditor { DefaultFontFamily = new FontFamily(family) };
        ed.LoadHtml("<p>가나다라마바사아자차카타파하</p><p>오늘 회의에서는 다음 분기의 예산과 일정을 정했습니다.</p>");
        using var ms = new MemoryStream();
        ed.SavePdf(ms);
        var pdf = ms.ToArray();
        var programs = FontPrograms(Objects(pdf));
        Assert.Contains(programs.Keys, k => k.EndsWith("+" + family));
        Assert.DoesNotContain(programs, p => IsCollection(p.Value));
        Assert.True(pdf.Length < 200_000, $"PDF is {pdf.Length:N0} bytes");
    }

    // The real case behind the rule: a face that is not the collection's first and has outlines of its own.
    [AvaloniaFact]
    public void NirmalaBold_IsCutFromTheBoldFace()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "Nirmala.ttc")), "needs Windows' Nirmala.ttc");
        var ed = new RichEditor { DefaultFontFamily = new FontFamily("Nirmala UI") };
        ed.LoadHtml("<p><b>हिन्दी bold</b></p>");
        using var ms = new MemoryStream();
        ed.SavePdf(ms);

        var program = FontPrograms(Objects(ms.ToArray())).Single(p => p.Key.EndsWith("+NirmalaUI-Bold")).Value;
        Assert.False(IsCollection(program), "the collection was embedded whole");
        Assert.True((bool)SubsetterMethod("HasPostScriptName").Invoke(null, new object[] { program, 0, "NirmalaUI-Bold" })!, "cut from another face");
        Assert.True(program.Length < 300_000, $"the font is {program.Length:N0} bytes");
    }

    // hb_subset (8.3.1, the one shipped) gives up re-packing GDEF for a sparse set of a big CJK font, and the font
    // then stayed whole. Layout tables have no use in a PDF — the page holds shaped glyph ids — so they are dropped.
    [AvaloniaFact]
    public void ASparseSetOfABigFont_IsStillCut()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "YuGothM.ttc");
        Assert.SkipUnless(OperatingSystem.IsWindows() && File.Exists(path), "needs Windows' Yu Gothic");
        var collection = File.ReadAllBytes(path);
        var glyphs = new HashSet<int>(Enumerable.Range(0, 24000).Where(g => g % 7 == 0));
        var cut = Cut(collection, 1, glyphs);
        Assert.True(cut != null, "hb_subset failed, so the font would be embedded whole");
        Assert.True(cut!.Length < collection.Length / 2, $"{cut.Length:N0} of {collection.Length:N0} bytes left");
    }

    private static byte[]? Cut(byte[] font, int face, IEnumerable<int> glyphs) =>
        (byte[]?)typeof(RichEditor).Assembly.GetType("AvaloniaRichEditor.Formatters.PdfFontSubsetter")!
            .GetNestedType("HarfBuzz", BindingFlags.NonPublic)!.GetMethod("Subset", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { font, face, new HashSet<int>(glyphs) { 0 } });

    private static bool HasTable(byte[] ttf, string tag) =>
        Enumerable.Range(0, U16(ttf, 4)).Any(i => Encoding.ASCII.GetString(ttf, 12 + i * 16, 4) == tag);

    // A CIDFontType2 with an Identity map is read by glyph id, so its character map is dead weight like the layout
    // tables, and a dangerous one: see the test below. On every OS, with the bundled font.
    [AvaloniaFact]
    public void ASubset_CarriesNoCharacterMap()
    {
        var ed = new RichEditor { DefaultFontFamily = Inter };
        ed.LoadHtml("<p>office text</p>");
        using var ms = new MemoryStream();
        ed.SavePdf(ms);
        var programs = FontPrograms(Objects(ms.ToArray()));
        Assert.NotEmpty(programs);
        Assert.DoesNotContain(programs, p => HasTable(p.Value, "cmap"));
    }

    // With GDEF gone hb_subset 8.3.1 got as far as re-packing cmap, and for this set it reads through a null pointer:
    // an access violation, which no catch stops. A document of some ten thousand different characters in YaHei killed
    // the process on SavePdf (measured 2026-11-22). Dropping cmap is what avoids it, so that is asserted first — a
    // regression is then a red test rather than a dead test host.
    [AvaloniaFact]
    public void AHugeSetOfABigFont_DoesNotCrashHarfBuzz()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc");
        Assert.SkipUnless(OperatingSystem.IsWindows() && File.Exists(path), "needs Windows' Microsoft YaHei");
        var collection = File.ReadAllBytes(path);
        Assert.False(HasTable(Cut(collection, 0, new[] { 1 })!, "cmap"), "cmap is kept, so the set below would crash the process");
        var cut = Cut(collection, 0, Enumerable.Range(0, 30000).Where(g => g % 3 == 0));
        Assert.True(cut != null, "hb_subset failed, so the font would be embedded whole");
    }
}
