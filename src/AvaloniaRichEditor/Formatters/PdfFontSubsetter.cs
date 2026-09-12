using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AvaloniaRichEditor.Formatters;

// Cuts the fonts in a PDF written by Skia's PDF backend (SavePdf's vector path) down to the glyphs the
// document uses. Skia embeds each TrueType font WHOLE — the SkiaSharp native build carries no subsetter —
// so one page with a line of Korean carried all of Malgun Gothic: measured 7.7 MB, 7.45 MB of it that one
// font. HarfBuzz, which Avalonia.Skia already ships on every platform, exports hb_subset: each FontFile2
// program is subset with RETAIN_GIDS, so glyph ids — the codes in the page content (Identity-H,
// CIDToGIDMap Identity) — stay valid and nothing changes but that stream, its /Length and /Length1, and the
// xref offsets.
//
// The glyphs a font needs are the union of two readings that back each other up: the codes the content
// streams show (the ground truth) and the font's ToUnicode map. NOT the /W widths, though they look like
// a glyph list: Skia leaves out glyphs whose width equals /DW (Malgun's Hangul, all 1000, never appear)
// and writes runs of equal width as c1 c2 w ranges that span glyphs never shown — reading /W kept 72
// outlines of Inter for a dozen used (measured).
//
// Scope, deliberately narrow: the shape Skia writes (one classic xref section, direct lengths, Flate
// streams). Anything else is left exactly as it came — a larger file, never a broken one.
internal static class PdfFontSubsetter
{
    private static readonly Encoding Latin1 = Encoding.Latin1; // one char per byte: string index == byte offset

    public static byte[] Subset(byte[] pdf)
    {
        try { return SubsetCore(pdf) ?? pdf; }
        catch (Exception ex) { RichEditorDiagnostics.Report(ex); return pdf; }
    }

    private sealed class PdfObject
    {
        public int Number, Start, End;     // End = where the next object (or the xref) begins
        public string Dict = "";
        public int DataStart = -1, DataLength = -1;
    }

    private static byte[]? SubsetCore(byte[] pdf)
    {
        string text = Latin1.GetString(pdf);

        // ---- the xref: one classic section, no incremental updates ----
        int startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        if (startxref < 0) return null;
        var sx = Regex.Match(text.Substring(startxref), @"^startxref\s+(\d+)");
        if (!sx.Success) return null;
        int xrefAt = int.Parse(sx.Groups[1].Value, CultureInfo.InvariantCulture);
        if (xrefAt <= 0 || xrefAt >= text.Length || string.CompareOrdinal(text, xrefAt, "xref", 0, 4) != 0) return null;
        var head = Regex.Match(text.Substring(xrefAt, Math.Min(64, text.Length - xrefAt)), @"^xref\s+0\s+(\d+)\s+");
        if (!head.Success) return null;
        int count = int.Parse(head.Groups[1].Value, CultureInfo.InvariantCulture);
        int trailerAt = text.IndexOf("trailer", xrefAt, StringComparison.Ordinal);
        if (trailerAt < 0 || trailerAt > startxref) return null;
        string trailer = text.Substring(trailerAt, startxref - trailerAt);
        if (trailer.Contains("/Prev", StringComparison.Ordinal)) return null;

        var objects = new List<PdfObject>();
        int entries = xrefAt + head.Length;
        for (int n = 0; n < count; n++)
        {
            string e = text.Substring(entries + n * 20, 18); // "oooooooooo ggggg n", then a 2-byte EOL
            if (e[17] == 'f') { if (n != 0) return null; continue; }
            objects.Add(new PdfObject { Number = n, Start = int.Parse(e.AsSpan(0, 10), CultureInfo.InvariantCulture) });
        }
        objects.Sort((a, b) => a.Start.CompareTo(b.Start));
        var byNumber = new Dictionary<int, PdfObject>();
        for (int i = 0; i < objects.Count; i++)
        {
            var o = objects[i];
            o.End = i + 1 < objects.Count ? objects[i + 1].Start : xrefAt;
            if (!ParseObject(text, o)) return null;
            byNumber[o.Number] = o;
        }

        // ---- font chains: Type0 → CIDFontType2 → FontDescriptor → FontFile2 ----
        var fontFileOfType0 = new Dictionary<int, int>();
        var cmapOfType0 = new Dictionary<int, int>();
        var used = new Dictionary<int, HashSet<int>>(); // font file object → glyph ids
        foreach (var o in objects)
        {
            if (!Has(o.Dict, @"/Subtype\s*/Type0")) continue;
            if (Ref(o.Dict, @"/DescendantFonts\s*\[\s*(\d+)\s+0\s+R") is not int cid || !byNumber.TryGetValue(cid, out var cidFont)) continue;
            if (!Has(cidFont.Dict, @"/Subtype\s*/CIDFontType2") || !Has(cidFont.Dict, @"/CIDToGIDMap\s*/Identity")) continue;
            if (Ref(cidFont.Dict, @"/FontDescriptor\s+(\d+)\s+0\s+R") is not int fd || !byNumber.TryGetValue(fd, out var descriptor)) continue;
            if (Ref(descriptor.Dict, @"/FontFile2\s+(\d+)\s+0\s+R") is not int ff || !byNumber.ContainsKey(ff)) continue;
            fontFileOfType0[o.Number] = ff;
            if (!used.ContainsKey(ff)) used[ff] = new HashSet<int> { 0 }; // .notdef always
            if (Ref(o.Dict, @"/ToUnicode\s+(\d+)\s+0\s+R") is int tu) cmapOfType0[o.Number] = tu;
        }
        if (fontFileOfType0.Count == 0) return null;

        foreach (var (type0, tu) in cmapOfType0)
            if (byNumber.TryGetValue(tu, out var cmap) && Inflate(pdf, cmap) is string c)
                AddCMapSources(c, used[fontFileOfType0[type0]]);

        // Resource names → Type0 fonts, from every /Font resource dictionary in the file.
        var fontByName = new Dictionary<string, int>();
        foreach (Match res in Regex.Matches(text, @"/Font\s*<<([^<>]*)>>"))
            foreach (Match r in Regex.Matches(res.Groups[1].Value, @"/([^\s/<>\[\]()]+)\s+(\d+)\s+0\s+R"))
                if (int.TryParse(r.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int f) && fontFileOfType0.ContainsKey(f))
                    fontByName[r.Groups[1].Value] = f;

        var fontFiles = new HashSet<int>(fontFileOfType0.Values);
        foreach (var o in objects)
        {
            if (o.DataStart < 0 || fontFiles.Contains(o.Number) || Has(o.Dict, @"/Subtype\s*/Image")) continue;
            if (Inflate(pdf, o) is string content && content.Contains("Tf", StringComparison.Ordinal))
                CollectShownGlyphs(content, fontByName, type0 => used[fontFileOfType0[type0]]);
        }

        // ---- subset each font program ----
        var replaced = new Dictionary<int, (string Dict, byte[] Data)>();
        foreach (var (ff, glyphs) in used)
        {
            var o = byNumber[ff];
            if (!Has(o.Dict, @"/Filter\s*/FlateDecode") || InflateBytes(pdf, o) is not byte[] program) continue;
            if (program.Length < 12 || (program[0] == 't' && program[1] == 't' && program[2] == 'c' && program[3] == 'f')) continue;
            if (HarfBuzz.Subset(program, glyphs) is not byte[] subset || subset.Length >= program.Length) continue;
            byte[] packed = Deflate(subset);
            string dict = Regex.Replace(o.Dict, @"/Length\s+\d+", "/Length " + packed.Length.ToString(CultureInfo.InvariantCulture));
            dict = Regex.Replace(dict, @"/Length1\s+\d+", "/Length1 " + subset.Length.ToString(CultureInfo.InvariantCulture));
            replaced[ff] = (dict, packed);
        }
        if (replaced.Count == 0) return null;

        // ---- write it back: same objects in the same order, a fresh xref ----
        using var ms = new MemoryStream(pdf.Length);
        void Ascii(string s) { var b = Latin1.GetBytes(s); ms.Write(b, 0, b.Length); }
        ms.Write(pdf, 0, objects[0].Start);
        var offsets = new Dictionary<int, long>();
        foreach (var o in objects)
        {
            offsets[o.Number] = ms.Position;
            if (replaced.TryGetValue(o.Number, out var r))
            {
                Ascii($"{o.Number} 0 obj\n{r.Dict}\nstream\n");
                ms.Write(r.Data, 0, r.Data.Length);
                Ascii("\nendstream\nendobj\n");
            }
            else ms.Write(pdf, o.Start, o.End - o.Start);
        }
        long xref = ms.Position;
        var sb = new StringBuilder();
        sb.Append("xref\n0 ").Append(count).Append('\n').Append("0000000000 65535 f \n");
        for (int n = 1; n < count; n++) sb.Append(offsets[n].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        Ascii(sb.ToString());
        Ascii(trailer);
        Ascii($"startxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        return ms.ToArray();
    }

    // "N 0 obj <<dict>> [stream ... endstream] endobj": the dictionary text and, for a stream, where its
    // data lies (by its direct /Length — an indirect one is out of scope).
    private static bool ParseObject(string text, PdfObject o)
    {
        var m = Regex.Match(text.Substring(o.Start, Math.Min(32, o.End - o.Start)), @"^(\d+)\s+0\s+obj\s*");
        if (!m.Success || int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) != o.Number) return false;
        int bodyStart = o.Start + m.Length;
        int streamKw = text.IndexOf("stream", bodyStart, o.End - bodyStart, StringComparison.Ordinal);
        int endobj = text.IndexOf("endobj", bodyStart, o.End - bodyStart, StringComparison.Ordinal);
        if (endobj < 0) return false;
        bool isStream = streamKw >= 0 && streamKw < endobj;
        o.Dict = text.Substring(bodyStart, (isStream ? streamKw : endobj) - bodyStart).Trim();
        if (!isStream) return true;
        if (Regex.Match(o.Dict, @"/Length\s+(\d+)(?!\s+\d+\s+R)") is not { Success: true } len) return false;
        int data = streamKw + 6;
        if (text[data] == '\r') data++;
        if (text[data] == '\n') data++;
        o.DataStart = data;
        o.DataLength = int.Parse(len.Groups[1].Value, CultureInfo.InvariantCulture);
        return o.DataStart + o.DataLength <= o.End;
    }

    private static bool Has(string dict, string pattern) => Regex.IsMatch(dict, pattern);

    private static int? Ref(string dict, string pattern)
        => Regex.Match(dict, pattern) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;

    private static byte[]? InflateBytes(byte[] pdf, PdfObject o)
    {
        if (o.DataStart < 0 || !Has(o.Dict, @"/Filter\s*/FlateDecode")) return null;
        try
        {
            using var z = new ZLibStream(new MemoryStream(pdf, o.DataStart, o.DataLength), CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            z.CopyTo(outMs);
            return outMs.ToArray();
        }
        catch (InvalidDataException) { return null; }
    }

    private static string? Inflate(byte[] pdf, PdfObject o) => InflateBytes(pdf, o) is byte[] b ? Latin1.GetString(b) : null;

    private static byte[] Deflate(byte[] data)
    {
        using var outMs = new MemoryStream();
        using (var z = new ZLibStream(outMs, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data, 0, data.Length);
        return outMs.ToArray();
    }

    // ToUnicode: every source code in bfchar pairs and bfrange ranges.
    private static void AddCMapSources(string cmap, HashSet<int> glyphs)
    {
        foreach (Match block in Regex.Matches(cmap, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
            foreach (Match pair in Regex.Matches(block.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<[0-9A-Fa-f]*>"))
                glyphs.Add(int.Parse(pair.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        foreach (Match block in Regex.Matches(cmap, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
            foreach (Match range in Regex.Matches(block.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
            {
                int lo = int.Parse(range.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int hi = int.Parse(range.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                for (int g = lo; g <= hi && g - lo < 65536; g++) glyphs.Add(g);
            }
    }

    // The codes shown in a content stream: a small tokenizer that follows the font selected by Tf and reads
    // the 2-byte codes of every hex string (Skia writes glyph ids as <hhhh…>, alone or inside TJ arrays).
    private static void CollectShownGlyphs(string content, Dictionary<string, int> fontByName, Func<int, HashSet<int>> glyphsOf)
    {
        int? font = null;
        string? lastName = null;
        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (c == '%') { while (i < content.Length && content[i] != '\n' && content[i] != '\r') i++; continue; }
            if (c == '(')
            {
                int depth = 1;
                for (i++; i < content.Length && depth > 0; i++)
                {
                    if (content[i] == '\\') { i++; continue; }
                    if (content[i] == '(') depth++;
                    else if (content[i] == ')') depth--;
                }
                i--;
                continue;
            }
            if (c == '<')
            {
                if (i + 1 < content.Length && content[i + 1] == '<') { i++; continue; }
                int close = content.IndexOf('>', i + 1);
                if (close < 0) return;
                if (font is int f)
                {
                    var hex = new StringBuilder();
                    for (int k = i + 1; k < close; k++) if (Uri.IsHexDigit(content[k])) hex.Append(content[k]);
                    var set = glyphsOf(f);
                    for (int k = 0; k + 4 <= hex.Length; k += 4)
                        set.Add(int.Parse(hex.ToString(k, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                }
                i = close;
                continue;
            }
            if (c == '/')
            {
                int s = ++i;
                while (i < content.Length && !char.IsWhiteSpace(content[i]) && "/[]<>()%{}".IndexOf(content[i]) < 0) i++;
                lastName = content.Substring(s, i - s);
                i--;
                continue;
            }
            if (c == 'T' && i + 1 < content.Length && content[i + 1] == 'f'
                && (i + 2 >= content.Length || char.IsWhiteSpace(content[i + 2]))
                && (i == 0 || char.IsWhiteSpace(content[i - 1])))
            {
                font = lastName != null && fontByName.TryGetValue(lastName, out int obj) ? obj : null;
                i++;
            }
        }
    }

    // hb_subset through the HarfBuzz native library Avalonia.Skia ships (its managed binding covers shaping
    // only). RETAIN_GIDS keeps every glyph at its id — removed ones become empty — so the PDF's codes need
    // no remapping.
    private static class HarfBuzz
    {
        private const string Lib = "libHarfBuzzSharp";
        // RETAIN_GIDS, and NO_LAYOUT_CLOSURE: the page content is already-shaped glyph ids, so the glyphs a
        // GSUB substitution could reach (ligatures, alternates) are dead weight — with the closure, Inter kept
        // 73 outlines for a line of text.
        private const uint RetainGids = 0x2 | 0x200;
        private const int MemoryModeDuplicate = 0;

        public static byte[]? Subset(byte[] font, HashSet<int> glyphs)
        {
            IntPtr blob = IntPtr.Zero, face = IntPtr.Zero, input = IntPtr.Zero, sub = IntPtr.Zero, outBlob = IntPtr.Zero;
            var pin = GCHandle.Alloc(font, GCHandleType.Pinned);
            try
            {
                blob = hb_blob_create(pin.AddrOfPinnedObject(), (uint)font.Length, MemoryModeDuplicate, IntPtr.Zero, IntPtr.Zero);
                face = hb_face_create(blob, 0);
                input = hb_subset_input_create_or_fail();
                if (face == IntPtr.Zero || input == IntPtr.Zero) return null;
                hb_subset_input_set_flags(input, RetainGids);
                var set = hb_subset_input_glyph_set(input);
                foreach (int g in glyphs) hb_set_add(set, (uint)g);
                sub = hb_subset_or_fail(face, input);
                if (sub == IntPtr.Zero) return null;
                outBlob = hb_face_reference_blob(sub);
                IntPtr data = hb_blob_get_data(outBlob, out uint length);
                if (data == IntPtr.Zero || length == 0) return null;
                var result = new byte[length];
                Marshal.Copy(data, result, 0, (int)length);
                return result;
            }
            finally
            {
                if (outBlob != IntPtr.Zero) hb_blob_destroy(outBlob);
                if (sub != IntPtr.Zero) hb_face_destroy(sub);
                if (input != IntPtr.Zero) hb_subset_input_destroy(input);
                if (face != IntPtr.Zero) hb_face_destroy(face);
                if (blob != IntPtr.Zero) hb_blob_destroy(blob);
                pin.Free();
            }
        }

        [DllImport(Lib)] private static extern IntPtr hb_blob_create(IntPtr data, uint length, int mode, IntPtr userData, IntPtr destroy);
        [DllImport(Lib)] private static extern IntPtr hb_face_create(IntPtr blob, uint index);
        [DllImport(Lib)] private static extern IntPtr hb_subset_input_create_or_fail();
        [DllImport(Lib)] private static extern void hb_subset_input_set_flags(IntPtr input, uint flags);
        [DllImport(Lib)] private static extern IntPtr hb_subset_input_glyph_set(IntPtr input);
        [DllImport(Lib)] private static extern void hb_set_add(IntPtr set, uint codepoint);
        [DllImport(Lib)] private static extern IntPtr hb_subset_or_fail(IntPtr face, IntPtr input);
        [DllImport(Lib)] private static extern IntPtr hb_face_reference_blob(IntPtr face);
        [DllImport(Lib)] private static extern IntPtr hb_blob_get_data(IntPtr blob, out uint length);
        [DllImport(Lib)] private static extern void hb_blob_destroy(IntPtr blob);
        [DllImport(Lib)] private static extern void hb_face_destroy(IntPtr face);
        [DllImport(Lib)] private static extern void hb_subset_input_destroy(IntPtr input);
    }
}
