using System;
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

        double margin = Geometry(ed, "PagePadX") * 72 / 96;
        Assert.True(raster.Right > 0 && vector.Right > 0, "nothing drawn");
        Assert.True(vector.Right < W - margin + 2, $"ink runs to x={vector.Right}, past the right margin at {W - margin:0}");
        Assert.True(Math.Abs(vector.Left - raster.Left) <= 2 && Math.Abs(vector.Right - raster.Right) <= 3,
            $"vector ink spans {vector.Left}..{vector.Right}, the rasterized page {raster.Left}..{raster.Right}");
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
            var lost = glyphs.Where(g => Outline(before[font], g) > 0 && Outline(after[font], g) == 0).ToList();
            Assert.True(lost.Count == 0, $"{font}: glyphs {string.Join(",", lost)} lost their outlines");
        }
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
}
