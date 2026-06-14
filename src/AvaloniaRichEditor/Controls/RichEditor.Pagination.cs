using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// P-milestone Phase 1: pagination core. Computes where pages begin in the editor's continuous
// layout space. Pages never cut through an indivisible atom: one text line of a paragraph
// (paragraphs split at line boundaries via the cached TextLayout's line metrics), or a whole
// image/table/divider block. The walk mirrors MeasureContentHeight's advancement exactly (same
// widths, same per-block heights) so downstream consumers (page-view gap injection, page render
// for print/PDF) slice the very same geometry the editor renders — keeping core invariant 1
// (the single TextLayout is the source of truth) intact.
public partial class RichEditor
{
    // A4 at 96 DPI — the default paper and the print fallback for the Continuous mode.
    internal const double A4PageWidth = 794;
    internal const double A4PageHeight = 1123;

    // Paper margins (content box inset) and the grey-desk gap between consecutive pages — uniform
    // across paper sizes.
    internal const double PagePadX = 48;
    internal const double PagePadY = 40;
    internal const double PageGap = 24;
    internal const double A4ContentWidth = A4PageWidth - 2 * PagePadX;    // 698
    internal const double A4ContentHeight = A4PageHeight - 2 * PagePadY;  // 1043

    /// <summary>Paper size for the document. <see cref="RichEditorPageSize.Continuous"/> (no fixed
    /// paper) reflows the text column to the control width; any concrete size fixes the column to that
    /// paper's content width. Default <see cref="RichEditorPageSize.A4"/>.</summary>
    public static readonly StyledProperty<RichEditorPageSize> PageSizeProperty =
        AvaloniaProperty.Register<RichEditor, RichEditorPageSize>(nameof(PageSize), RichEditorPageSize.A4);

    /// <summary>Gets or sets the paper size. Default <see cref="RichEditorPageSize.A4"/>.</summary>
    public RichEditorPageSize PageSize
    {
        get => GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    /// <summary>For a concrete <see cref="PageSize"/>, whether to draw discrete pages on a grey desk
    /// (Word-style print layout). Off keeps the fixed-width column flowing continuously with no page
    /// chrome. Ignored when <see cref="PageSize"/> is <see cref="RichEditorPageSize.Continuous"/>. Default true.</summary>
    public static readonly StyledProperty<bool> ShowPageBoundariesProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(ShowPageBoundaries), true);

    /// <summary>Gets or sets whether page boundaries (desk, paper, inter-page gaps) are drawn for a
    /// concrete <see cref="PageSize"/>. Default true.</summary>
    public bool ShowPageBoundaries
    {
        get => GetValue(ShowPageBoundariesProperty);
        set => SetValue(ShowPageBoundariesProperty, value);
    }

    /// <summary>Page orientation. <see cref="RichEditorPageOrientation.Landscape"/> swaps the paper's
    /// width and height (editor view and print/PDF). No effect for <see cref="RichEditorPageSize.Continuous"/>
    /// on screen, but still rotates the A4 print fallback. Default
    /// <see cref="RichEditorPageOrientation.Portrait"/>.</summary>
    public static readonly StyledProperty<RichEditorPageOrientation> PageOrientationProperty =
        AvaloniaProperty.Register<RichEditor, RichEditorPageOrientation>(nameof(PageOrientation));

    /// <summary>Gets or sets the page orientation. Default <see cref="RichEditorPageOrientation.Portrait"/>.</summary>
    public RichEditorPageOrientation PageOrientation
    {
        get => GetValue(PageOrientationProperty);
        set => SetValue(PageOrientationProperty, value);
    }

    // A concrete paper size fixes the text-column width; Continuous reflows to the control width.
    internal bool IsPaged => PageSize != RichEditorPageSize.Continuous;
    // Paged AND showing chrome -> the gap-injecting page-stack layout. Paged without chrome -> a
    // fixed-width centered column flowing continuously (no desk, no inter-page gaps).
    private bool PagedChrome => IsPaged && ShowPageBoundaries;

    // Paper pixel dimensions at 96 DPI (portrait), then swapped for landscape. Continuous falls back
    // to A4 for print/PDF (you can't print an unbounded-width column), but its layout width comes from
    // the control, not these.
    private (double w, double h) PaperDims
    {
        get
        {
            var (w, h) = PageSize switch
            {
                RichEditorPageSize.A3 => (1123.0, 1587.0),  // 297 x 420 mm
                RichEditorPageSize.A5 => (559.0, 794.0),    // 148 x 210 mm
                RichEditorPageSize.B4 => (971.0, 1376.0),   // JIS 257 x 364 mm
                RichEditorPageSize.B5 => (688.0, 971.0),    // JIS 182 x 257 mm
                RichEditorPageSize.Letter => (816.0, 1056.0), // 8.5 x 11 in
                RichEditorPageSize.Legal => (816.0, 1344.0),  // 8.5 x 14 in
                RichEditorPageSize.Tabloid => (1056.0, 1632.0), // 11 x 17 in
                _ => (A4PageWidth, A4PageHeight),           // A4, and Continuous's print fallback
            };
            return PageOrientation == RichEditorPageOrientation.Landscape ? (h, w) : (w, h);
        }
    }
    internal double PaperWidth => PaperDims.w;
    internal double PaperHeight => PaperDims.h;
    internal double PaperContentWidth => PaperWidth - 2 * PagePadX;
    internal double PaperContentHeight => PaperHeight - 2 * PagePadY;

    /// <summary>The current paper's pixel size at 96 DPI (width × height), accounting for
    /// <see cref="PageOrientation"/>. <see cref="RichEditorPageSize.Continuous"/> reports its A4 print
    /// fallback. Useful for host fit-to-width and print math.</summary>
    public Size GetPaperPixelSize() => new(PaperWidth, PaperHeight);

    /// <summary>Header text drawn in each page's top margin (page view and print/PDF output).
    /// Null or empty = no header. Does not affect pagination — the margin band hosts it.</summary>
    public static readonly StyledProperty<string?> PageHeaderProperty =
        AvaloniaProperty.Register<RichEditor, string?>(nameof(PageHeader));

    /// <summary>Gets or sets the page header text (top margin, page view and print).</summary>
    public string? PageHeader
    {
        get => GetValue(PageHeaderProperty);
        set => SetValue(PageHeaderProperty, value);
    }

    /// <summary>Footer text drawn in each page's bottom margin (page view and print/PDF output).
    /// Null or empty = no footer.</summary>
    public static readonly StyledProperty<string?> PageFooterProperty =
        AvaloniaProperty.Register<RichEditor, string?>(nameof(PageFooter));

    /// <summary>Gets or sets the page footer text (bottom margin, page view and print).</summary>
    public string? PageFooter
    {
        get => GetValue(PageFooterProperty);
        set => SetValue(PageFooterProperty, value);
    }

    /// <summary>Draws "page / total" in each page's bottom-right margin (page view and print/PDF
    /// output). Default false.</summary>
    public static readonly StyledProperty<bool> ShowPageNumbersProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(ShowPageNumbers));

    /// <summary>Gets or sets whether page numbers are drawn (bottom margin, page view and print).</summary>
    public bool ShowPageNumbers
    {
        get => GetValue(ShowPageNumbersProperty);
        set => SetValue(ShowPageNumbersProperty, value);
    }

    // Header/footer/page number, drawn inside the paper's margin bands (never the content box, so
    // pagination is unaffected). `paper` is the page rect in the caller's coordinate space — the
    // page-view loop passes view coordinates, RenderPrintPage passes the page at the origin.
    private void DrawPageMarginChrome(DrawingContext ctx, Rect paper, int pageIndex, int pageCount)
    {
        var typeface = new Avalonia.Media.Typeface(DefaultFontFamily);
        void DrawSmall(string text, bool top, bool right)
        {
            var ft = new Avalonia.Media.FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                Avalonia.Media.FlowDirection.LeftToRight, typeface, 11, Avalonia.Media.Brushes.Gray);
            double x = right ? paper.X + PagePadX + PaperContentWidth - ft.Width : paper.X + PagePadX;
            double bandCenter = top ? paper.Y + PagePadY / 2 : paper.Bottom - PagePadY / 2;
            ctx.DrawText(ft, new Point(x, bandCenter - ft.Height / 2));
        }
        if (!string.IsNullOrEmpty(PageHeader)) DrawSmall(PageHeader!, top: true, right: false);
        if (!string.IsNullOrEmpty(PageFooter)) DrawSmall(PageFooter!, top: false, right: false);
        if (ShowPageNumbers) DrawSmall($"{pageIndex + 1} / {pageCount}", top: false, right: true);
    }

    // Page-start positions in continuous document space; recomputed by MeasureOverride (every edit
    // funnels through InvalidateMeasure via NotifyStatus) and lazily on first use.
    private List<double>? _pageBreaks;

    private List<double> EnsurePageBreaks()
        => _pageBreaks ??= ComputePageBreaks(PaperContentWidth, PaperContentHeight);

    // The wrap width every walker (render, measure, the three hit-tests, BlockAtY) must use, so
    // pagination and geometry can never disagree. A concrete paper fixes it to the paper's content
    // width (with or without page chrome); Continuous reflows to the control width.
    internal double ContentLayoutWidth => IsPaged ? PaperContentWidth : Bounds.Width;

    // Left edge of the fixed-width text column when paged without page chrome: centered in the control.
    private double NoChromeColX => Math.Max(0, (Bounds.Width - PaperContentWidth) / 2);

    private double PageDeskX => Math.Max(0, (Bounds.Width - PaperWidth) / 2);
    private double PageContentOffsetX => PageDeskX + PagePadX;
    private Rect PageRectView(int i) => new(PageDeskX, PageGap + i * (PaperHeight + PageGap), PaperWidth, PaperHeight);
    private double ContentTopView(int i) => PageGap + i * (PaperHeight + PageGap) + PagePadY;

    // Bare-column mode (paged, no chrome) injects this much whitespace between pages, with the dashed
    // separator centered in it — so consecutive pages read as separate without the full page chrome.
    internal const double NoChromePageGap = 40;

    // Per-page content origin in view space, for both paged modes (chrome page-stack and bare column).
    private double PagedContentLeftView => PagedChrome ? PageContentOffsetX : NoChromeColX;
    private double PagedContentTopView(int i) => PagedChrome
        ? ContentTopView(i)                              // desk gap + paper stack + top margin
        : EnsurePageBreaks()[i] + i * NoChromePageGap;   // bare column: just the injected gaps

    private int PageOfDocY(double docY)
    {
        var br = EnsurePageBreaks();
        int i = br.Count - 1;
        while (i > 0 && docY < br[i]) i--;
        return i;
    }

    // --- Print rendering (P-milestone Phase 3). ---

    /// <summary>Number of pages the document occupies when paginated for print, at the current
    /// <see cref="PageSize"/> (Continuous falls back to A4). Independent of <see cref="ShowPageBoundaries"/> —
    /// printing always paginates, even from the continuous layout.</summary>
    public int GetPrintPageCount()
        => ComputePageBreaks(PaperContentWidth, PaperContentHeight).Count;

    /// <summary>Renders one page to a bitmap at the given DPI (96 = screen preview, 300 = print
    /// quality; a 300-DPI A4 page is ≈2480×3508 px / ~35 MB, so render and dispose page by page).
    /// Page dimensions follow <see cref="PageSize"/> (Continuous falls back to A4). Content only — no caret,
    /// selection highlights, or resize handles. Must run on the UI thread.</summary>
    public Avalonia.Media.Imaging.Bitmap RenderPrintPage(int pageIndex, double dpi = 96)
    {
        double paperW = PaperWidth, paperH = PaperHeight, contentW = PaperContentWidth, contentH = PaperContentHeight;
        var breaks = ComputePageBreaks(contentW, contentH);
        if (pageIndex < 0 || pageIndex >= breaks.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"document has {breaks.Count} page(s)");

        double scale = dpi / 96.0;
        var pixels = new Avalonia.PixelSize(
            (int)Math.Round(paperW * scale), (int)Math.Round(paperH * scale));
        var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(pixels, new Vector(dpi, dpi));
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.FillRectangle(Avalonia.Media.Brushes.White, new Rect(0, 0, paperW, paperH));
            if (Document != null)
            {
                double sliceTop = breaks[pageIndex];
                double sliceBottom = pageIndex + 1 < breaks.Count ? breaks[pageIndex + 1] : double.PositiveInfinity;
                // Same slice clip rule as the page-view render: end the clip where the slice ends.
                var clip = new Rect(PagePadX, PagePadY, contentW,
                    Math.Min(contentH, sliceBottom - sliceTop));
                using (ctx.PushClip(clip))
                using (ctx.PushTransform(Avalonia.Matrix.CreateTranslation(PagePadX, PagePadY - sliceTop)))
                    DrawDocumentBlocks(ctx, contentW, sliceTop, sliceBottom, chrome: false);
            }
            DrawPageMarginChrome(ctx, new Rect(0, 0, paperW, paperH), pageIndex, breaks.Count);
        }
        return rtb;
    }

    /// <summary>Writes the document as a raster PDF (no external dependencies), one page per
    /// <see cref="PageSize"/> sheet (Continuous falls back to A4). Each page is one FlateDecode RGB image at
    /// the given DPI — 300 (default) is print quality; text-heavy pages compress well, photo-heavy
    /// documents grow large. Text is not selectable (vector PDF would need a DrawingContext PDF backend
    /// Avalonia doesn't expose). Must run on the UI thread.</summary>
    public void SavePdf(System.IO.Stream stream, double dpi = 300)
    {
        int pages = GetPrintPageCount();
        const double ptPerPx = 72.0 / 96.0;
        Formatters.PdfWriter.Write(stream, PaperWidth * ptPerPx, PaperHeight * ptPerPx, pages, i =>
        {
            var bmp = RenderPrintPage(i, dpi);
            try { return BitmapToRgb24(bmp); }
            finally { (bmp as IDisposable)?.Dispose(); }
        });
    }

    // BGRA8888 (RenderTargetBitmap's format) -> packed top-down RGB24. Alpha is always 255 here:
    // print pages start from an opaque white fill.
    private static (int width, int height, byte[] rgb) BitmapToRgb24(Avalonia.Media.Imaging.Bitmap bmp)
    {
        var ps = bmp.PixelSize;
        int stride = ps.Width * 4;
        var bgra = new byte[stride * ps.Height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bgra, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bmp.CopyPixels(new PixelRect(ps), handle.AddrOfPinnedObject(), bgra.Length, stride);
        }
        finally { handle.Free(); }

        var rgb = new byte[ps.Width * ps.Height * 3];
        for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
        {
            rgb[j] = bgra[i + 2];
            rgb[j + 1] = bgra[i + 1];
            rgb[j + 2] = bgra[i];
        }
        return (ps.Width, ps.Height, rgb);
    }

    // --- The single doc<->view coordinate choke point (P-milestone Phase 2). ---
    // Document space = the continuous layout every walker computes in. View space = control
    // coordinates with page chrome (desk centering, paper margins, inter-page gaps) injected.
    // Render, caret/IME geometry and BringIntoView map doc->view; pointer input maps view->doc
    // once at entry. Both are identity when page view is off.

    internal Point MapDocToView(Point doc)
    {
        if (!IsPaged) return doc;
        int i = PageOfDocY(doc.Y);
        return new Point(doc.X + PagedContentLeftView, doc.Y + (PagedContentTopView(i) - EnsurePageBreaks()[i]));
    }

    internal Rect MapDocToView(Rect doc)
        => IsPaged ? new Rect(MapDocToView(doc.TopLeft), doc.Size) : doc;

    internal Point MapViewToDoc(Point view)
    {
        if (!IsPaged) return view;
        var br = EnsurePageBreaks();
        if (PagedChrome)
        {
            double band = PaperHeight + PageGap;
            int i = Math.Clamp((int)Math.Floor((view.Y - PageGap) / band), 0, br.Count - 1);
            // Clamp into the page's content slice: clicks on the top/bottom paper margin or in the gap
            // resolve to that page's first/last line instead of bleeding into the neighboring page.
            double sliceLen = i + 1 < br.Count ? br[i + 1] - br[i] - 0.01 : double.MaxValue;
            double local = Math.Clamp(view.Y - ContentTopView(i), 0, sliceLen);
            return new Point(view.X - PageContentOffsetX, br[i] + local);
        }
        // Bare column: pages are content slices separated by NoChromePageGap (heights vary, so search
        // rather than divide). A click in a gap clamps to the page above's last line.
        int p = 0;
        while (p < br.Count - 1 && view.Y >= PagedContentTopView(p + 1)) p++;
        double len = (p + 1 < br.Count ? br[p + 1] - br[p] : double.MaxValue) - 0.01;
        double loc = Math.Clamp(view.Y - PagedContentTopView(p), 0, len);
        return new Point(view.X - NoChromeColX, br[p] + loc);
    }

    // Document-space y positions where each page's content starts; [0] is always 0. An atom taller
    // than pageContentHeight (huge image/table) gets a page of its own and overflows it (v1 contract:
    // no intra-row table splits, no image scaling). The image branch must never touch img.Image —
    // the getter lazily decodes RawBytes (N6-2) and pagination has to stay decode-free.
    internal List<double> ComputePageBreaks(double contentWidth, double pageContentHeight)
    {
        var breaks = new List<double> { 0 };
        if (Document == null || pageContentHeight <= 0) return breaks;

        double y = 0;         // continuous doc-space offset (mirrors MeasureContentHeight)
        double pageStart = 0; // doc-space y where the current page began
        const double eps = 0.01; // float noise must not split an exactly-full page

        void PlaceAtom(double height)
        {
            if (y + height > pageStart + pageContentHeight + eps && y > pageStart)
            {
                breaks.Add(y);
                pageStart = y;
            }
            y += height;
        }

        foreach (var block in Document.Blocks)
        {
            y += block.MarginTop;
            if (block is TableBlock tb)
            {
                // Rows are the atoms (Word's default): a table taller than the remaining page
                // continues on the next page at a row boundary — the page-view clip+replay then
                // shows the leading rows on one page and the rest on the next automatically.
                // (Cells spanning rows across a break are split visually, like Word.)
                var tl = LayoutTable(tb, 10 + tb.Indent, y);
                for (int r = 0; r < tb.Rows; r++)
                    PlaceAtom(tl.RowY[r + 1] - tl.RowY[r]);
                y += tb.MarginBottom;
            }
            else if (block is ImageBlock img)
            {
                PlaceAtom(img.Height > 0 ? img.Height : 200);
                y += img.MarginBottom;
            }
            else if (block is DividerBlock dv)
            {
                PlaceAtom(DividerHeight);
                y += dv.MarginBottom;
            }
            else if (block is Paragraph p)
            {
                if (GetParagraphLength(p) == 0)
                {
                    PlaceAtom(!double.IsNaN(p.LineHeight) ? p.LineHeight : 20);
                }
                else
                {
                    var layout = BuildTextLayout(p, Math.Max(10, contentWidth - 20 - ParaLeft(p) - p.MarginRight));
                    double paraTop = y;
                    // Line atom boundaries come from the layout's own line-top positions (the same
                    // geometry Render draws at), NOT from summing TextLine.Height — line spacing /
                    // LineHeight overrides make height sums drift from real line tops, which sliced
                    // glyphs in half at page boundaries.
                    var lines = layout.TextLines;
                    double atomTop = 0;
                    for (int li = 1; li <= lines.Count; li++)
                    {
                        double atomBottom = li < lines.Count
                            ? layout.HitTestTextPosition(lines[li].FirstTextSourceIndex).Y
                            : layout.Height;
                        if (paraTop + atomBottom > pageStart + pageContentHeight + eps && paraTop + atomTop > pageStart)
                        {
                            breaks.Add(paraTop + atomTop);
                            pageStart = paraTop + atomTop;
                        }
                        atomTop = atomBottom;
                    }
                    y = paraTop + layout.Height;
                }
                y += p.MarginBottom;
            }
        }
        return breaks;
    }
}
