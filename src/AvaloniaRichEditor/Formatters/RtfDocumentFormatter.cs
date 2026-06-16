using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Formatters;

/// <summary>
/// Parses a practical subset of RTF — the "Rich Text Format" both Word and the Korean HWP put on
/// the clipboard — into a <see cref="FlowDocument"/>: paragraphs, bold/italic/underline/strike,
/// font size, foreground colour, embedded images (<c>\pict</c> PNG/JPEG, bytes carried inline), and
/// simple tables (<c>\trowd…\cell…\row</c>). Unlike Word's CF_HTML (which references temp files for
/// images), RTF embeds the image bytes, so nothing is lost. Zero external dependencies beyond a
/// code-page provider for CJK text (<c>\'hh</c> bytes are decoded with the document's <c>\ansicpg</c>).
/// </summary>
public static class RtfDocumentFormatter
{
    static RtfDocumentFormatter()
    {
        // CP949 (Korean), Shift-JIS, GB2312 etc. aren't in .NET's default set — register them so
        // \'hh runs from HWP/Word decode correctly.
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
    }

    /// <summary>True if <paramref name="text"/> starts with the RTF signature.</summary>
    public static bool LooksLikeRtf(string? text)
        => text != null && text.TrimStart().StartsWith(@"{\rtf", StringComparison.Ordinal);

    /// <summary>Parses an RTF string into a <see cref="FlowDocument"/> (empty document on failure).</summary>
    public static FlowDocument Parse(string rtf)
    {
        try { return new RtfParser(rtf).Run(); }
        catch { return new FlowDocument(); }
    }

    /// <summary>Serializes a <see cref="FlowDocument"/> to an RTF string (the inverse of <see cref="Parse"/>):
    /// paragraphs, runs (bold/italic/underline/strike, size, colour, font family), alignment/indent,
    /// headings, lists (as literal markers), tables, and embedded PNG/JPEG images. Non-ASCII text is
    /// emitted as <c>\u</c> escapes, so the output is code-page independent and reads in Word/HWP/WordPad.</summary>
    public static string Write(FlowDocument document) => new RtfWriter().Build(document);
}

// One pass over the RTF char stream. Group state (character formatting + the active "destination")
// is pushed on '{' and popped on '}', so nested formatting restores correctly. Normal text is
// buffered as bytes and decoded with the document code page so multi-byte CJK characters (which
// span several \'hh) come out whole.
internal sealed class RtfParser
{
    private readonly string _s;
    private int _i;

    private enum Dest { Normal, Skip, ColorTable, Pict }

    private struct State
    {
        public bool Bold, Italic, Underline, Strike;
        public double FontSize;   // points; 0 = use the run default
        public int Color;         // index into _colors; -1 = default (black)
        public Dest Dest;
        public int UnicodeSkip;   // chars to swallow after a \uN (set by \ucN)
    }

    private State _st = new() { Color = -1, UnicodeSkip = 1 };
    private readonly Stack<State> _stack = new();

    private readonly FlowDocument _doc = new();
    private Paragraph _para = new();
    private readonly StringBuilder _run = new();

    // Code-page text accumulator: plain chars and \'hh escapes are bytes in the document code page
    // (\ansicpg, e.g. 949 = CP949 for Korean). Multi-byte characters span several bytes, so they are
    // buffered and decoded together; \uN unicode flushes the buffer first to keep order.
    private readonly List<byte> _bytes = new();
    private int _codepage = 1252;
    private Encoding? _enc;
    private Encoding Enc => _enc ??= GetEncoding(_codepage);

    // Color table (\colortbl): index 0 is the "auto" entry. Built while Dest == ColorTable.
    private readonly List<Color> _colors = new();
    private int _ctR, _ctG, _ctB;
    private bool _ctHasColor; // false for the leading auto entry (";" with no \red/\green/\blue)

    // \pict accumulator (active while Dest == Pict). Only PNG/JPEG blips are decodable.
    private readonly StringBuilder _pictHex = new();
    private string? _pictMime;
    private int _pictWTwips, _pictHTwips;

    // Table builder: rows accumulate until a normal paragraph (or the document end) flushes them
    // into a TableBlock. Each cell is one Paragraph; intra-cell \par becomes a newline.
    private List<List<Paragraph>>? _tableRows;
    private List<Paragraph>? _curRow;
    // \cellx<N> = cumulative right boundary (twips) per column. Captured from the first row so the
    // pasted table keeps the source column widths instead of a uniform default.
    private List<int> _curCellx = new();
    private List<int>? _tableCellx;

    public RtfParser(string s) => _s = s;

    public FlowDocument Run()
    {
        while (_i < _s.Length)
        {
            char c = _s[_i];
            if (c == '{') { _stack.Push(_st); _i++; }
            else if (c == '}')
            {
                if (_st.Dest == Dest.Pict) FinalizePict(); else FlushRun();
                _st = _stack.Count > 0 ? _stack.Pop() : _st; _i++;
            }
            else if (c == '\\') ReadControl();
            else if (c == '\r' || c == '\n') _i++;            // RTF line breaks are not content
            else if (_st.Dest == Dest.ColorTable && c == ';') { CloseColorEntry(); _i++; }
            else if (_st.Dest == Dest.Pict) { if (Uri.IsHexDigit(c)) _pictHex.Append(c); _i++; }
            else { if (_st.Dest == Dest.Normal) AppendByte(c); _i++; }
        }
        EndRow();        // a table that ran to the document end (no trailing normal paragraph)
        FlushRun();
        FinalizeTable();
        if (_para.Inlines.Count > 0) _doc.Blocks.Add(_para);
        if (_doc.Blocks.Count == 0) _doc.Blocks.Add(new Paragraph());
        return _doc;
    }

    // ---- control word / symbol ----

    private void ReadControl()
    {
        _i++; // past '\'
        if (_i >= _s.Length) return;
        char c = _s[_i];

        if (c == '\'') { ReadHexChar(); return; }
        if (!char.IsLetter(c))
        {
            // Control symbol: \\ \{ \} are literals; \~ nbsp, \_ hyphen, \* marks an optional dest.
            _i++;
            if (_st.Dest == Dest.Normal)
            {
                if (c == '\\' || c == '{' || c == '}') AppendByte(c);
                else if (c == '~') AppendByte(' ');
            }
            if (c == '*') _st.Dest = Dest.Skip; // unknown optional destination -> ignore its body
            return;
        }

        // Control word: letters then an optional signed integer, then an optional single space.
        int start = _i;
        while (_i < _s.Length && char.IsLetter(_s[_i])) _i++;
        string word = _s.Substring(start, _i - start);
        int? param = null;
        if (_i < _s.Length && (_s[_i] == '-' || char.IsDigit(_s[_i])))
        {
            int ns = _i;
            if (_s[_i] == '-') _i++;
            while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
            param = int.Parse(_s.Substring(ns, _i - ns), CultureInfo.InvariantCulture);
        }
        if (_i < _s.Length && _s[_i] == ' ') _i++; // a single trailing space is part of the keyword

        Apply(word, param);
    }

    private void ReadHexChar()
    {
        _i++; // past '\''
        if (_i + 1 >= _s.Length) return;
        string hex = _s.Substring(_i, 2);
        _i += 2;
        if (_st.Dest != Dest.Normal) return;
        if (byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            _bytes.Add(b); // decoded with the code page when the byte run is flushed
    }

    private void Apply(string w, int? p)
    {
        switch (w)
        {
            // document code page for \'hh runs
            case "ansicpg": _codepage = p ?? 1252; _enc = null; break;

            // character formatting — flush the run under the OLD state before the change
            case "b": SetBold(p != 0); break;
            case "i": SetItalic(p != 0); break;
            case "ul": SetUnderline(p != 0); break;
            case "ulnone": SetUnderline(false); break;
            case "strike": SetStrike(p != 0); break;
            case "fs": FlushRun(); _st.FontSize = (p ?? 24) / 2.0; break;
            case "cf": FlushRun(); _st.Color = p ?? -1; break;
            case "plain": FlushRun(); _st.Bold = _st.Italic = _st.Underline = _st.Strike = false; _st.FontSize = 0; _st.Color = -1; break;

            // text/paragraph structure
            case "par": case "sect": EndParagraph(); break;
            case "line": if (_st.Dest == Dest.Normal) _bytes.Add(10); break;
            case "tab": if (_st.Dest == Dest.Normal) _bytes.Add(9); break;
            case "pard": break; // paragraph-property reset: nothing we track resets here

            // tables
            case "trowd": StartRow(); break;
            case "cell": EndCell(); break;
            case "row": EndRow(); break;
            case "intbl": break;                  // structure is driven by \cell/\row
            case "cellx": _curCellx.Add(p ?? 0); break; // column boundary, for source-width preservation

            // Nested tables (B deferred): the model can't nest a table in a cell, so flatten —
            // nested cells become tab-separated, nested rows newline-separated, inside the parent cell.
            case "nestcell": if (_st.Dest == Dest.Normal) _bytes.Add(9); break;
            case "nestrow": if (_st.Dest == Dest.Normal) _bytes.Add(10); break;

            // Text boxes / shapes (HWP 글상자): the editor has no floating frame, so pull out the
            // \shptxt content as normal text and skip the shape's property name/value groups (\sp/\sn/\sv).
            case "shptxt": _st.Dest = Dest.Normal; break;
            case "sp": case "sn": case "sv": _st.Dest = Dest.Skip; break;

            // unicode
            case "u": EmitUnicode(p ?? 0); break;
            case "uc": _st.UnicodeSkip = p ?? 1; break;

            // destinations
            case "colortbl": _st.Dest = Dest.ColorTable; _colors.Clear(); _ctR = _ctG = _ctB = 0; _ctHasColor = false; break;
            case "fonttbl": case "stylesheet": case "info": case "pntext": case "themedata":
            case "datastore": case "xmlnstbl": case "rsidtbl": case "generator": case "listtable":
            case "listoverridetable": case "revtbl":
                _st.Dest = Dest.Skip; break;

            // color-table component words
            case "red": _ctR = p ?? 0; _ctHasColor = true; break;
            case "green": _ctG = p ?? 0; _ctHasColor = true; break;
            case "blue": _ctB = p ?? 0; _ctHasColor = true; break;

            // images. \*\shppict wraps the modern (PNG/JPEG) pict — understood, so un-skip it; the
            // \nonshppict WMF/EMF fallback alongside it is skipped.
            case "shppict": _st.Dest = Dest.Normal; break;
            case "nonshppict": _st.Dest = Dest.Skip; break;
            case "pict": FlushRun(); _st.Dest = Dest.Pict; _pictHex.Clear(); _pictMime = null; _pictWTwips = _pictHTwips = 0; break;
            case "pngblip": _pictMime = "image/png"; break;
            case "jpegblip": _pictMime = "image/jpeg"; break;
            case "picwgoal": _pictWTwips = p ?? 0; break;
            case "pichgoal": _pictHTwips = p ?? 0; break;

            default: break; // unknown control word: ignore (its text, if any, still flows)
        }
    }

    // A ';' closed one \colortbl entry. An entry with no \red/\green/\blue is the "auto" colour —
    // store it with zero alpha so MakeRun leaves the run's foreground at the default.
    private void CloseColorEntry()
    {
        _colors.Add(_ctHasColor
            ? Color.FromRgb((byte)_ctR, (byte)_ctG, (byte)_ctB)
            : Color.FromArgb(0, 0, 0, 0));
        _ctR = _ctG = _ctB = 0;
        _ctHasColor = false;
    }

    private void SetBold(bool v) { if (v != _st.Bold) FlushRun(); _st.Bold = v; }
    private void SetItalic(bool v) { if (v != _st.Italic) FlushRun(); _st.Italic = v; }
    private void SetUnderline(bool v) { if (v != _st.Underline) FlushRun(); _st.Underline = v; }
    private void SetStrike(bool v) { if (v != _st.Strike) FlushRun(); _st.Strike = v; }

    private void EmitUnicode(int code)
    {
        FlushBytes(); // keep order: any buffered code-page text comes before the unicode char
        if (_st.Dest == Dest.Normal)
        {
            if (code < 0) code += 65536; // RTF \u is signed 16-bit
            try { _run.Append(char.ConvertFromUtf32(code)); } catch { }
        }
        // Skip the spell-out fallback that follows a \uN (a plain char or a \'hh each count as one).
        for (int k = 0; k < _st.UnicodeSkip && _i < _s.Length; k++)
        {
            if (_s[_i] == '\\')
                _i += (_i + 1 < _s.Length && _s[_i + 1] == '\'') ? 4 : 2; // skip \'hh or \symbol
            else if (_s[_i] == '{' || _s[_i] == '}') break;
            else _i++;
        }
    }

    // ---- building ----

    private void AppendByte(char c)
    {
        if (_st.Dest != Dest.Normal) return;
        if (c < 256) _bytes.Add((byte)c);
        else { FlushBytes(); _run.Append(c); }
    }

    private void FlushBytes()
    {
        if (_bytes.Count == 0) return;
        _run.Append(Enc.GetString(_bytes.ToArray()));
        _bytes.Clear();
    }

    private void FlushRun()
    {
        FlushBytes();
        if (_run.Length == 0) return;
        if (_st.Dest != Dest.Normal) { _run.Clear(); return; }
        _para.Inlines.Add(MakeRun(_run.ToString()));
        _run.Clear();
    }

    private Run MakeRun(string text)
    {
        var r = new Run
        {
            Text = text,
            FontWeight = _st.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = _st.Italic ? FontStyle.Italic : FontStyle.Normal,
            FontSize = _st.FontSize > 0 ? _st.FontSize : 14,
        };
        if (_st.Underline || _st.Strike)
        {
            var decos = new TextDecorationCollection();
            if (_st.Underline) decos.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
            if (_st.Strike) decos.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
            r.TextDecorations = decos;
        }
        if (_st.Color >= 0 && _st.Color < _colors.Count)
        {
            var col = _colors[_st.Color];
            if (col.A != 0) r.Foreground = new ImmutableSolidColorBrush(col);
        }
        return r;
    }

    private void EndParagraph()
    {
        // Inside a table cell, \par is an intra-cell line break, not a document paragraph.
        if (_curRow != null) { _bytes.Add(10); return; }
        FlushRun();
        FinalizeTable(); // a normal paragraph ends any table that was being built
        _doc.Blocks.Add(_para);
        _para = new Paragraph();
    }

    // ---- tables ----

    private void StartRow()
    {
        _tableRows ??= new List<List<Paragraph>>();
        _curRow ??= new List<Paragraph>();
        _curCellx = new List<int>(); // \cellx for this row follows \trowd
        _para = new Paragraph();     // first cell's content
    }

    private void EndCell()
    {
        if (_curRow == null) StartRow();
        FlushRun();
        _curRow!.Add(_para);
        _para = new Paragraph();
    }

    private void EndRow()
    {
        if (_curRow == null) return;
        _tableRows ??= new List<List<Paragraph>>();
        _tableRows.Add(_curRow);
        _curRow = null;
        if (_tableCellx == null && _curCellx.Count > 0) _tableCellx = _curCellx; // keep the first row's columns
    }

    private void FinalizeTable()
    {
        var rows = _tableRows;
        var cellx = _tableCellx;
        _tableRows = null;
        _curRow = null;
        _tableCellx = null;
        if (rows == null || rows.Count == 0) return;

        int cols = 0;
        foreach (var r in rows) if (r.Count > cols) cols = r.Count;
        if (cols == 0) return;

        var tb = new TableBlock(rows.Count, cols);
        tb.Cells.Clear();
        foreach (var r in rows)
        {
            var cells = new List<Paragraph>(cols);
            for (int c = 0; c < cols; c++) cells.Add(c < r.Count ? r[c] : new Paragraph());
            tb.Cells.Add(cells);
        }
        tb.Rows = rows.Count;
        tb.Columns = cols;
        // Source column widths from \cellx (cumulative right boundaries in twips → px /15).
        if (cellx != null && cellx.Count > 0)
        {
            tb.ColumnWidths.Clear();
            int prev = 0;
            for (int c = 0; c < cols; c++)
            {
                int boundary = c < cellx.Count ? cellx[c] : prev + 1500;
                double wpx = (boundary - prev) / 15.0;
                tb.ColumnWidths.Add(wpx >= 16 ? wpx : 100); // floor out 0/negative/garbage boundaries
                prev = boundary;
            }
        }
        tb.ColSpans.Clear();
        tb.RowSpans.Clear();
        for (int r = 0; r < rows.Count; r++)
        {
            var cs = new List<int>(cols);
            var rs = new List<int>(cols);
            for (int c = 0; c < cols; c++) { cs.Add(1); rs.Add(1); }
            tb.ColSpans.Add(cs);
            tb.RowSpans.Add(rs);
        }
        _doc.Blocks.Add(tb);
    }

    // ---- images ----

    // Decodes the accumulated \pict bytes and places the image: small (<64px) inline, larger as its
    // own block. Twips → px is /15 (1440 twips = 96 px/in). Unsupported blips or undecodable bytes drop.
    private void FinalizePict()
    {
        var hex = _pictHex.ToString();
        _pictHex.Clear();
        if (_pictMime == null || hex.Length < 8) return;
        var bytes = HexToBytes(hex);
        if (bytes == null || bytes.Length == 0) return;

        double w = _pictWTwips > 0 ? _pictWTwips / 15.0 : 0;
        double h = _pictHTwips > 0 ? _pictHTwips / 15.0 : 0;
        Avalonia.Media.Imaging.Bitmap? bmp = null;
        if (w <= 0 || h <= 0)
        {
            try { bmp = new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(bytes)); }
            catch { return; } // not a decodable PNG/JPEG after all
            w = bmp.Size.Width; h = bmp.Size.Height;
        }
        string mime = ImageMime.Detect(bytes) ?? _pictMime;

        if (w < 64 && h < 64)
        {
            var img = new InlineImage { Width = w, Height = h };
            img.SetImageData(bytes, mime, bmp);
            _para.Inlines.Add(img);
        }
        else
        {
            if (_para.Inlines.Count > 0) { _doc.Blocks.Add(_para); _para = new Paragraph(); }
            var ib = new ImageBlock { Width = w, Height = h };
            ib.SetImageData(bytes, mime, bmp);
            _doc.Blocks.Add(ib);
        }
    }

    private static byte[]? HexToBytes(string hex)
    {
        if ((hex.Length & 1) != 0) hex = hex.Substring(0, hex.Length - 1); // ignore a trailing nibble
        var bytes = new byte[hex.Length / 2];
        for (int k = 0; k < bytes.Length; k++)
            if (!byte.TryParse(hex.AsSpan(k * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[k]))
                return null;
        return bytes;
    }

    private static Encoding GetEncoding(int codepage)
    {
        try { return Encoding.GetEncoding(codepage); }
        catch { return Encoding.Latin1; }
    }
}

// Serializes a FlowDocument to RTF — the inverse of RtfParser, covering the same subset. The body is
// built first (collecting the fonts and colours it references), then the \fonttbl/\colortbl headers are
// prepended, since RTF requires them before the content.
internal sealed class RtfWriter
{
    private readonly StringBuilder _body = new();
    private readonly List<string> _fonts = new() { "" };  // \f0 = default
    private readonly List<Color> _colors = new();          // \colortbl entry 0 is "auto"; these are 1-based
    private readonly Dictionary<string, int> _fontIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, int> _colorIndex = new();

    public string Build(FlowDocument doc)
    {
        int ordered = 0;
        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph p && p.ListType == ListKind.Ordered) ordered++;
            else ordered = 0;
            WriteBlock(block, ordered);
        }

        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0");
        sb.Append(@"{\fonttbl");
        for (int i = 0; i < _fonts.Count; i++)
            sb.Append($@"{{\f{i}\fnil ").Append(EscapeText(_fonts[i].Length == 0 ? "Default" : _fonts[i])).Append(";}");
        sb.Append('}');
        sb.Append(@"{\colortbl;");
        foreach (var c in _colors) sb.Append($@"\red{c.R}\green{c.G}\blue{c.B};");
        sb.Append('}').Append('\n');
        sb.Append(_body);
        sb.Append('}');
        return sb.ToString();
    }

    private void WriteBlock(Block block, int ordered)
    {
        switch (block)
        {
            case Paragraph p: WriteParagraph(p, ordered); break;
            case TableBlock tb: WriteTable(tb); break;
            case ImageBlock ib when ib.RawBytes != null:
                _body.Append(@"\pard ");
                WritePict(ib.RawBytes, ib.MimeType, ib.Width, ib.Height);
                _body.Append(@"\par").Append('\n');
                break;
            case DividerBlock:
                // A thin bottom border on an empty paragraph reads as a horizontal rule.
                _body.Append(@"\pard\brdrb\brdrs\brdrw10\brsp20 \par").Append('\n');
                break;
        }
    }

    private void WriteParagraph(Paragraph p, int ordered)
    {
        _body.Append(@"\pard");
        switch (p.TextAlignment)
        {
            case TextAlignment.Center: _body.Append(@"\qc"); break;
            case TextAlignment.Right: _body.Append(@"\qr"); break;
            case TextAlignment.Justify: _body.Append(@"\qj"); break;
        }
        if (p.Indent > 0) _body.Append($@"\li{(int)(p.Indent * 15)}");
        _body.Append(' ');

        // Lists have no portable round-trip in this subset, so emit a literal marker + tab (Word renders
        // it; our parser treats it as text). Headings export as the larger/bold look the editor shows.
        if (p.ListType == ListKind.Bullet) _body.Append(@"\bullet\tab ");
        else if (p.ListType == ListKind.Ordered) _body.Append($@"{ordered}.\tab ");

        bool heading = p.HeadingLevel is >= 1 and <= 6;
        double headingSize = heading ? HeadingSize(p.HeadingLevel) : 0;
        foreach (var inline in p.Inlines)
        {
            if (inline is Run r && !string.IsNullOrEmpty(r.Text)) WriteRun(r, heading, headingSize);
            else if (inline is InlineImage img && img.RawBytes != null) WritePict(img.RawBytes, img.MimeType, img.Width, img.Height);
        }
        _body.Append(@"\par").Append('\n');
    }

    private void WriteRun(Run r, bool heading, double headingSize)
    {
        _body.Append('{');
        if (r.FontWeight == FontWeight.Bold || heading) _body.Append(@"\b");
        if (r.FontStyle == FontStyle.Italic) _body.Append(@"\i");
        if (HasDecoration(r.TextDecorations, TextDecorationLocation.Underline) || !string.IsNullOrEmpty(r.NavigateUri)) _body.Append(@"\ul");
        if (HasDecoration(r.TextDecorations, TextDecorationLocation.Strikethrough)) _body.Append(@"\strike");
        int f = FontIndex(r.FontFamily);
        if (f > 0) _body.Append($@"\f{f}");
        double size = r.FontSize <= 0 ? 14 : r.FontSize;
        if (heading && (r.FontSize <= 0 || Math.Abs(r.FontSize - 14) < 0.01)) size = headingSize;
        _body.Append($@"\fs{(int)Math.Round(size * 2)}");
        int c = ColorIndex(r.Foreground);
        if (c > 0) _body.Append($@"\cf{c}");
        _body.Append(' ');
        WriteEscaped(r.Text!);
        _body.Append('}');
    }

    private void WriteTable(TableBlock tb)
    {
        for (int row = 0; row < tb.Rows; row++)
        {
            _body.Append(@"\trowd");
            // Cumulative right cell boundaries in twips (px*15), from the column widths.
            int x = 0;
            for (int col = 0; col < tb.Columns; col++)
            {
                int wpx = col < tb.ColumnWidths.Count ? (int)tb.ColumnWidths[col] : 100;
                x += wpx * 15;
                _body.Append($@"\cellx{x}");
            }
            for (int col = 0; col < tb.Columns; col++)
            {
                _body.Append(@"\pard\intbl ");
                var cell = tb.Cells[row][col];
                foreach (var inline in cell.Inlines)
                    if (inline is Run r && !string.IsNullOrEmpty(r.Text)) WriteRun(r, false, 0);
                _body.Append(@"\cell");
            }
            _body.Append(@"\row").Append('\n');
        }
        _body.Append(@"\pard").Append('\n');
    }

    // {\*\shppict{\pict ...}} — the modern wrapper our parser un-skips; bytes go out as hex, size in twips.
    private void WritePict(byte[] bytes, string? mime, double w, double h)
    {
        _body.Append(@"{\*\shppict{\pict");
        _body.Append(mime != null && mime.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ? @"\jpegblip" : @"\pngblip");
        if (w > 0) _body.Append($@"\picwgoal{(int)(w * 15)}");
        if (h > 0) _body.Append($@"\pichgoal{(int)(h * 15)}");
        _body.Append(' ');
        foreach (byte b in bytes) _body.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        _body.Append("}}");
    }

    private int FontIndex(string? family)
    {
        if (string.IsNullOrEmpty(family)) return 0;
        if (_fontIndex.TryGetValue(family, out var i)) return i;
        i = _fonts.Count;
        _fonts.Add(family);
        _fontIndex[family] = i;
        return i;
    }

    private int ColorIndex(IBrush? brush)
    {
        if (brush is not ISolidColorBrush s) return 0;
        var col = s.Color;
        // Black is the default text colour — no \cf needed (keeps the output clean and matches the model
        // default where a null foreground also renders black).
        if (col.R == 0 && col.G == 0 && col.B == 0) return 0;
        uint key = ((uint)col.R << 16) | ((uint)col.G << 8) | col.B;
        if (_colorIndex.TryGetValue(key, out var i)) return i;
        _colors.Add(col);
        i = _colors.Count; // 1-based: \colortbl entry 0 is the auto colour
        _colorIndex[key] = i;
        return i;
    }

    private static double HeadingSize(int level)
        => level switch { 1 => 24, 2 => 20, 3 => 16, 4 => 14, 5 => 13, 6 => 12, _ => 14 };

    private static bool HasDecoration(TextDecorationCollection? decos, TextDecorationLocation loc)
    {
        if (decos == null) return false;
        foreach (var d in decos) if (d.Location == loc) return true;
        return false;
    }

    private void WriteEscaped(string text) => _body.Append(EscapeText(text));

    // Escapes RTF specials and emits non-ASCII as \uN? (signed 16-bit, per UTF-16 code unit — surrogate
    // pairs come out as two \u, which readers recombine). Soft '\n' becomes \line.
    private static string EscapeText(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            if (ch == '\\' || ch == '{' || ch == '}') sb.Append('\\').Append(ch);
            else if (ch == '\n') sb.Append(@"\line ");
            else if (ch == '\r') { /* skip */ }
            else if (ch < 128) sb.Append(ch);
            else { int code = ch > 0x7FFF ? ch - 0x10000 : ch; sb.Append(@"\u").Append(code.ToString(CultureInfo.InvariantCulture)).Append('?'); }
        }
        return sb.ToString();
    }
}
