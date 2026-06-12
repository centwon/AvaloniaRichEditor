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
    // A4 at 96 DPI.
    internal const double A4PageWidth = 794;
    internal const double A4PageHeight = 1123;

    // Paper margins (content box inset) and the grey-desk gap between consecutive pages.
    internal const double PagePadX = 48;
    internal const double PagePadY = 40;
    internal const double PageGap = 24;
    internal const double PageContentWidth = A4PageWidth - 2 * PagePadX;    // 698
    internal const double PageContentHeight = A4PageHeight - 2 * PagePadY;  // 1043

    /// <summary>Word-style page view: renders the document as a stack of A4 pages on a grey desk,
    /// breaking content at line boundaries. Off (default) keeps the continuous single-column layout,
    /// so existing hosts are unaffected.</summary>
    public static readonly StyledProperty<bool> PageViewProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(PageView));

    /// <summary>Gets or sets whether the editor renders as discrete A4 pages (Word-style page view).</summary>
    public bool PageView
    {
        get => GetValue(PageViewProperty);
        set => SetValue(PageViewProperty, value);
    }

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
            double x = right ? paper.X + PagePadX + PageContentWidth - ft.Width : paper.X + PagePadX;
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
        => _pageBreaks ??= ComputePageBreaks(PageContentWidth, PageContentHeight);

    // The wrap width every walker (render, measure, the three hit-tests, BlockAtY) must use, so
    // pagination and geometry can never disagree. Identity (= control width) when page view is off.
    internal double ContentLayoutWidth => PageView ? PageContentWidth : Bounds.Width;

    private double PageDeskX => Math.Max(0, (Bounds.Width - A4PageWidth) / 2);
    private double PageContentOffsetX => PageDeskX + PagePadX;
    private Rect PageRectView(int i) => new(PageDeskX, PageGap + i * (A4PageHeight + PageGap), A4PageWidth, A4PageHeight);
    private double ContentTopView(int i) => PageGap + i * (A4PageHeight + PageGap) + PagePadY;

    private int PageOfDocY(double docY)
    {
        var br = EnsurePageBreaks();
        int i = br.Count - 1;
        while (i > 0 && docY < br[i]) i--;
        return i;
    }

    // --- Print rendering (P-milestone Phase 3). ---

    /// <summary>Number of A4 pages the document occupies when paginated for print. Independent of
    /// <see cref="PageView"/> — printing always paginates, even from the continuous layout.</summary>
    public int GetPrintPageCount()
        => ComputePageBreaks(PageContentWidth, PageContentHeight).Count;

    /// <summary>Renders one A4 page to a bitmap at the given DPI (96 = screen preview, 300 = print
    /// quality; a 300-DPI page is ≈2480×3508 px / ~35 MB, so render and dispose page by page).
    /// Content only — no caret, selection highlights, or resize handles. Must run on the UI thread.</summary>
    public Avalonia.Media.Imaging.Bitmap RenderPrintPage(int pageIndex, double dpi = 96)
    {
        var breaks = ComputePageBreaks(PageContentWidth, PageContentHeight);
        if (pageIndex < 0 || pageIndex >= breaks.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"document has {breaks.Count} page(s)");

        double scale = dpi / 96.0;
        var pixels = new Avalonia.PixelSize(
            (int)Math.Round(A4PageWidth * scale), (int)Math.Round(A4PageHeight * scale));
        var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(pixels, new Vector(dpi, dpi));
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.FillRectangle(Avalonia.Media.Brushes.White, new Rect(0, 0, A4PageWidth, A4PageHeight));
            if (Document != null)
            {
                double sliceTop = breaks[pageIndex];
                double sliceBottom = pageIndex + 1 < breaks.Count ? breaks[pageIndex + 1] : double.PositiveInfinity;
                // Same slice clip rule as the page-view render: end the clip where the slice ends.
                var clip = new Rect(PagePadX, PagePadY, PageContentWidth,
                    Math.Min(PageContentHeight, sliceBottom - sliceTop));
                using (ctx.PushClip(clip))
                using (ctx.PushTransform(Avalonia.Matrix.CreateTranslation(PagePadX, PagePadY - sliceTop)))
                    DrawDocumentBlocks(ctx, PageContentWidth, sliceTop, sliceBottom, chrome: false);
            }
            DrawPageMarginChrome(ctx, new Rect(0, 0, A4PageWidth, A4PageHeight), pageIndex, breaks.Count);
        }
        return rtb;
    }

    /// <summary>Writes the document as a raster PDF (A4 pages, no external dependencies). Each page
    /// is one FlateDecode RGB image at the given DPI — 300 (default) is print quality; text-heavy
    /// pages compress well, photo-heavy documents grow large. Text is not selectable (vector PDF
    /// would need a DrawingContext PDF backend Avalonia doesn't expose). Must run on the UI thread.</summary>
    public void SavePdf(System.IO.Stream stream, double dpi = 300)
    {
        int pages = GetPrintPageCount();
        const double ptPerPx = 72.0 / 96.0;
        Formatters.PdfWriter.Write(stream, A4PageWidth * ptPerPx, A4PageHeight * ptPerPx, pages, i =>
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
        if (!PageView) return doc;
        int i = PageOfDocY(doc.Y);
        return new Point(doc.X + PageContentOffsetX, doc.Y + (ContentTopView(i) - EnsurePageBreaks()[i]));
    }

    internal Rect MapDocToView(Rect doc)
        => PageView ? new Rect(MapDocToView(doc.TopLeft), doc.Size) : doc;

    internal Point MapViewToDoc(Point view)
    {
        if (!PageView) return view;
        var br = EnsurePageBreaks();
        double band = A4PageHeight + PageGap;
        int i = Math.Clamp((int)Math.Floor((view.Y - PageGap) / band), 0, br.Count - 1);
        // Clamp into the page's content slice: clicks on the top/bottom paper margin or in the gap
        // resolve to that page's first/last line instead of bleeding into the neighboring page.
        double sliceLen = i + 1 < br.Count ? br[i + 1] - br[i] - 0.01 : double.MaxValue;
        double local = Math.Clamp(view.Y - ContentTopView(i), 0, sliceLen);
        return new Point(view.X - PageContentOffsetX, br[i] + local);
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
                if (BuildPlain(p) == "")
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
