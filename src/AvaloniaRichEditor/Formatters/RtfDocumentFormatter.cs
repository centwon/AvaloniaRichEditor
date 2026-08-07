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
/// <para>Writing covers more than reading, because that is what other applications consume: merged cells
/// (<c>\clmgf</c>/<c>\clmrg</c>, <c>\clvmgf</c>/<c>\clvmrg</c>), per-cell shading (<c>\clcbpat</c>),
/// everything a cell holds (several paragraphs, images, dividers, list markers), and tables nested in a
/// cell (<c>\nestcell</c>/<c>\nestrow</c>, read back as a real nested table). Reading back is still
/// lossier in places: cell merge flags and shading are ignored, and a nested table's column widths come
/// out at the default because they live in the ignorable <c>{\*\nesttableprops}</c> group. An
/// <see cref="InlineTable"/> has no RTF equivalent, so it is written as a
/// block-level table that splits its host paragraph — the content and its order survive, the in-line
/// placement does not (use <c>.flow</c>/JSON or HTML to keep that).</para>
/// </summary>
public static class RtfDocumentFormatter
{
    // The left gutter a list item at this nesting level gets, in twips. One place, because the writer adds
    // it to \li and the reader subtracts the same amount back out — if these two ever disagree, a list
    // item's indent grows or shrinks on every save.
    internal static int ListGutterTwips(int level) => 720 * (Math.Clamp(level, 0, 8) + 1);

    // Marker style <-> wire code for {\*\armkb|armkn}, spelled out in BOTH directions on purpose: a cast
    // would tie the RTF we write to the enum's declaration order, so inserting a style would silently
    // change the format.
    internal static int MarkerCode(ListMarkerStyle s) => s switch
    {
        ListMarkerStyle.Disc => 1,
        ListMarkerStyle.Circle => 2,
        ListMarkerStyle.Square => 3,
        ListMarkerStyle.Dash => 4,
        ListMarkerStyle.Decimal => 5,
        ListMarkerStyle.DecimalParen => 6,
        ListMarkerStyle.LowerAlpha => 7,
        ListMarkerStyle.UpperAlpha => 8,
        ListMarkerStyle.LowerRoman => 9,
        _ => 0,
    };

    internal static ListMarkerStyle MarkerFromCode(int c) => c switch
    {
        1 => ListMarkerStyle.Disc,
        2 => ListMarkerStyle.Circle,
        3 => ListMarkerStyle.Square,
        4 => ListMarkerStyle.Dash,
        5 => ListMarkerStyle.Decimal,
        6 => ListMarkerStyle.DecimalParen,
        7 => ListMarkerStyle.LowerAlpha,
        8 => ListMarkerStyle.UpperAlpha,
        9 => ListMarkerStyle.LowerRoman,
        _ => ListMarkerStyle.Default,
    };

    static RtfDocumentFormatter()
    {
        // CP949 (Korean), Shift-JIS, GB2312 etc. aren't in .NET's default set — register them so
        // \'hh runs from HWP/Word decode correctly.
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
        catch (Exception ex) { RichEditorDiagnostics.Report(ex); }
    }

    /// <summary>True if <paramref name="text"/> starts with the RTF signature.</summary>
    public static bool LooksLikeRtf(string? text)
        => text != null && text.TrimStart().StartsWith(@"{\rtf", StringComparison.Ordinal);

    /// <summary>Parses an RTF string into a <see cref="FlowDocument"/> (empty document on failure).
    /// <para>Failure is indistinguishable from a genuinely empty document here. Callers that would
    /// REPLACE open content with the result — loading a file, not pasting a fragment — must use
    /// <see cref="TryParse"/> instead, or a damaged file silently blanks the document and the next
    /// save writes that blank over the original.</para></summary>
    public static FlowDocument Parse(string rtf)
    {
        // Deliberately NOT TryParse: this path is for pasting a fragment, where whatever was readable is
        // better than nothing, and a truncated clipboard flavour should still contribute its text. The
        // strictness that protects an open document belongs only to TryParse.
        try { return new RtfParser(rtf).Run(); }
        catch (Exception ex) { RichEditorDiagnostics.Report(ex); return new FlowDocument(); }
    }

    /// <summary>Parses an RTF string, reporting whether it succeeded. Returns <see langword="false"/>
    /// only when the RTF is damaged badly enough to abort the parse; a well-formed but empty document
    /// is a success. On failure <paramref name="document"/> is an empty document (matching
    /// <see cref="Parse"/>) and <paramref name="error"/> describes the fault.
    /// <para>This is the safe entry point for anything that replaces open content — it is the RTF
    /// counterpart of the exception <see cref="DocumentSerializer.Deserialize"/> throws for damaged
    /// JSON. A parse that aborts part-way reports failure rather than returning what it had read so
    /// far: truncated content that LOOKS like a document is the outcome most likely to be saved over
    /// the original by a host that cannot tell it is incomplete.</para></summary>
    public static bool TryParse(string rtf, out FlowDocument document, out string? error)
    {
        try
        {
            var parser = new RtfParser(rtf);
            var parsed = parser.Run();
            // Truncation is the common damage — a half-copied file, a cut-short download — and it does
            // not throw: the reader just runs out of input and finalizes what it has. That looked like a
            // clean parse of a SHORTER document, so LoadRtf replaced the open one with it and the next
            // save wrote the shorter version over the original. Unclosed groups are the giveaway, and
            // until this check the only damage actually detected was input that ABORTS the parse (a
            // numeric overflow, say) — which is what the fixture in DamagedRtfTests happens to be.
            if (parser.UnclosedGroups > 0)
            {
                document = new FlowDocument();
                error = $"The RTF ends inside {parser.UnclosedGroups} unclosed group(s); the file is truncated.";
                return false;
            }
            document = parsed;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            // Also reported to RichEditorDiagnostics: Parse() discards `error`, so without this a paste
            // that fell back to plain text would be invisible to a host watching only the fault channel.
            RichEditorDiagnostics.Report(ex);
            document = new FlowDocument();
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
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

    private enum Dest { Normal, Skip, ColorTable, Pict, PageChrome }

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
    // into a TableBlock. A cell is a TableCell so it can hold blocks, not just its text paragraph:
    // Word writes a table inside a cell as nested rows, and those become real nested tables.
    // Intra-cell \par becomes a newline.
    private List<List<TableCell>>? _tableRows;
    private List<TableCell>? _curRow;
    // Nested tables, keyed by RTF nesting depth (\itap): 2 = a table inside a cell, 3 = one deeper, and
    // so on. `_nestRows[d]` holds the finished rows at that depth and `_nestRow[d]` the row being filled;
    // both are consumed when the cell one level up closes. Depth comes from \itap, which is how Word
    // tells the levels apart — every \nestcell looks the same otherwise.
    private readonly Dictionary<int, List<List<TableCell>>> _nestRows = new();
    private readonly Dictionary<int, List<TableCell>> _nestRow = new();
    private int _itap = 1;
    // Paragraphs already closed in the cell being filled, keyed by the depth that cell belongs to: text
    // that preceded a nested table stays with ITS cell instead of being taken by the deeper one.
    private readonly Dictionary<int, List<Block>> _cellPending = new();
    // \cellx<N> = cumulative right boundary (twips) per column. Captured from the first row so the
    // pasted table keeps the source column widths instead of a uniform default.
    private List<int> _curCellx = new();
    private List<int>? _tableCellx;

    // Per-cell properties from the row definition. In RTF these precede each \cellx, one group per
    // column, so they accumulate here and commit when that column's boundary arrives. Merged cells
    // still occupy a column each (with their own \cellx and \cell), so the imported grid has the same
    // shape and only the spans have to be reconstructed.
    private struct CellProps
    {
        public bool HMergeFirst;  // \clmgf — this cell starts a horizontal merge
        public bool HMergeCont;   // \clmrg — it continues the one to its left
        public bool VMergeFirst;  // \clvmgf — starts a vertical merge
        public bool VMergeCont;   // \clvmrg — continues the one above
        public int Shading;       // \clcbpat<N> — colour-table index, 0 = none
    }
    private CellProps _pendingCell;             // accumulating until the next \cellx
    private List<CellProps> _curCellProps = new();
    private List<List<CellProps>>? _tableCellProps;

    // Set by our own {\*\arinline} marker: the next top-level table was an InlineTable and belongs back
    // on the preceding paragraph's text line. A parser field, not part of the pushed/popped group state,
    // because the marker group closes before the table it describes begins.
    private bool _nextTableInline;

    public RtfParser(string s) => _s = s;

    public FlowDocument Run()
    {
        while (_i < _s.Length)
        {
            char c = _s[_i];
            // Commit the text collected so far BEFORE descending into a group. A group can switch to a
            // destination we skip ({\*\nesttableprops …}, bookmarks, fields — all normal in Word output),
            // and the closing brace's FlushRun then runs with that destination still active and throws the
            // pending run away. Word documents lost the text preceding any such group.
            if (c == '{') { if (_st.Dest == Dest.Normal) FlushRun(); _stack.Push(_st); _i++; }
            else if (c == '}')
            {
                if (_st.Dest == Dest.Pict) FinalizePict();
                else if (_st.Dest == Dest.PageChrome && _stack.Count == _chromeDepth) FinalizePageChrome();
                else FlushRun();
                _st = _stack.Count > 0 ? _stack.Pop() : _st; _i++;
            }
            else if (c == '\\') ReadControl();
            else if (c == '\r' || c == '\n') _i++;            // RTF line breaks are not content
            else if (_st.Dest == Dest.ColorTable && c == ';') { CloseColorEntry(); _i++; }
            else if (_st.Dest == Dest.Pict) { if (Uri.IsHexDigit(c)) _pictHex.Append(c); _i++; }
            // Page chrome collects plain characters too — its text is ordinary text, just bound for the
            // header/footer band instead of the body.
            else { if (_st.Dest is Dest.Normal or Dest.PageChrome) AppendByte(c); _i++; }
        }
        EndRow();        // a table that ran to the document end (no trailing normal paragraph)
        FlushRun();
        FinalizeTable();
        if (_para.Inlines.Count > 0) _doc.Blocks.Add(_para);
        if (_doc.Blocks.Count == 0) _doc.Blocks.Add(new Paragraph());
        // RTF is brace-balanced, so groups still open here mean the input ENDED early. Nothing above
        // notices: the loop simply runs out of characters and every partial structure is finalized as
        // though it had been closed properly, which is why a truncated file looks like a clean parse.
        // See TryParse — a truncated file must not be allowed to replace an open document.
        UnclosedGroups = _stack.Count;
        return _doc;
    }

    /// How many groups were still open when the input ran out. Non-zero means truncated.
    public int UnclosedGroups { get; private set; }

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
            case "par": case "sect":
                _skipMarkerText = false;
                // Inside a header/footer a \par separates lines of a band this model stores as ONE line —
                // it must not reach EndParagraph, which would append a paragraph to the DOCUMENT.
                if (_st.Dest == Dest.PageChrome) { FlushRun(); _chromeText.Append(' '); break; }
                EndParagraph();
                break;
            case "line": if (_st.Dest == Dest.Normal) _bytes.Add(10); break;
            // A \tab also TERMINATES a list marker's text (see armkb/armkn): that one is structure, so
            // it is consumed rather than emitted. Every other \tab is a real tab.
            case "tab":
                if (_skipMarkerText) { _skipMarkerText = false; break; }
                if (_st.Dest == Dest.Normal) _bytes.Add(9);
                break;
            case "pard":
                _skipMarkerText = false;   // a marker's text never spans a paragraph reset
                _paraBottomBorder = false; // a border is paragraph formatting, so the reset clears it
                SetItap(1); break;         // paragraph-property reset; \itap is one of those properties
            // A horizontal rule has no control word of its own in RTF; Word — and this writer — spell it
            // as an empty paragraph carrying a bottom border. Only the writing half existed, so every
            // divider came back as a blank line and was gone for good after one save/load.
            case "brdrb": if (_st.Dest == Dest.Normal && _curRow == null) _paraBottomBorder = true; break;

            // Ours (see WriteListMarker): the list nesting level, announced before the marker tag. It
            // restores ListLevel — RTF has no standard place for it — and says how much gutter the \li
            // above carried, so it can be taken back out of the author's own indent.
            case "arlvl":
                _para.ListLevel = Math.Clamp(p ?? 0, 0, 8);
                _pendingListGutter = RtfDocumentFormatter.ListGutterTwips(_para.ListLevel);
                break;
            // The list kind + exact marker style, and a signal that the text up to the next \tab is the
            // MARKER rather than content. Every other reader skips the ignorable group and renders that
            // text, which is the point — it is the only spelling both Word and HWP show.
            case "armkb": _para.ListType = ListKind.Bullet; StartMarkerText(p); break;
            case "armkn": _para.ListType = ListKind.Ordered; StartMarkerText(p); break;

            // tables. Every one of these is guarded by the destination: Word writes a nested table's row
            // definition inside the ignorable group {\*\nesttableprops \trowd …\nestrow}, and acting on
            // control words we are supposed to be skipping started a fresh row mid-cell — which threw
            // away the text the parent cell had accumulated so far.
            case "trowd": if (_st.Dest == Dest.Normal) StartRow(); break;
            case "cell": if (_st.Dest == Dest.Normal) EndCell(); break;
            case "row": if (_st.Dest == Dest.Normal) EndRow(); break;
            case "intbl": break;                  // structure is driven by \cell/\row
            // Column boundary: commits this column's width AND the cell properties collected for it.
            case "cellx":
                if (_st.Dest == Dest.Normal)
                {
                    _curCellx.Add(p ?? 0);
                    _curCellProps.Add(_pendingCell);
                    _pendingCell = default;
                }
                break;

            // Cell merge + shading. The writer has always emitted these (Word and HWP honour them), but
            // reading them back was skipped, so our own export came home with the grid un-merged and the
            // cell colours gone — a round trip through our own format lost more than Word's did.
            case "clmgf": if (_st.Dest == Dest.Normal) _pendingCell.HMergeFirst = true; break;
            case "clmrg": if (_st.Dest == Dest.Normal) _pendingCell.HMergeCont = true; break;
            case "clvmgf": if (_st.Dest == Dest.Normal) _pendingCell.VMergeFirst = true; break;
            case "clvmrg": if (_st.Dest == Dest.Normal) _pendingCell.VMergeCont = true; break;
            case "clcbpat": if (_st.Dest == Dest.Normal) _pendingCell.Shading = p ?? 0; break;

            // A table inside a cell: the model nests (milestone A) and the writer emits these, so they
            // come back as a real nested TableBlock in the parent cell rather than flattened text.
            case "itap": SetItap(p ?? 1); break;
            case "nestcell": if (_st.Dest == Dest.Normal) EndNestedCell(); break;
            case "nestrow": EndNestedRow(); break; // see EndNestedRow: intentionally not destination-gated
            // The fallback copy of a nested table, for readers that can't nest. We can, so skip it —
            // otherwise its \par landed as a stray line break in the parent cell.
            case "nonesttables": _st.Dest = Dest.Skip; break;
            // Our own inline-table marker (see WriteParagraph). It arrives as {\*\arinline}, so \* has
            // already switched this group to Skip — the group is empty and only the flag matters.
            case "arinline": _nextTableInline = true; break;

            // Text boxes / shapes (HWP 글상자): the editor has no floating frame, so pull out the
            // \shptxt content as normal text and skip the shape's property name/value groups (\sp/\sn/\sv).
            case "shptxt": _st.Dest = Dest.Normal; break;
            case "sp": case "sn": case "sv": _st.Dest = Dest.Skip; break;

            // unicode
            case "u": EmitUnicode(p ?? 0); break;
            case "uc": _st.UnicodeSkip = p ?? 1; break;

            // destinations
            case "colortbl": _st.Dest = Dest.ColorTable; _colors.Clear(); _ctR = _ctG = _ctB = 0; _ctHasColor = false; break;
            // {\header …}/{\footer …} and their left/right/first-page variants. Until this existed they were
            // UNKNOWN destinations, so their text flowed straight into the body: opening a Word document
            // with a header put that header in as the document's first paragraph.
            case "header": case "headerl": case "headerr": case "headerf":
                StartPageChrome(header: true); break;
            case "footer": case "footerl": case "footerr": case "footerf":
                StartPageChrome(header: false); break;
            // The page-number placeholders. Inside the chrome they mean "this document wants page numbers"
            // and contribute no text; everything after one is that band's DECORATION, not content — our own
            // writer follows \chpgn with " / " and a NUMPAGES field, and collecting that made the separator
            // ACCUMULATE ("footer/" then "footer//" on the next save).
            case "chpgn": case "pgnstart":
                if (_st.Dest == Dest.PageChrome) { FlushRun(); _chromeNumbers = true; _chromeTextClosed = true; }
                break;

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
        // _skipMarkerText matters HERE too, and this is the path that carries it: a non-ASCII bullet goes
        // out as \uN, so gating only the byte path swallowed "1." and let "•" through.
        if ((_st.Dest == Dest.Normal || _st.Dest == Dest.PageChrome) && !_skipMarkerText)
        {
            if (code < 0) code += 65536; // RTF \u is signed 16-bit
            // \u carries one UTF-16 code UNIT (not a scalar): astral chars arrive as two \u (a surrogate
            // pair), so append the raw unit — consecutive halves recombine in the buffer. ConvertFromUtf32
            // would throw on a lone surrogate and drop emoji etc.
            if (code >= 0 && code <= 0xFFFF) _run.Append((char)code);
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

    // True from a {\*\armkb|armkn} tag until the \tab that closes the list marker's literal text, which
    // this reader must NOT take as content (other readers render it — that is why it is written at all).
    private bool _skipMarkerText;

    // The gutter the writer folded into \li for the level last announced by {\*rlvl}.
    private int _pendingListGutter;

    // Also carries the exact marker style, and takes the list gutter back out of the indent: the writer
    // adds it to \li so other readers lay the item out.
    private void StartMarkerText(int? code)
    {
        if (code is { } c) _para.ListMarker = RtfDocumentFormatter.MarkerFromCode(c);
        if (_pendingListGutter > 0)
        {
            _para.Indent = Math.Max(0, _para.Indent - _pendingListGutter / 15.0);
            _pendingListGutter = 0;
        }
        _skipMarkerText = true;
    }

    private void AppendByte(char c)
    {
        if ((_st.Dest != Dest.Normal && _st.Dest != Dest.PageChrome) || _skipMarkerText) return;
        if (c < 256) _bytes.Add((byte)c);
        else { FlushBytes(); _run.Append(c); }
    }

    private void FlushBytes()
    {
        if (_bytes.Count == 0) return;
        _run.Append(Enc.GetString(_bytes.ToArray()));
        _bytes.Clear();
    }

    // ---- page chrome ({\header …} / {\footer …}) ----

    private bool _chromeIsHeader;      // which of the two the current group is
    private int _chromeDepth = -1;     // group depth that opened it, so the matching '}' is identifiable
    private bool _chromeNumbers;       // a \chpgn / page field was seen inside a footer
    private bool _chromeTextClosed;    // past the page-number placeholder: the rest is decoration
    private readonly StringBuilder _chromeText = new();

    private void StartPageChrome(bool header)
    {
        _st.Dest = Dest.PageChrome;
        _chromeIsHeader = header;
        _chromeDepth = _stack.Count;
        _chromeText.Clear();
        _chromeTextClosed = false;
    }

    // Takes the text the chrome group accumulated and puts it on the document's PageSetup instead of into
    // the body. The model holds ONE line per band, so a multi-paragraph header collapses to its text with
    // single spaces — lossy, and honestly so: this reader has nowhere richer to put it.
    private void FinalizePageChrome()
    {
        FlushRun(); // banks whatever the last line collected

        string text = System.Text.RegularExpressions.Regex.Replace(_chromeText.ToString(), @"\s+", " ").Trim();
        _chromeText.Clear();
        _chromeDepth = -1;
        _chromeTextClosed = false;

        if (text.Length == 0 && !_chromeNumbers) return;
        _doc.PageSetup ??= new PageSetup();
        if (_chromeIsHeader)
        {
            if (text.Length > 0 && string.IsNullOrEmpty(_doc.PageSetup.Header)) _doc.PageSetup.Header = text;
        }
        else
        {
            if (text.Length > 0 && string.IsNullOrEmpty(_doc.PageSetup.Footer)) _doc.PageSetup.Footer = text;
            if (_chromeNumbers) _doc.PageSetup.ShowPageNumbers = true;
        }
        _chromeNumbers = false;
    }

    private void FlushRun()
    {
        FlushBytes();
        if (_run.Length == 0) return;
        // Page chrome banks its text instead of discarding it. This is the ONE place that has to know,
        // because FlushRun is called from everywhere — every group close, every font/bold change — and a
        // footer with a page-number field hit exactly that: the field's closing brace ran FlushRun with the
        // chrome destination still active and the footer text vanished.
        if (_st.Dest == Dest.PageChrome)
        {
            if (!_chromeTextClosed) _chromeText.Append(_run);
            _run.Clear();
            return;
        }
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
            FontSize = _st.FontSize > 0 ? _st.FontSize : 10, // pt; body default
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

        // RTF has no block picture: the writer emits one as `\pard <pict>\par`, so that \par TERMINATES
        // the image's own paragraph rather than starting a new one. Reading it as content added a blank
        // paragraph after every image — and another on the next cycle, and the next, so a document saved
        // and reopened a few times grew a gap under each picture.
        //
        // A blank line the author really did put under an image still survives: the writer emits it as
        // its OWN `\pard\par`, so the first \par is consumed here and the second one lands as usual.
        bool structural = _imageOwnsNextPar && _para.Inlines.Count == 0;
        _imageOwnsNextPar = false;
        if (structural) return;

        // An empty paragraph carrying a bottom border is a horizontal rule (see the \brdrb case).
        if (_paraBottomBorder && _para.Inlines.Count == 0)
        {
            _paraBottomBorder = false;
            _doc.Blocks.Add(new DividerBlock());
            _para = new Paragraph();
            return;
        }
        _paraBottomBorder = false;
        _doc.Blocks.Add(_para);
        _para = new Paragraph();
    }

    // True immediately after a block picture was added, while its terminating \par is still pending.
    private bool _imageOwnsNextPar;
    // True while the paragraph being read carries a bottom border (\brdrb) — see EndParagraph.
    private bool _paraBottomBorder;

    // ---- tables ----

    private void StartRow()
    {
        _tableRows ??= new List<List<TableCell>>();
        _curRow ??= new List<TableCell>();
        _curCellx = new List<int>(); // \cellx for this row follows \trowd
        _curCellProps = new List<CellProps>();
        _pendingCell = default;
        _para = new Paragraph();     // first cell's content
    }

    private void EndCell()
    {
        if (_curRow == null) StartRow();
        int col = _curRow!.Count;
        var cell = TakeCell(childDepth: 2);
        // The row definition precedes the cells' content, so this column's shading is already known.
        if (col < _curCellProps.Count && _curCellProps[col].Shading is int ci && ci > 0 && ci < _colors.Count)
        {
            var bg = _colors[ci];
            if (bg.A != 0) cell.Background = new ImmutableSolidColorBrush(bg);
        }
        _curRow.Add(cell);
    }

    // \nestcell ends one cell of the nested row at the current depth. That cell may itself contain the
    // table one level deeper, which is why it goes through the same builder as a top-level cell.
    private void EndNestedCell()
    {
        int depth = Math.Max(2, _itap);
        if (!_nestRow.TryGetValue(depth, out var row)) _nestRow[depth] = row = new List<TableCell>();
        row.Add(TakeCell(depth + 1));
    }

    // \nestrow ends the nested row at the current depth. It arrives inside {\*\nesttableprops …}, which
    // is an ignorable destination, so this one is deliberately NOT gated on the destination — a reader
    // that supports nesting has to act on it there. The row's \cellx widths are in that same group and
    // are not read, so a nested table comes back at the default column width.
    private void EndNestedRow()
    {
        int depth = Math.Max(2, _itap);
        if (!_nestRow.TryGetValue(depth, out var row) || row.Count == 0) return;
        if (!_nestRows.TryGetValue(depth, out var rows)) _nestRows[depth] = rows = new List<List<TableCell>>();
        rows.Add(row);
        _nestRow.Remove(depth);
    }

    // The cell that just ended: the paragraphs it collected, the table nested one level deeper (if any),
    // and the paragraph currently being filled — in the order they appeared.
    private TableCell TakeCell(int childDepth)
    {
        FlushRun();
        var cell = new TableCell(_para);
        _para = new Paragraph();

        // A nested row left open (no \nestrow seen, e.g. truncated input) still counts.
        int saved = _itap;
        _itap = childDepth;
        EndNestedRow();
        _itap = saved;

        int at = 0;
        if (_cellPending.TryGetValue(childDepth - 1, out var pending))
        {
            _cellPending.Remove(childDepth - 1);
            foreach (var b in pending) cell.Blocks.Insert(at++, b);
        }
        if (_nestRows.TryGetValue(childDepth, out var rows))
        {
            _nestRows.Remove(childDepth);
            if (BuildTable(rows, null) is { } inner) cell.Blocks.Insert(at, inner);
        }
        return cell;
    }

    // \itap<N> switches nesting depth. Going deeper closes the text collected so far as a paragraph of
    // the cell being filled, so "text, then a nested table" keeps that order instead of the text being
    // swallowed into the nested table's first cell.
    private void SetItap(int depth)
    {
        if (depth == _itap) return;
        if (depth > _itap)
        {
            FlushRun();
            if (_para.Inlines.Count > 0)
            {
                // The paragraph belongs to the cell being filled at the CURRENT depth, not the deeper one.
                if (!_cellPending.TryGetValue(_itap, out var pending))
                    _cellPending[_itap] = pending = new List<Block>();
                pending.Add(_para);
                _para = new Paragraph();
            }
        }
        _itap = depth;
    }

    private void EndRow()
    {
        if (_curRow == null) return;
        _tableRows ??= new List<List<TableCell>>();
        _tableRows.Add(_curRow);
        _curRow = null;
        (_tableCellProps ??= new List<List<CellProps>>()).Add(_curCellProps);
        if (_tableCellx == null && _curCellx.Count > 0) _tableCellx = _curCellx; // keep the first row's columns
    }

    private void FinalizeTable()
    {
        var rows = _tableRows;
        var cellx = _tableCellx;
        var props = _tableCellProps;
        _tableRows = null;
        _curRow = null;
        _tableCellx = null;
        _tableCellProps = null;
        bool inline = _nextTableInline;
        _nextTableInline = false;
        if (BuildTable(rows, cellx) is not { } table) return;
        ApplyMerges(table, props);

        // Marked as an inline table by our own writer: put it back on the preceding paragraph's line
        // and continue that paragraph, so "text, table, more text" is one line again instead of three
        // blocks. The host paragraph was closed by the \par the writer emits before the table.
        if (inline && _doc.Blocks.Count > 0 && _doc.Blocks[_doc.Blocks.Count - 1] is Paragraph host)
        {
            _doc.Blocks.RemoveAt(_doc.Blocks.Count - 1);
            var it = new InlineTable { Table = table };
            it.Parent = host;
            host.Inlines.Add(it);
            // Whatever the current paragraph has collected is the text that followed the table.
            foreach (var inl in new List<Inline>(_para.Inlines))
            {
                _para.Inlines.Remove(inl);
                inl.Parent = host;
                host.Inlines.Add(inl);
            }
            _para = host; // the caller adds it back
            return;
        }
        _doc.Blocks.Add(table);
    }

    // Rebuilds the merge spans from the row definitions' \clmgf/\clmrg and \clvmgf/\clvmrg flags.
    // A merged region is an anchor cell followed by continuation cells to its right and/or below;
    // MergeCells folds the (empty) covered cells into it and stamps the spans.
    private static void ApplyMerges(TableBlock tb, List<List<CellProps>>? props)
    {
        if (props == null) return;
        for (int r = 0; r < tb.Rows && r < props.Count; r++)
        {
            var row = props[r];
            for (int c = 0; c < tb.Columns && c < row.Count; c++)
            {
                if (!row[c].HMergeFirst && !row[c].VMergeFirst) continue;

                int c1 = c;
                if (row[c].HMergeFirst)
                    while (c1 + 1 < tb.Columns && c1 + 1 < row.Count && row[c1 + 1].HMergeCont) c1++;

                int r1 = r;
                if (row[c].VMergeFirst)
                    while (r1 + 1 < tb.Rows && r1 + 1 < props.Count
                           && c < props[r1 + 1].Count && props[r1 + 1][c].VMergeCont) r1++;

                if (r1 > r || c1 > c) tb.MergeCells(r, c, r1, c1);
            }
        }
    }

    // Rows of cells -> a TableBlock, padding short rows. Shared by the top-level table and the nested
    // ones, so both get the same shape (spans reset to 1, widths from \cellx when available).
    private static TableBlock? BuildTable(List<List<TableCell>>? rows, List<int>? cellx)
    {
        if (rows == null || rows.Count == 0) return null;

        int cols = 0;
        foreach (var r in rows) if (r.Count > cols) cols = r.Count;
        if (cols == 0) return null;

        var tb = new TableBlock(rows.Count, cols);
        tb.Cells.Clear();
        foreach (var r in rows)
        {
            var cells = new List<TableCell>(cols);
            for (int c = 0; c < cols; c++) cells.Add(c < r.Count ? r[c] : new TableCell());
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
        return tb;
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
            // not a decodable PNG/JPEG after all
            catch (Exception ex) { RichEditorDiagnostics.Report(ex); return; }
            w = bmp.Size.Width; h = bmp.Size.Height;
        }
        string mime = ImageMime.Detect(bytes) ?? _pictMime;

        // A picture inside a table row belongs to the cell being built. Splicing it out as a block
        // would push the cell's half-built paragraph into the document body — a photo in a Word/HWP
        // table came out beside the table, with the surrounding text reordered. Keep it inline.
        if ((w < 64 && h < 64) || _curRow != null)
        {
            var img = new InlineImage { Width = w, Height = h };
            img.SetImageData(bytes, mime, bmp);
            _para.Inlines.Add(img);
        }
        else
        {
            // A table is not appended to the document until FinalizeTable runs, and until then it is
            // only pending rows — so a picture that follows `\row` would be added FIRST and end up
            // ahead of the table it came after. Every other block append goes through EndParagraph,
            // which finalizes; this one has to do the same.
            FinalizeTable();
            if (_para.Inlines.Count > 0) { _doc.Blocks.Add(_para); _para = new Paragraph(); }
            var ib = new ImageBlock { Width = w, Height = h };
            ib.SetImageData(bytes, mime, bmp);
            _doc.Blocks.Add(ib);
            _imageOwnsNextPar = true;
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
        catch (Exception ex) { RichEditorDiagnostics.Report(ex); return Encoding.Latin1; }
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
        // The header/footer destinations belong to the document area, BEFORE the body — that is where Word
        // puts them and where readers look. Nothing wrote them, so a document with a header exported to
        // Word or HWP simply lost it while the model and .flow carried it correctly.
        WritePageChrome(sb, doc.PageSetup);
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

    // {\header …} / {\footer …} — the page chrome the editor draws in the margin bands.
    //
    // The footer carries the page number when the document asks for it, at a RIGHT TAB STOP on the content
    // edge, because that is how the editor draws it (footer text left, "3 / 12" right). Without the
    // explicit stop the number lands on whatever default tab the reader happens to have.
    private void WritePageChrome(StringBuilder sb, PageSetup? ps)
    {
        if (ps == null) return;
        bool hasHeader = !string.IsNullOrEmpty(ps.Header);
        bool hasFooter = !string.IsNullOrEmpty(ps.Footer) || ps.ShowPageNumbers;
        if (!hasHeader && !hasFooter) return;

        // The writer emits into _body; borrow it so WriteEscaped can be reused, then move the result.
        int mark = _body.Length;

        if (hasHeader)
        {
            _body.Append(@"{\header\pard\plain\ql ");
            WriteEscaped(ps.Header!);
            _body.Append(@"\par}").Append('\n');
        }
        if (hasFooter)
        {
            var (w, _) = PageSetup.PaperDips(ps.PageSize, ps.Orientation);
            int contentTwips = (int)Math.Round((w - 2 * PageSetup.MarginX) * 15);
            _body.Append(@"{\footer\pard\plain\ql");
            if (ps.ShowPageNumbers) _body.Append(@"\tqr\tx").Append(contentTwips);
            _body.Append(' ');
            if (!string.IsNullOrEmpty(ps.Footer)) WriteEscaped(ps.Footer!);
            // \chpgn is the current page; the total needs a field, which is what Word writes. This parser
            // reads both as "the document wanted page numbers" rather than as text.
            if (ps.ShowPageNumbers) _body.Append(@"\tab \chpgn / {\field{\*\fldinst NUMPAGES}{\fldrslt }}");
            _body.Append(@"\par}").Append('\n');
        }

        sb.Append(_body, mark, _body.Length - mark);
        _body.Length = mark;
    }

    // "\pard" + this paragraph's own properties.
    private void WriteParagraphProps(Paragraph p)
    {
        _body.Append(@"\pard");
        WriteParagraphPropsBody(p); // ends with the delimiter space for the last control word
    }

    // The properties themselves, without the \pard. Split out because a CELL paragraph opens with
    // `\pard\intbl` and never followed it with any of these — so a centred or indented paragraph inside a
    // table exported as neither, while the identical paragraph at the top level exported correctly. Not a
    // limitation of RTF: the top-level path has always written them, and the cell path simply never did.
    private void WriteParagraphPropsBody(Paragraph p)
    {
        // ALWAYS emit the alignment, including \ql for left. In the spec \pard resets alignment to left,
        // but HWP treats \pard as "back to the current defaults" and keeps a previously seen \qr — so a
        // single right-aligned paragraph turned every following one right-aligned on paste. Being explicit
        // costs 3 bytes per paragraph and removes the reader-dependent behaviour entirely.
        switch (p.TextAlignment)
        {
            case TextAlignment.Center: _body.Append(@"\qc"); break;
            case TextAlignment.Right: _body.Append(@"\qr"); break;
            case TextAlignment.Justify: _body.Append(@"\qj"); break;
            default: _body.Append(@"\ql"); break;
        }
        // A list item needs a real hanging indent, not just a marker followed by \tab. Without one the tab
        // lands on the reader's next DEFAULT tab stop — in HWP that is far to the right, so the text was
        // thrown across the line while the marker sat alone at the margin. \fi-360 hangs the marker, \li
        // puts the text at the gutter, \tx pins the tab there. This is what Word writes for its own lists.
        int indentTwips = (int)(p.Indent * 15);
        if (p.IsListItem)
        {
            int gutter = RtfDocumentFormatter.ListGutterTwips(p.ListLevel);
            _body.Append($@"\fi-360\li{indentTwips + gutter}\tx{gutter}");
        }
        else if (indentTwips > 0) _body.Append($@"\li{indentTwips}");
        _body.Append(' ');
    }

    // A list item's marker. It used to go out as BARE TEXT followed by \tab — the comment here called that
    // a deliberate trade-off ("our parser treats it as text"), but the cost was not only cosmetic: the
    // glyph became part of the document's CONTENT on the way back in. A bulleted item reopened as the
    // plain text "•\t항목", list gone, bullet now part of what the user typed, and saving again kept it.
    //
    // No round-trip test could see it, because the result is PERFECTLY IDEMPOTENT: cycle 2 reads back
    // exactly what cycle 1 produced, since the marker is only written for a paragraph that still has a
    // ListType and this one no longer has one.
    //
    // The fix keeps the literal text — that is what every other reader renders, and the standard
    // {\pntext}{\*\pn} pair is NOT a substitute: HWP skips both and then shows no marker at all (measured
    // on the WinUI peer against a real HWP paste). Instead an ignorable tag in front of it says "the text
    // up to the next \tab is the marker, not content", which only this reader acts on. The tag's parameter
    // carries the exact marker style, and {\*\arlvl} carries the nesting level — so ListLevel, which RTF
    // has no standard place for, survives too.
    private void WriteListMarker(Paragraph p, int ordered)
    {
        _body.Append(@"{\*\arlvl").Append(Math.Clamp(p.ListLevel, 0, 8)).Append('}');
        _body.Append(p.ListType == ListKind.Bullet ? @"{\*\armkb" : @"{\*\armkn")
             .Append(RtfDocumentFormatter.MarkerCode(p.ListMarker)).Append('}');
        WriteEscaped(Controls.RichEditor.ListMarkerText(p.ListType, p.ListMarker, ordered));
        _body.Append(@"\tab ");
    }

    private void WriteParagraph(Paragraph p, int ordered)
    {
        WriteParagraphProps(p);

        if (p.ListType != ListKind.None) WriteListMarker(p, ordered);

        bool heading = p.HeadingLevel is >= 1 and <= 6;
        double headingSize = heading ? HeadingSize(p.HeadingLevel) : 0;
        foreach (var inline in p.Inlines)
        {
            // RTF has no inline table, so one splits its host paragraph: the text before it, then the
            // table as a block-level one, then the rest as a fresh paragraph. Word/HWP show the same
            // content in the same order; only the "inside the line" placement is lost (our own .flow and
            // HTML keep it).
            if (inline is InlineTable it)
            {
                _body.Append(@"\par").Append('\n');
                // Ours, and deliberately an ignorable destination: RTF has no inline table, so the
                // grid still goes out as a block-level table that every other reader shows exactly as
                // before ({\*\...} groups are skipped by definition). Our own reader sees the marker
                // and puts the table back on the text line, so RTF joins .flow/JSON/HTML in
                // round-tripping an inline table through our own save/load.
                _body.Append(@"{\*\arinline}");
                WriteTable(it.Table);
                _body.Append(@"\pard ");
                continue;
            }
            WriteInline(inline, heading, headingSize);
        }
        _body.Append(@"\par").Append('\n');
    }

    private void WriteInline(Inline inline, bool heading, double headingSize)
    {
        if (inline is Run r && !string.IsNullOrEmpty(r.Text)) WriteRun(r, heading, headingSize);
        else if (inline is InlineImage img && img.RawBytes != null) WritePict(img.RawBytes, img.MimeType, img.Width, img.Height);
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
        double size = r.FontSize <= 0 ? 10 : r.FontSize; // pt; body default
        if (heading && (r.FontSize <= 0 || Math.Abs(r.FontSize - 10) < 0.01)) size = headingSize;
        _body.Append($@"\fs{(int)Math.Round(size * 2)}"); // \fs is half-points; model size is already pt
        int c = ColorIndex(r.Foreground);
        if (c > 0) _body.Append($@"\cf{c}");
        _body.Append(' ');
        WriteEscaped(r.Text!);
        _body.Append('}');
    }

    // `depth` is the RTF table nesting level (\itap): 1 for a top-level table, 2+ for a table inside a
    // cell, which RTF writes with \nestcell/\nestrow instead of \cell/\row.
    private void WriteTable(TableBlock tb, int depth = 1)
    {
        for (int row = 0; row < tb.Rows; row++)
        {
            var rowDef = new StringBuilder();
            rowDef.Append(@"\trowd");
            if (depth > 1) rowDef.Append($@"\itap{depth}");
            // Cumulative right cell boundaries in twips (px*15), from the column widths. Each boundary is
            // preceded by that cell's own properties: merge flags and shading.
            int x = 0;
            for (int col = 0; col < tb.Columns; col++)
            {
                var (cs, rs) = tb.SpanOf(row, col);
                bool covered = tb.IsCovered(row, col);
                var (ar, ac) = tb.AnchorOf(row, col);
                // Horizontal merge: the anchor opens the range, the columns it covers continue it.
                if (cs > 1 && !covered) rowDef.Append(@"\clmgf");
                else if (covered && ar == row) rowDef.Append(@"\clmrg");
                // Vertical merge: same, down the rows.
                if (rs > 1 && !covered) rowDef.Append(@"\clvmgf");
                else if (covered && ar != row) rowDef.Append(@"\clvmrg");
                // Cell shading uses the colour table, like text colour.
                int bg = ColorIndex(tb.Cells[ar][ac].Background);
                if (bg > 0) rowDef.Append($@"\clcbpat{bg}");
                // Cell borders. Without these Word and HWP draw the grid with NO lines at all — the
                // table is there and selectable, but invisible, so an exported document does not look
                // like the one on screen. The editor draws a plain single border, so emit that.
                rowDef.Append(@"\clbrdrt\brdrs\brdrw10\clbrdrl\brdrs\brdrw10")
                      .Append(@"\clbrdrb\brdrs\brdrw10\clbrdrr\brdrs\brdrw10");

                int wpx = col < tb.ColumnWidths.Count ? (int)tb.ColumnWidths[col] : 100;
                x += wpx * 15;
                rowDef.Append($@"\cellx{x}");
            }

            // A nested row's definition follows its cells, wrapped in an ignorable group; a top-level
            // row's precedes them.
            if (depth == 1) _body.Append(rowDef);
            for (int col = 0; col < tb.Columns; col++)
            {
                _body.Append(@"\pard\intbl");
                if (depth > 1) _body.Append($@"\itap{depth}");
                _body.Append(' ');
                // A nested table leaves \itap set to ITS depth, so the cell has to re-declare its own
                // before closing — otherwise the reader books this cell into the deeper table.
                if (WriteCellContent(tb.Cells[row][col], depth))
                {
                    _body.Append(@"\pard\intbl");
                    if (depth > 1) _body.Append($@"\itap{depth}");
                    _body.Append(' ');
                }
                _body.Append(depth == 1 ? @"\cell" : @"\nestcell");
            }
            if (depth == 1)
                _body.Append(@"\row").Append('\n');
            else
                // \nesttableprops is ignorable: a reader that doesn't do nested tables still sees the
                // cell text (ours flattens it into the parent cell), which is why this can't corrupt a
                // document. \nonesttables carries the same fallback for very old readers.
                _body.Append(@"{\*\nesttableprops").Append(rowDef).Append(@"\nestrow}{\nonesttables\par}").Append('\n');
        }
        if (depth == 1) _body.Append(@"\pard").Append('\n');
    }

    // Everything a cell can hold: several paragraphs (separated by \par), block images, dividers, and
    // tables — nested ones and the inline tables living in a cell paragraph, both written one \itap
    // deeper. Returns true when it wrote such a table, so the caller can re-declare this cell's depth.
    private bool WriteCellContent(TableCell cell, int depth)
    {
        bool first = true, wroteNested = false;

        // A nested table leaves \itap at ITS depth and consumes the paragraph properties. Anything this
        // cell writes afterwards has to re-open the cell's own level first, or Word books that text into
        // the deeper table and drops it (a paragraph after a nested table vanished entirely).
        void ReopenCell()
        {
            _body.Append(@"\pard\intbl");
            if (depth > 1) _body.Append($@"\itap{depth}");
            _body.Append(' ');
            // A fresh paragraph is now open at this cell's level, so the next block writes straight
            // into it rather than prefixing another \par (which would leave a blank line).
            first = true;
        }
        // Terminate whatever this cell has written so far before descending into a nested table.
        // Without it the preceding paragraph's text is not closed and Word glues it onto the first
        // nested cell ("...형제 문단)중첩1").
        void CloseBeforeNested()
        {
            if (!first) _body.Append(@"\par ");
            first = false;
        }

        foreach (var blk in cell.Blocks)
        {
            if (blk is Paragraph cpara)
            {
                if (!first) _body.Append(@"\par ");
                first = false;
                // This paragraph's OWN alignment and indent. The cell prelude opens with `\pard\intbl` and
                // wrote none of them, so every cell paragraph exported as left-aligned and un-indented no
                // matter what it was — while the identical paragraph at the top level exported correctly.
                WriteParagraphPropsBody(cpara);
                if (cpara.ListType != ListKind.None) WriteListMarker(cpara, 1);
                bool heading = cpara.HeadingLevel is >= 1 and <= 6;
                double headingSize = heading ? HeadingSize(cpara.HeadingLevel) : 0;
                foreach (var inline in cpara.Inlines)
                {
                    if (inline is InlineTable it)
                    {
                        CloseBeforeNested();
                        WriteTable(it.Table, depth + 1);
                        wroteNested = true;
                        ReopenCell(); // the rest of this paragraph belongs to THIS cell, not the inner table
                    }
                    else WriteInline(inline, heading, headingSize);
                }
            }
            else if (blk is TableBlock nested)
            {
                CloseBeforeNested();
                WriteTable(nested, depth + 1);
                wroteNested = true;
                ReopenCell();
            }
            else if (blk is ImageBlock cib && cib.RawBytes != null)
            {
                if (!first) _body.Append(@"\par ");
                first = false;
                WritePict(cib.RawBytes, cib.MimeType, cib.Width, cib.Height);
            }
            else if (blk is DividerBlock)
            {
                if (!first) _body.Append(@"\par ");
                first = false;
                _body.Append(@"\brdrb\brdrs\brdrw10\brsp20 ");
            }
        }
        return wroteNested;
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

    // Heading sizes in points (pt), mirroring RichEditor.HeadingFontSize.
    private static double HeadingSize(int level)
        => level switch { 1 => 20, 2 => 16, 3 => 14, 4 => 12, 5 => 11, 6 => 10, _ => 10 };

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
