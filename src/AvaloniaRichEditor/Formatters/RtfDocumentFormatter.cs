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
            case "intbl": case "cellx": break; // structure is driven by \cell/\row; boundaries unused

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
        _para = new Paragraph(); // first cell's content
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
    }

    private void FinalizeTable()
    {
        var rows = _tableRows;
        _tableRows = null;
        _curRow = null;
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
