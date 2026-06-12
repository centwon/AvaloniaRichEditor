using System;
using System.Collections.Generic;
using Avalonia;
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
                PlaceAtom(LayoutTable(tb, 10 + tb.Indent, y).TotalHeight);
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
