using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Measure + render pass (content height, MeasureOverride, Render) and the automation-peer hook.
// Part of RichEditor (split out of the main file for readability).
public partial class RichEditor
{
    // Render runs per frame (caret blink = 2 Hz minimum): the fixed accent colors and pens are
    // allocated once instead of per draw call. Immutable variants carry no thread affinity.
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush AccentBrush = new(Color.FromArgb(255, 0, 120, 215));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush AccentFill50 = new(Color.FromArgb(50, 0, 120, 215));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush AccentFill60 = new(Color.FromArgb(60, 0, 120, 215));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush AccentHandleFill = new(Color.FromArgb(230, 0, 120, 215));
    private static readonly Avalonia.Media.Immutable.ImmutablePen GrayBorderPen = new(Brushes.Gray, 1);
    private static readonly Avalonia.Media.Immutable.ImmutablePen AccentPen2 = new(AccentBrush, 2);
    private static readonly Avalonia.Media.Immutable.ImmutablePen AccentPen15 = new(AccentBrush, 1.5);
    private static readonly Avalonia.Media.Immutable.ImmutablePen AccentBorderPen = new(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(120, 0, 120, 215)), 1);
    private static readonly Avalonia.Media.Immutable.ImmutablePen BlockCaretPen = new(Brushes.Black, 2);
    // Text-caret pen, cached so the 2 Hz blink / scroll / hover repaints don't allocate a Pen each frame
    // (unlike the static pens above it depends on the CaretBrush property). Reset when CaretBrush changes.
    private Pen? _caretPen;
    // Faint dashed marker at page boundaries when a paper size is set but page chrome is off.
    private static readonly Avalonia.Media.Immutable.ImmutablePen PageBreakPen =
        new(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(110, 0x9E, 0x9E, 0x9E)), 1,
            new Avalonia.Media.Immutable.ImmutableDashStyle(new double[] { 4, 4 }, 0));

    // Total rendered height of the document at the given content width (mirrors the render advancement).
    // Reported via MeasureOverride so the hosting ScrollViewer grows its scrollable extent with content.
    private double MeasureContentHeight(double width)
    {
        if (Document == null) return 0;
        double yOffset = 0;
        foreach (var block in Document.Blocks)
        {
            yOffset += block.MarginTop;
            yOffset += BlockExtent(block, width, yOffset, out _, out _) + block.MarginBottom;
        }
        return yOffset + 40; // a little breathing room at the bottom
    }

    private double _measuredHeight;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        // No edit pending => content is unchanged since the last build, so the layout/table caches can
        // be trusted here too (mirrors Render). This keeps caret-only moves — NotifyStatus funnels every
        // one through InvalidateMeasure — from re-hashing every paragraph via ParagraphSig. An edit runs
        // with the flag off (verified path rebuilds the changed paragraph), and a width change still
        // misses on the width check inside BuildTextLayout and rebuilds correctly.
        _trustLayoutCache = !_textChangedPending;
        try
        {
            bool noWidth = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0;
            if (PagedChrome)
            {
                // Every edit funnels through InvalidateMeasure (NotifyStatus), so recomputing the page
                // breaks here keeps them fresh for render and hit-testing without extra invalidation.
                _pageBreaks = ComputePageBreaks(PaperContentWidth, PaperContentHeight);
                double pw = noWidth ? PaperWidth + 2 * PageGap : Math.Max(availableSize.Width, PaperWidth);
                _measuredHeight = Math.Max(MinHeight, PageGap + _pageBreaks.Count * (PaperHeight + PageGap));
                return new Size(pw, _measuredHeight);
            }
            if (IsPaged)
            {
                // Paged without chrome: a fixed-width column flowing continuously, centered in any extra
                // width, with NoChromePageGap whitespace injected between pages. The control is at least the
                // column wide, so a narrow viewport scrolls horizontally.
                double cw = PaperContentWidth;
                _pageBreaks = ComputePageBreaks(cw, PaperContentHeight);
                double contentH = MeasureContentHeight(cw) + (_pageBreaks.Count - 1) * NoChromePageGap;
                _measuredHeight = Math.Max(MinHeight, contentH);
                return new Size(noWidth ? cw : Math.Max(availableSize.Width, cw), _measuredHeight);
            }
            double w = noWidth ? A4ContentWidth : availableSize.Width;
            _measuredHeight = Math.Max(MinHeight, MeasureContentHeight(w));
            return new Size(w, _measuredHeight);
        }
        finally { _trustLayoutCache = false; } // never leak the trusted state past this measure pass
    }

    // The peer is created lazily (only when assistive tech attaches); kept so state changes
    // (read-only toggle) can be surfaced to it. Null until then.
    private RichEditorAutomationPeer? _automationPeer;

    /// <inheritdoc/>
    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
        => _automationPeer = new RichEditorAutomationPeer(this);

    // Draw-culling redraw contract: blocks outside the viewport are skipped in Render, so scrolling
    // must re-run Render for blocks entering the viewport to appear. Avalonia happens to re-render
    // scrolled content already; subscribing makes that an explicit guarantee instead of an accident.
    private ScrollViewer? _cullScroller;

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _cullScroller = this.FindAncestorOfType<ScrollViewer>();
        if (_cullScroller != null) _cullScroller.ScrollChanged += OnHostScrollChanged;
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_cullScroller != null)
        {
            _cullScroller.ScrollChanged -= OnHostScrollChanged;
            _cullScroller = null;
        }
    }

    private void OnHostScrollChanged(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        // Was this paint scheduled by a content edit? Read the flag before RaisePendingChangeEvents
        // consumes it. A repaint with no pending edit (caret blink, scroll) can't have changed any
        // paragraph, so the layout cache may be trusted without re-hashing every paragraph's text.
        bool contentChanged = _textChangedPending;
        // Flush change events after the edit/caret move that scheduled this paint (off the render stack).
        RaisePendingChangeEvents();
        // Transparent fill makes the whole control hit-testable (clicks on empty space below the
        // text must still reach OnPointerPressed). Must cover the full bounds, not a fixed height.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Document == null) return;

        _trustLayoutCache = !contentChanged;
        try
        {

        // Recomputed every render so resize handles track the current layout.
        _columnBoundaries.Clear();
        _rowBoundaries.Clear();
        _imageHandles.Clear();
        _cellImageRects.Clear();
        _inlineImageRects.Clear();
        _inlineHandles.Clear();

        // Draw culling (N6-5): blocks fully outside the hosting ScrollViewer's viewport advance
        // yOffset without issuing draw commands, so the scene the render thread rasterizes stays
        // viewport-sized regardless of document length. Layout math, caret geometry, and hit-testing
        // are unaffected (hit-test paths walk the document independently of what was drawn). The
        // block holding the caret or selection anchor always draws so _lastCaretPoint/BringIntoView
        // stay correct even when the caret is scrolled off-screen. No ScrollViewer => no culling.
        double visTop = double.NegativeInfinity, visBottom = double.PositiveInfinity;
        if (this.FindAncestorOfType<ScrollViewer>() is { } scroller
            && scroller.TranslatePoint(default, this) is { } vt
            && scroller.TranslatePoint(new Point(0, scroller.Viewport.Height), this) is { } vb)
        {
            // TranslatePoint runs the full transform chain, so zoom (LayoutTransform) is handled.
            double slack = Math.Max(400, vb.Y - vt.Y); // one viewport of slack on each side
            visTop = vt.Y - slack;
            visBottom = vb.Y + slack;
        }

        Point? caretPoint;
        double caretHeight;
        Rect? blockCaretRect;

        if (!IsPaged)
        {
            // Continuous (Free): one walk at the control width.
            (caretPoint, caretHeight, blockCaretRect) = DrawDocumentBlocks(context, Bounds.Width, visTop, visBottom);
        }
        else if (!ShowPageBoundaries)
        {
            // Paged without chrome: a centered fixed-width column with NoChromePageGap whitespace
            // injected between pages (a faint dashed line centered in each gap). Replay the block walk
            // once per page under a clip + translation — same gap-injection pattern as the page-stack
            // render, minus the desk/paper/margins. The returned caret stays document-space; MapDocToView
            // adds the centering X and the cumulative gaps.
            var breaks = EnsurePageBreaks();
            caretPoint = null; caretHeight = 20; blockCaretRect = null;
            for (int i = 0; i < breaks.Count; i++)
            {
                double sliceTop = breaks[i];
                double sliceBottom = i + 1 < breaks.Count ? breaks[i + 1] : double.PositiveInfinity;
                double viewTop = sliceTop + i * NoChromePageGap;
                double clipH = i + 1 < breaks.Count ? sliceBottom - sliceTop : Math.Max(1, _measuredHeight - viewTop);
                if (viewTop + clipH < visTop || viewTop > visBottom) continue;
                if (i > 0) // dashed separator centered in the gap above this page
                {
                    double sepY = viewTop - NoChromePageGap / 2;
                    context.DrawLine(PageBreakPen, new Point(NoChromeColX, sepY), new Point(NoChromeColX + PaperContentWidth, sepY));
                }
                using (context.PushClip(new Rect(NoChromeColX, viewTop, PaperContentWidth, clipH)))
                using (context.PushTransform(Matrix.CreateTranslation(NoChromeColX, viewTop - sliceTop)))
                {
                    var (cp, ch, bcr) = DrawDocumentBlocks(context, PaperContentWidth, sliceTop, sliceBottom);
                    if (cp != null) { caretPoint = cp; caretHeight = ch; }
                    if (bcr != null) blockCaretRect = bcr;
                }
            }
        }
        else
        {
            // Page view (P-milestone Phase 2): paint the grey desk and each visible paper, then
            // replay the (unchanged) block walk once per visible page under a clip + translation —
            // gap injection without per-page reflow. The walk's cull window is the page's document
            // slice, so each replay only issues draw commands for its own page's blocks; a paragraph
            // straddling two pages draws in both replays and the clips split it at the line boundary.
            var breaks = EnsurePageBreaks();
            context.FillRectangle(DeskBrush, new Rect(Bounds.Size));
            caretPoint = null; caretHeight = 20; blockCaretRect = null;
            double dx = PageContentOffsetX;
            for (int i = 0; i < breaks.Count; i++)
            {
                var paper = PageRectView(i);
                if (paper.Bottom < visTop || paper.Y > visBottom) continue;
                context.FillRectangle(Brushes.White, paper);
                context.DrawRectangle(null, GrayBorderPen, paper);
                DrawPageMarginChrome(context, paper, i, breaks.Count);
                var contentBox = new Rect(paper.X + PagePadX, paper.Y + PagePadY,
                    paper.Width - 2 * PagePadX, paper.Height - 2 * PagePadY);
                double sliceTop = breaks[i];
                double sliceBottom = i + 1 < breaks.Count ? breaks[i + 1] : double.PositiveInfinity;
                // The clip must end where the page's document slice ends, not at the full content
                // box: a page whose slice stops short (next line didn't fit) has leftover room at
                // the bottom, and the next page's first lines would otherwise render into it,
                // sliced mid-glyph at the box edge. (An oversized atom keeps the full box: Min.)
                var clip = new Rect(contentBox.X, contentBox.Y, contentBox.Width,
                    Math.Min(contentBox.Height, sliceBottom - sliceTop));
                using (context.PushClip(clip))
                using (context.PushTransform(Matrix.CreateTranslation(dx, contentBox.Y - sliceTop)))
                {
                    var (cp, ch, bcr) = DrawDocumentBlocks(context, PaperContentWidth, sliceTop, sliceBottom);
                    if (cp != null) { caretPoint = cp; caretHeight = ch; }
                    if (bcr != null) blockCaretRect = bcr;
                }
            }
        }

        DrawCaretAndBringIntoView(context, caretPoint, caretHeight, blockCaretRect);

        // "Draw table" rubber-band: a dashed rectangle following the cursor (view space, drawn over all
        // content). Only present while a drag is in progress in draw mode.
        if (_tableDrawStart is { } ds && _tableDrawCurrent is { } dc)
            context.DrawRectangle(AccentFill50, AccentPen2, new Rect(ds, dc));
        }
        finally { _trustLayoutCache = false; } // never leak the trusted state past this render pass
    }

    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush DeskBrush = new(Color.FromRgb(158, 158, 158));

    // The document block walk: selection highlights, table grids, paragraphs, images, dividers,
    // resize-handle registration and caret geometry, all in continuous document coordinates.
    // Page view replays this once per visible page under a clip+translation, with the page's
    // document slice as the cull window; the continuous mode calls it once with the viewport.
    // chrome=false (print/export rendering) draws content only: no selection highlights, caret
    // geometry, IME preedit, image borders/resize handles, or handle-registry writes.
    private (Point? caretPoint, double caretHeight, Rect? blockCaretRect) DrawDocumentBlocks(
        DrawingContext context, double maxWidth, double visTop, double visBottom, bool chrome = true)
    {
        TextPointer? selStart = null, selEnd = null;
        HashSet<Paragraph>? selectedParagraphs = null;
        int selCmp = _selectionStart.Paragraph != null && _selectionEnd.Paragraph != null
            ? _selectionStart.CompareTo(_selectionEnd) : 0;
        if (chrome && _selectionStart.Paragraph != null && _selectionEnd.Paragraph != null && selCmp != 0)
        {
            if (selCmp < 0) { selStart = _selectionStart; selEnd = _selectionEnd; }
            else { selStart = _selectionEnd; selEnd = _selectionStart; }
            var allParas = GetAllParagraphsInOrder();
            // One scan for both endpoints instead of two IndexOf passes (this runs every render frame a
            // selection exists — once per visible page in page view — so the linear walks add up on drag).
            int si = -1, ei = -1;
            for (int idx = 0; idx < allParas.Count; idx++)
            {
                var pp = allParas[idx];
                if (si < 0 && ReferenceEquals(pp, selStart.Paragraph)) si = idx;
                if (ReferenceEquals(pp, selEnd.Paragraph)) ei = idx;
            }
            if (si >= 0 && ei >= 0)
            {
                selectedParagraphs = new HashSet<Paragraph>();
                for (int idx = si; idx <= ei; idx++)
                    selectedParagraphs.Add(allParas[idx]);
            }
        }

        double yOffset = 0;
        double listIndent = 10;
        Point? caretPoint = null;
        // Caret bar = glyph-sized and bottom-aligned in its line. On tall lines (an inline image
        // treated as a character) the caret keeps the adjacent text's height down at the baseline,
        // instead of a fixed-height bar floating at the line top far above the text.
        double caretHeight = 20;
        Rect? blockCaretRect = null; // when a block caret is active, the image/table it sits in front of
        int orderedIndex = 0; // running counter for consecutive ordered-list paragraphs

        foreach (var block in Document!.Blocks)
        {
            yOffset += block.MarginTop;
            // G1 P5: source each block's height + layout objects from the single BlockExtent pass — the
            // same one measure/hit-tests/pagination consume — so the render walk can never drift from
            // them on a block's height. Drawing, culling, caret/selection and the per-cell IME-preedit
            // layout below all stay in render; only the geometry source is unified.
            double beHeight = BlockExtent(block, maxWidth, yOffset, out var beParaLayout, out var beTableLayout);
            if (block is TableBlock tb)
            {
                orderedIndex = 0;
                double startX = 10 + tb.Indent;
                double tableTop = yOffset;
                var tl = beTableLayout!.Value; // == LayoutTable(tb, 10 + tb.Indent, tableTop) from BlockExtent
                if (ReferenceEquals(tb, _caretBlock))
                    blockCaretRect = new Rect(startX, tableTop, tl.TableWidth, tl.TotalHeight);

                // Cull check: skip cell draws + resize-handle registration when fully off-screen
                // (off-screen handles can't be grabbed). Geometry (tl) is still computed above so
                // yOffset advances identically. A table being edited (caret in a cell) always draws.
                bool tbVisible = (tableTop + tl.TotalHeight >= visTop && tableTop <= visBottom)
                    || (_caretPosition?.Paragraph?.Parent as TableCell)?.Parent == tb
                    || ReferenceEquals(tb, _caretBlock) || ReferenceEquals(tb, _selectedBlock);
                if (!tbVisible)
                {
                    yOffset = tableTop + tl.TotalHeight + tb.MarginBottom;
                    continue;
                }

                // When the drag spans multiple cells of this table, highlight the rectangular block fully
                // (Excel/Word style) instead of the linear text run; otherwise fall back to text highlight.
                var cellBlock = chrome ? SelectedCellRange(tb) : null;

                foreach (var (r, c, rect) in tl.AnchorRects)
                {
                    var cell = tb.Cells[r][c];
                    double innerW = Math.Max(10, rect.Width - 10);

                    if (cell.Background != null)
                        context.FillRectangle(cell.Background, rect);
                    context.DrawRectangle(null, GrayBorderPen, rect);

                    // A cell is in "cell-selection mode" when it's part of a multi-cell drag block, or its
                    // whole content is selected (Tab focus / triple-click). Such cells show a fill and NO
                    // caret. A bare caret (collapsed selection) means "editing text" and shows no fill —
                    // so the caret's presence vs. the fill cleanly distinguishes the two modes.
                    bool inBlock = cellBlock is { } cb && r >= cb.r0 && r <= cb.r1 && c >= cb.c0 && c <= cb.c1;
                    if (inBlock)
                        context.FillRectangle(SelectionBrush, rect);
                    bool cellSelected = inBlock;

                    // Draw the cell's block list top-to-bottom via the recursive primitive (handles
                    // paragraphs, block images, dividers and nested tables — P4-2b). Chrome (highlight,
                    // caret, preedit, inline images) is keyed to the paragraph that holds the caret.
                    DrawCellBlockList(context, cell.Blocks, rect.X + 5, rect.Y + 5, innerW, chrome,
                        cellSelected, cellBlock != null, selectedParagraphs, selStart, selEnd,
                        ref caretPoint, ref caretHeight);
                }

                // Resize handles live on the physical grid lines (independent of merges). Internal column
                // edges redistribute width with the next column; the outer-right edge grows the last column.
                if (chrome)
                {
                    for (int r = 0; r < tb.Rows; r++)
                        _rowBoundaries.Add((new Rect(startX, tl.RowY[r + 1] - 3, tl.TableWidth, 6), tb, r, tl.RowY[r + 1] - tl.RowY[r]));
                    for (int c = 0; c < tb.Columns; c++)
                        _columnBoundaries.Add((new Rect(tl.ColX[c + 1] - 3, tableTop, 6, tl.TotalHeight), tb, c));
                }

                yOffset = tableTop + tl.TotalHeight;
                // Selected-table affordance: the accent frame + translucent fill shows when the table
                // is the active block — set by clicking its outer left/top border (_caretBlock) or by
                // the explicit block selection (_selectedBlock). This is what makes "the table is
                // selected" visible (the border MoveCursor signals it's grabbable beforehand).
                if (chrome && (ReferenceEquals(tb, _selectedBlock) || ReferenceEquals(tb, _caretBlock)))
                {
                    var tableRect = new Rect(startX, tableTop, tl.TableWidth, tl.TotalHeight);
                    context.FillRectangle(AccentFill50, tableRect);
                    context.DrawRectangle(null, AccentPen2, tableRect);
                }
                // MarginBottom (default 10) — must match MeasureContentHeight/hit-tests/pagination;
                // this was a hardcoded 10 left over from before the block-margin milestone.
                yOffset += tb.MarginBottom;
            }
            else if (block is Paragraph paragraph)
            {
                string fullText = BuildPlain(paragraph);

                bool hasPreedit = chrome && _caretPosition != null && _caretPosition.Paragraph == paragraph && !string.IsNullOrEmpty(_preeditText);

                double px = ParaLeft(paragraph);

                // Ordered numbering runs continuously across consecutive ordered paragraphs; reset otherwise.
                if (paragraph.ListType != ListKind.Ordered) orderedIndex = 0;

                if (fullText == "" && !hasPreedit)
                {
                    if (paragraph.ListType != ListKind.None)
                        DrawListMarker(context, paragraph, paragraph.ListType == ListKind.Ordered ? ++orderedIndex : 0, px, yOffset);
                    if (chrome && _caretPosition != null && _caretPosition.Paragraph == paragraph)
                    {
                        caretPoint = new Point(px, yOffset);
                        _lastCaretPoint = caretPoint.Value;
                    }
                    yOffset += paragraph.MarginBottom + beHeight; // empty-paragraph height, from BlockExtent
                    continue;
                }

                double pWidth = Math.Max(10, maxWidth - 20 - px - paragraph.MarginRight);
                // Non-preedit: reuse the cached layout BlockExtent already built (same width). The IME path
                // rebuilds with the inline composition text, which is transient and never cached.
                var layout = hasPreedit
                    ? BuildTextLayout(paragraph, pWidth, _caretPosition!.Offset, _preeditText)
                    : (beParaLayout ?? BuildTextLayout(paragraph, pWidth));

                // Cull check: the layout above is still built (cached; its height advances yOffset),
                // but off-screen paragraphs issue no draw commands. The caret paragraph always draws.
                bool pVisible = (yOffset + layout.Height >= visTop && yOffset <= visBottom)
                    || (_caretPosition != null && _caretPosition.Paragraph == paragraph);

                // One marker per hard line (\n): each line of a list paragraph is an item. Ordered lists
                // number each line; this is what makes "press Enter -> next bullet/number" work given the
                // editor's "Enter inserts \n in a Run" model (lines aren't separate paragraphs).
                // The ordered counter must advance even for culled paragraphs so visible numbering holds.
                if (paragraph.ListType != ListKind.None)
                {
                    int segStart = 0;
                    for (int i = 0; i <= fullText.Length; i++)
                    {
                        if (i == fullText.Length || fullText[i] == '\n')
                        {
                            int marker = paragraph.ListType == ListKind.Ordered ? ++orderedIndex : 0;
                            if (pVisible)
                            {
                                var lcr = layout.HitTestTextPosition(Math.Min(segStart, fullText.Length));
                                DrawListMarker(context, paragraph, marker, px, yOffset + lcr.Y);
                            }
                            segStart = i + 1;
                        }
                    }
                }

                if (!pVisible)
                {
                    yOffset += layout.Height + paragraph.MarginBottom;
                    continue;
                }

                if (paragraph.Background != null)
                    context.FillRectangle(paragraph.Background, new Rect(px, yOffset, pWidth, layout.Height));

                if (paragraph.IsQuote)
                    context.FillRectangle(Brushes.Silver, new Rect(Math.Max(0, px - 10), yOffset, 3, layout.Height));

                if (selectedParagraphs?.Contains(paragraph) == true)
                {
                    int hlStart = (paragraph == selStart!.Paragraph) ? selStart.Offset : 0;
                    int hlEnd = (paragraph == selEnd!.Paragraph) ? selEnd.Offset : fullText.Length;
                    if (hlEnd > hlStart)
                        DrawSelectionHighlight(context, layout, hlStart, hlEnd, px, yOffset);
                }

                if (chrome && _caretPosition != null && _caretPosition.Paragraph == paragraph)
                {
                    int caretDisp = _caretPosition.Offset + (hasPreedit ? _preeditText!.Length : 0);
                    var cr = layout.HitTestTextPosition(caretDisp);
                    cr = FixCaretAfterTrailingImage(layout, paragraph, _caretPosition.Offset, caretDisp, cr);
                    double th = CaretTextHeight(paragraph, _caretPosition.Offset);
                    if (cr.Height > 0 && th > cr.Height) th = cr.Height;
                    caretPoint = new Point(px + cr.X, yOffset + cr.Y + CaretYInLine(paragraph, cr.Height, th));
                    caretHeight = th;
                    _lastCaretPoint = caretPoint.Value;
                }

                layout.Draw(context, new Point(px, yOffset));
                FlushInlineTableDraws(context, chrome, selectedParagraphs, selStart, selEnd, ref caretPoint, ref caretHeight);
                if (chrome) RegisterInlineImages(context, paragraph, layout, px, yOffset);
                yOffset += layout.Height + paragraph.MarginBottom;
            }
            else if (block is ImageBlock img)
            {
                orderedIndex = 0;
                double cullHeight = beHeight; // == img.Height (or 200 fallback), from BlockExtent
                // Cull check before touching img.Image: the getter lazily decodes RawBytes (N6-2), so
                // skipping it means off-screen images are never decoded at all. yOffset advances by the
                // declared size, matching MeasureContentHeight's unconditional advance.
                if ((yOffset + cullHeight < visTop || yOffset > visBottom)
                    && !ReferenceEquals(img, _caretBlock) && !ReferenceEquals(img, _selectedBlock))
                {
                    yOffset += cullHeight + img.MarginBottom;
                    continue;
                }
                if (img.Image != null)
                {
                    double width = img.Width > 0 ? img.Width : 200;
                    double height = beHeight; // == img.Height (or 200), shared with measure/hit-tests
                    double imgX = listIndent + img.Indent;
                    var imgRect = new Rect(imgX, yOffset, width, height);
                    context.DrawImage(img.Image, imgRect);
                    if (ReferenceEquals(img, _caretBlock)) blockCaretRect = imgRect;

                    if (chrome)
                    {
                        bool imgSelected = ReferenceEquals(img, _selectedBlock);
                        if (imgSelected)
                        {
                            // Selection: translucent overlay + bold border.
                            context.FillRectangle(AccentFill60, imgRect);
                            context.DrawRectangle(null, AccentPen2, imgRect);
                        }
                        // Thin border + bottom-right resize handle.
                        context.DrawRectangle(null, AccentBorderPen, imgRect);
                        var handle = new Rect(imgX + width - 6, yOffset + height - 6, 12, 12);
                        context.FillRectangle(AccentHandleFill, handle);
                        // Slightly larger hit area than the visual handle for easier grabbing.
                        _imageHandles.Add((new Rect(imgX + width - 9, yOffset + height - 9, 18, 18), img));
                    }

                    yOffset += height + img.MarginBottom;
                }
            }
            else if (block is DividerBlock dv)
            {
                orderedIndex = 0;
                if (yOffset + beHeight >= visTop && yOffset <= visBottom)
                {
                    double y = yOffset + beHeight / 2; // beHeight == DividerHeight, from BlockExtent
                    context.DrawLine(GrayBorderPen, new Point(listIndent, y), new Point(Math.Max(listIndent + 1, maxWidth - 10), y));
                }
                yOffset += beHeight + dv.MarginBottom;
            }
        }

        return (caretPoint, caretHeight, blockCaretRect);
    }

    // Recursive primitive: draws a block list top-to-bottom inside the box at (ox,oy) of width innerW
    // (a table cell's content box). Handles paragraphs, block images, dividers and nested tables (the
    // last via DrawNestedTable, which calls back here per nested cell — arbitrary nesting depth). Caret/
    // selection/preedit chrome is keyed to the paragraph that holds the caret; caretPoint/caretHeight are
    // threaded by ref so a caret in a (possibly deeply nested) cell is reported to the render pass.
    // cellSelected = the containing cell is filled as a block (Tab/drag select) -> no caret/highlight;
    // cellRangeActive = a multi-cell drag is in progress on the containing table -> suppress text highlight.
    private void DrawCellBlockList(
        DrawingContext context, System.Collections.Generic.IList<Block> blocks,
        double ox, double oy, double innerW, bool chrome,
        bool cellSelected, bool cellRangeActive,
        HashSet<Paragraph>? selectedParagraphs, TextPointer? selStart, TextPointer? selEnd,
        ref Point? caretPoint, ref double caretHeight)
    {
        double by = 0;
        foreach (var cb2 in blocks)
        {
            double blkY = oy + by;
            if (cb2 is Paragraph para)
            {
                bool blkPreedit = chrome && _caretPosition != null && _caretPosition.Paragraph == para && !string.IsNullOrEmpty(_preeditText);
                var layout = blkPreedit
                    ? BuildTextLayout(para, innerW, _caretPosition!.Offset, _preeditText)
                    : BuildTextLayout(para, innerW);

                if (!cellSelected && !cellRangeActive && selectedParagraphs?.Contains(para) == true)
                {
                    int cellLen = GetParagraphLength(para);
                    int hlStart = (para == selStart!.Paragraph) ? selStart.Offset : 0;
                    int hlEnd = (para == selEnd!.Paragraph) ? selEnd.Offset : cellLen;
                    if (hlEnd > hlStart)
                        DrawSelectionHighlight(context, layout, hlStart, hlEnd, ox, blkY);
                }

                if (chrome && _caretPosition != null && _caretPosition.Paragraph == para && (!cellSelected || blkPreedit))
                {
                    int caretDisp = _caretPosition.Offset + (blkPreedit ? _preeditText!.Length : 0);
                    var cr = layout.HitTestTextPosition(caretDisp);
                    cr = FixCaretAfterTrailingImage(layout, para, _caretPosition.Offset, caretDisp, cr);
                    double th = CaretTextHeight(para, _caretPosition.Offset);
                    if (cr.Height > 0 && th > cr.Height) th = cr.Height;
                    caretPoint = new Point(ox + cr.X, blkY + cr.Y + CaretYInLine(para, cr.Height, th));
                    caretHeight = th;
                    _lastCaretPoint = caretPoint.Value;
                }

                layout.Draw(context, new Point(ox, blkY));
                FlushInlineTableDraws(context, chrome, selectedParagraphs, selStart, selEnd, ref caretPoint, ref caretHeight);
                if (chrome) RegisterInlineImages(context, para, layout, ox, blkY);
                by += layout.Height;
            }
            else if (cb2 is ImageBlock cimg)
            {
                // A block image inside a cell, scaled to fit the cell width. With chrome on, it gets the
                // same selection overlay + resize handle as a top-level block image, registered in
                // document coordinates so the shared resize/hit-test paths work unchanged.
                var (iw, ih) = CellImageSize(cimg, innerW);
                if (cimg.Image is { } bmp)
                {
                    var ir = new Rect(ox, blkY, iw, ih);
                    context.DrawImage(bmp, ir);
                    if (chrome)
                    {
                        if (ReferenceEquals(cimg, _selectedBlock))
                        {
                            context.FillRectangle(AccentFill60, ir);
                            context.DrawRectangle(null, AccentPen2, ir);
                        }
                        context.DrawRectangle(null, AccentBorderPen, ir);
                        var handle = new Rect(ox + iw - 6, blkY + ih - 6, 12, 12);
                        context.FillRectangle(AccentHandleFill, handle);
                        _imageHandles.Add((new Rect(ox + iw - 9, blkY + ih - 9, 18, 18), cimg));
                        _cellImageRects.Add((ir, cimg));
                    }
                }
                by += ih;
            }
            else if (cb2 is DividerBlock)
            {
                double y = blkY + DividerHeight / 2;
                context.DrawLine(GrayBorderPen, new Point(ox, y), new Point(ox + innerW, y));
                by += DividerHeight;
            }
            else if (cb2 is TableBlock nt)
            {
                // P4-2b: a nested table. Draws the grid + recurses into each nested cell, registering
                // row/column resize handles like a top-level table (no selected-table affordance yet).
                DrawNestedTable(context, nt, ox, blkY, chrome, selectedParagraphs, selStart, selEnd, ref caretPoint, ref caretHeight);
                by += LayoutTable(nt, ox, blkY).TotalHeight;
            }
        }
    }

    // Inline tables recorded during a paragraph's layout.Draw (their document-space origins are only
    // known then). Flushed immediately after, so the list never spans paragraphs. See the BuildTextLayout
    // inline-table segment and FlushInlineTableDraws.
    private readonly List<(TableBlock table, Point origin)> _inlineTableDraws = new();

    // Draws the inline tables recorded by the just-drawn paragraph layout, then clears the list. Each is
    // drawn via DrawNestedTable so cell contents, selection highlight and a caret inside a cell all
    // render, with the caret threaded back by ref. No-op (and allocation-free) when the list is empty.
    private void FlushInlineTableDraws(
        DrawingContext context, bool chrome,
        HashSet<Paragraph>? selectedParagraphs, TextPointer? selStart, TextPointer? selEnd,
        ref Point? caretPoint, ref double caretHeight)
    {
        if (_inlineTableDraws.Count == 0) return;
        // Snapshot and clear BEFORE drawing: DrawNestedTable -> DrawCellBlockList draws each cell
        // paragraph, which calls back here. Clearing first means a nested cell's own (possibly empty)
        // inline-table list is what the re-entrant call sees, not these same entries again (infinite loop).
        var pending = _inlineTableDraws.ToArray();
        _inlineTableDraws.Clear();
        foreach (var (table, origin) in pending)
            DrawNestedTable(context, table, origin.X, origin.Y, chrome,
                selectedParagraphs, selStart, selEnd, ref caretPoint, ref caretHeight);
    }

    // Draws a nested table's grid at (startX, top) and recurses into each anchor cell via DrawCellBlockList.
    private void DrawNestedTable(
        DrawingContext context, TableBlock tb, double startX, double top, bool chrome,
        HashSet<Paragraph>? selectedParagraphs, TextPointer? selStart, TextPointer? selEnd,
        ref Point? caretPoint, ref double caretHeight)
    {
        var tl = LayoutTable(tb, startX, top);
        foreach (var (r, c, rect) in tl.AnchorRects)
        {
            var cell = tb.Cells[r][c];
            if (cell.Background != null) context.FillRectangle(cell.Background, rect);
            context.DrawRectangle(null, GrayBorderPen, rect);
            DrawCellBlockList(context, cell.Blocks, rect.X + 5, rect.Y + 5, Math.Max(10, rect.Width - 10), chrome,
                cellSelected: false, cellRangeActive: false, selectedParagraphs, selStart, selEnd,
                ref caretPoint, ref caretHeight);
        }
        // Resize handles on the physical grid lines (document coordinates), shared with the top-level
        // resize path — the handlers key off the TableBlock, so nested tables resize identically. The
        // outer-right edge adjusts the nested table's total width; the resize handler clamps it to the
        // enclosing cell's content width so it can't overflow. Row handles grow the cell (parent reflows).
        if (chrome)
        {
            for (int r = 0; r < tb.Rows; r++)
                _rowBoundaries.Add((new Rect(startX, tl.RowY[r + 1] - 3, tl.TableWidth, 6), tb, r, tl.RowY[r + 1] - tl.RowY[r]));
            for (int c = 0; c < tb.Columns; c++)
                _columnBoundaries.Add((new Rect(tl.ColX[c + 1] - 3, top, 6, tl.TotalHeight), tb, c));
        }
    }

    // Caret bar + deferred BringIntoView. Caret geometry arrives in document space; in page view it
    // is mapped to view space here (the walk and _lastCaretPoint stay document-space throughout).
    private void DrawCaretAndBringIntoView(DrawingContext context, Point? caretPoint, double caretHeight, Rect? blockCaretRect)
    {
        if (blockCaretRect.HasValue)
        {
            // Blinking bar: "before" = top of the left edge, "after" = bottom of the RIGHT edge —
            // mirroring a text caret before/after a character. (Drawing both on the left read as
            // "in front of the table" even when the caret was logically after it.)
            if (_isCaretVisible && IsFocused && !IsReadOnly)
            {
                var r = MapDocToView(blockCaretRect.Value);
                double cx = _caretBlockAfter ? r.Right + 3 : r.X - 3;
                double cy1 = _caretBlockAfter ? Math.Max(r.Y, r.Bottom - 20) : r.Y;
                double cy2 = _caretBlockAfter ? r.Bottom : Math.Min(r.Bottom, r.Y + 20);
                context.DrawLine(BlockCaretPen, new Point(cx, cy1), new Point(cx, cy2));
            }
        }
        else if (caretPoint.HasValue)
        {
            _lastCaretPoint = caretPoint.Value;
            _lastCaretHeight = caretHeight;
            if (_isCaretVisible && IsFocused && !IsReadOnly)
            {
                var cv = MapDocToView(caretPoint.Value);
                context.DrawLine(_caretPen ??= new Pen(CaretBrush, 1.5), cv, new Point(cv.X, cv.Y + caretHeight));
            }
        }

        // After the caret position is known, scroll it into view if it moved off-screen. Posted (not
        // called inline) so it runs after this layout/render pass rather than re-entering it.
        if (_bringCaretIntoView)
        {
            _bringCaretIntoView = false;
            // Include some margin above/below the caret so scrolling leaves it comfortably inside the
            // viewport rather than flush against (or just past) an edge.
            const double m = 40;
            Rect target = blockCaretRect is { } br
                ? MapDocToView(new Rect(br.X, Math.Max(0, br.Y - m), 2, br.Height + 2 * m))
                : MapDocToView(new Rect(_lastCaretPoint.X, Math.Max(0, _lastCaretPoint.Y - m), 2, _lastCaretHeight + 2 * m));
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { try { this.BringIntoView(target); } catch { } });
        }
    }

    // Registers the on-screen rect of each inline image in `p` (click hit-testing for selection)
    // and, for the selected one, draws the selection border + corner resize handle. The image is
    // baseline-aligned in its line, so its rect hangs from the bottom of the hit-test box.
    private void RegisterInlineImages(DrawingContext context, Paragraph p, TextLayout layout, double ox, double oy)
    {
        int off = 0;
        foreach (var inl in p.Inlines)
        {
            if (inl is InlineImage ii)
            {
                foreach (var r in layout.HitTestTextRange(off, 1))
                {
                    double w = Math.Max(8, ii.Width > 0 ? ii.Width : 16);
                    double h = Math.Max(8, ii.Height > 0 ? ii.Height : 16);
                    var ir = new Rect(ox + r.X, oy + r.Bottom - h, w, h);
                    _inlineImageRects.Add((ir, p, ii));
                    if (_selectedInline is { } sel && ReferenceEquals(sel.img, ii))
                    {
                        context.DrawRectangle(null, AccentPen2, ir);
                        var knob = new Rect(ir.Right - 5, ir.Bottom - 5, 10, 10);
                        context.FillRectangle(Brushes.White, knob);
                        context.DrawRectangle(null, AccentPen15, knob);
                        _inlineHandles.Add((new Rect(ir.Right - 9, ir.Bottom - 9, 18, 18), p, ii));
                    }
                    break;
                }
            }
            off += InlineLen(inl);
        }
    }
}
