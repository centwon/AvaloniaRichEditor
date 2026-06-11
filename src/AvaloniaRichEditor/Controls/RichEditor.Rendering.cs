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
    // Total rendered height of the document at the given content width (mirrors the render advancement).
    // Reported via MeasureOverride so the hosting ScrollViewer grows its scrollable extent with content.
    private double MeasureContentHeight(double width)
    {
        if (Document == null) return 0;
        double yOffset = 0;
        foreach (var block in Document.Blocks)
        {
            if (block is TableBlock tb) yOffset += LayoutTable(tb, 10 + tb.Indent, yOffset).TotalHeight + 10;
            else if (block is ImageBlock img) yOffset += (img.Height > 0 ? img.Height : 200) + 10;
            else if (block is DividerBlock) yOffset += DividerHeight;
            else if (block is Paragraph p)
            {
                if (BuildPlain(p) == "")
                    yOffset += p.MarginBottom + (!double.IsNaN(p.LineHeight) ? p.LineHeight : 20);
                else
                    yOffset += BuildTextLayout(p, Math.Max(10, width - 20 - ParaLeft(p))).Height + p.MarginBottom;
            }
        }
        return yOffset + 40; // a little breathing room at the bottom
    }

    private double _measuredHeight;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        double w = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0 ? 698 : availableSize.Width;
        _measuredHeight = Math.Max(MinHeight, MeasureContentHeight(w));
        return new Size(w, _measuredHeight);
    }

    /// <inheritdoc/>
    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
        => new RichEditorAutomationPeer(this);

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
        // Flush change events after the edit/caret move that scheduled this paint (off the render stack).
        RaisePendingChangeEvents();
        context.FillRectangle(Brushes.Transparent, new Rect(0, 0, Bounds.Width, 2000));
        if (Document == null) return;

        // Recomputed every render so resize handles track the current layout.
        _columnBoundaries.Clear();
        _rowBoundaries.Clear();
        _imageHandles.Clear();
        _inlineImageRects.Clear();
        _inlineHandles.Clear();

        TextPointer? selStart = null, selEnd = null;
        HashSet<Paragraph>? selectedParagraphs = null;
        if (_selectionStart.Paragraph != null && _selectionEnd.Paragraph != null && _selectionStart.CompareTo(_selectionEnd) != 0)
        {
            if (_selectionStart.CompareTo(_selectionEnd) < 0) { selStart = _selectionStart; selEnd = _selectionEnd; }
            else { selStart = _selectionEnd; selEnd = _selectionStart; }
            var allParas = GetAllParagraphsInOrder();
            int si = allParas.IndexOf(selStart.Paragraph);
            int ei = allParas.IndexOf(selEnd.Paragraph);
            if (si >= 0 && ei >= 0)
            {
                selectedParagraphs = new HashSet<Paragraph>();
                for (int idx = si; idx <= ei; idx++)
                    selectedParagraphs.Add(allParas[idx]);
            }
        }

        double yOffset = 0;
        double maxWidth = Bounds.Width;
        double listIndent = 10;
        Point? caretPoint = null;
        // Caret bar = glyph-sized and bottom-aligned in its line. On tall lines (an inline image
        // treated as a character) the caret keeps the adjacent text's height down at the baseline,
        // instead of a fixed-height bar floating at the line top far above the text.
        double caretHeight = 20;
        Rect? blockCaretRect = null; // when a block caret is active, the image/table it sits in front of
        int orderedIndex = 0; // running counter for consecutive ordered-list paragraphs

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

        foreach (var block in Document.Blocks)
        {
            if (block is TableBlock tb)
            {
                orderedIndex = 0;
                double startX = 10 + tb.Indent;
                double tableTop = yOffset;
                var tl = LayoutTable(tb, startX, tableTop);
                if (ReferenceEquals(tb, _caretBlock))
                    blockCaretRect = new Rect(startX, tableTop, tl.TableWidth, tl.TotalHeight);

                // Cull check: skip cell draws + resize-handle registration when fully off-screen
                // (off-screen handles can't be grabbed). Geometry (tl) is still computed above so
                // yOffset advances identically. A table being edited (caret in a cell) always draws.
                bool tbVisible = (tableTop + tl.TotalHeight >= visTop && tableTop <= visBottom)
                    || _caretPosition?.Paragraph?.Parent == tb
                    || ReferenceEquals(tb, _caretBlock) || ReferenceEquals(tb, _selectedBlock);
                if (!tbVisible)
                {
                    yOffset = tableTop + tl.TotalHeight + 10;
                    continue;
                }

                // When the drag spans multiple cells of this table, highlight the rectangular block fully
                // (Excel/Word style) instead of the linear text run; otherwise fall back to text highlight.
                var cellBlock = SelectedCellRange(tb);

                foreach (var (r, c, rect) in tl.AnchorRects)
                {
                    var cell = tb.Cells[r][c];
                    double cellWidth = rect.Width;

                    if (cell.Background != null)
                        context.FillRectangle(cell.Background, rect);
                    context.DrawRectangle(null, new Pen(Brushes.Gray, 1), rect);

                    bool cellHasPreedit = _caretPosition != null && _caretPosition.Paragraph == cell && !string.IsNullOrEmpty(_preeditText);
                    var layout = cellHasPreedit
                        ? BuildTextLayout(cell, Math.Max(10, cellWidth - 10), _caretPosition!.Offset, _preeditText)
                        : BuildTextLayout(cell, Math.Max(10, cellWidth - 10));

                    // A cell is in "cell-selection mode" when it's part of a multi-cell drag block, or its
                    // whole content is selected (Tab focus / triple-click). Such cells show a fill and NO
                    // caret. A bare caret (collapsed selection) means "editing text" and shows no fill —
                    // so the caret's presence vs. the fill cleanly distinguishes the two modes.
                    bool inBlock = cellBlock is { } cb && r >= cb.r0 && r <= cb.r1 && c >= cb.c0 && c <= cb.c1;
                    if (inBlock)
                    {
                        context.FillRectangle(SelectionBrush, rect);
                    }
                    else if (cellBlock == null && selectedParagraphs?.Contains(cell) == true)
                    {
                        // Selecting text within a single cell shows the usual text-run highlight (not a full
                        // cell fill) so it reads as "editing text", visibly distinct from a selected cell.
                        int cellLen = GetParagraphLength(cell);
                        int hlStart = (cell == selStart!.Paragraph) ? selStart.Offset : 0;
                        int hlEnd = (cell == selEnd!.Paragraph) ? selEnd.Offset : cellLen;
                        if (hlEnd > hlStart)
                            DrawSelectionHighlight(context, layout, hlStart, hlEnd, rect.X + 5, rect.Y + 5);
                    }

                    bool cellSelected = inBlock;
                    if (_caretPosition != null && _caretPosition.Paragraph == cell && (!cellSelected || cellHasPreedit))
                    {
                        int caretDisp = _caretPosition.Offset + (cellHasPreedit ? _preeditText!.Length : 0);
                        var cr = layout.HitTestTextPosition(caretDisp);
                        cr = FixCaretAfterTrailingImage(layout, cell, _caretPosition.Offset, caretDisp, cr);
                        double th = CaretTextHeight(cell, _caretPosition.Offset);
                        if (cr.Height > 0 && th > cr.Height) th = cr.Height;
                        caretPoint = new Point(rect.X + 5 + cr.X, rect.Y + 5 + cr.Y + Math.Max(0, cr.Height - th));
                        caretHeight = th;
                        _lastCaretPoint = caretPoint.Value;
                    }

                    layout.Draw(context, new Point(rect.X + 5, rect.Y + 5));
                    RegisterInlineImages(context, cell, layout, rect.X + 5, rect.Y + 5);
                }

                // Resize handles live on the physical grid lines (independent of merges). Internal column
                // edges redistribute width with the next column; the outer-right edge grows the last column.
                for (int r = 0; r < tb.Rows; r++)
                    _rowBoundaries.Add((new Rect(startX, tl.RowY[r + 1] - 3, tl.TableWidth, 6), tb, r, tl.RowY[r + 1] - tl.RowY[r]));
                for (int c = 0; c < tb.Columns; c++)
                    _columnBoundaries.Add((new Rect(tl.ColX[c + 1] - 3, tableTop, 6, tl.TotalHeight), tb, c));

                yOffset = tableTop + tl.TotalHeight;
                if (ReferenceEquals(tb, _selectedBlock))
                {
                    var tableRect = new Rect(startX, tableTop, tl.TableWidth, tl.TotalHeight);
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(50, 0, 120, 215)), tableRect);
                    context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)), 2), tableRect);
                }
                yOffset += 10;
            }
            else if (block is Paragraph paragraph)
            {
                string fullText = BuildPlain(paragraph);

                bool hasPreedit = _caretPosition != null && _caretPosition.Paragraph == paragraph && !string.IsNullOrEmpty(_preeditText);

                double px = ParaLeft(paragraph);

                // Ordered numbering runs continuously across consecutive ordered paragraphs; reset otherwise.
                if (paragraph.ListType != ListKind.Ordered) orderedIndex = 0;

                if (fullText == "" && !hasPreedit)
                {
                    if (paragraph.ListType != ListKind.None)
                        DrawListMarker(context, paragraph, paragraph.ListType == ListKind.Ordered ? ++orderedIndex : 0, px, yOffset);
                    if (_caretPosition != null && _caretPosition.Paragraph == paragraph)
                    {
                        caretPoint = new Point(px, yOffset);
                        _lastCaretPoint = caretPoint.Value;
                    }
                    yOffset += paragraph.MarginBottom + (!double.IsNaN(paragraph.LineHeight) ? paragraph.LineHeight : 20);
                    continue;
                }

                double pWidth = Math.Max(10, maxWidth - 20 - px);
                var layout = hasPreedit
                    ? BuildTextLayout(paragraph, pWidth, _caretPosition!.Offset, _preeditText)
                    : BuildTextLayout(paragraph, pWidth);

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

                if (_caretPosition != null && _caretPosition.Paragraph == paragraph)
                {
                    int caretDisp = _caretPosition.Offset + (hasPreedit ? _preeditText!.Length : 0);
                    var cr = layout.HitTestTextPosition(caretDisp);
                    cr = FixCaretAfterTrailingImage(layout, paragraph, _caretPosition.Offset, caretDisp, cr);
                    double th = CaretTextHeight(paragraph, _caretPosition.Offset);
                    if (cr.Height > 0 && th > cr.Height) th = cr.Height;
                    caretPoint = new Point(px + cr.X, yOffset + cr.Y + Math.Max(0, cr.Height - th));
                    caretHeight = th;
                    _lastCaretPoint = caretPoint.Value;
                }

                layout.Draw(context, new Point(px, yOffset));
                RegisterInlineImages(context, paragraph, layout, px, yOffset);
                yOffset += layout.Height + paragraph.MarginBottom;
            }
            else if (block is ImageBlock img)
            {
                orderedIndex = 0;
                double cullHeight = img.Height > 0 ? img.Height : 200;
                // Cull check before touching img.Image: the getter lazily decodes RawBytes (N6-2), so
                // skipping it means off-screen images are never decoded at all. yOffset advances by the
                // declared size, matching MeasureContentHeight's unconditional advance.
                if ((yOffset + cullHeight < visTop || yOffset > visBottom)
                    && !ReferenceEquals(img, _caretBlock) && !ReferenceEquals(img, _selectedBlock))
                {
                    yOffset += cullHeight + 10;
                    continue;
                }
                if (img.Image != null)
                {
                    double width = img.Width > 0 ? img.Width : 200;
                    double height = img.Height > 0 ? img.Height : 200;
                    double imgX = listIndent + img.Indent;
                    var imgRect = new Rect(imgX, yOffset, width, height);
                    context.DrawImage(img.Image, imgRect);
                    if (ReferenceEquals(img, _caretBlock)) blockCaretRect = imgRect;

                    bool imgSelected = ReferenceEquals(img, _selectedBlock);
                    if (imgSelected)
                    {
                        // Selection: translucent overlay + bold border.
                        context.FillRectangle(new SolidColorBrush(Color.FromArgb(60, 0, 120, 215)), imgRect);
                        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)), 2), imgRect);
                    }
                    // Thin border + bottom-right resize handle.
                    context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 120, 215)), 1), imgRect);
                    var handle = new Rect(imgX + width - 6, yOffset + height - 6, 12, 12);
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(230, 0, 120, 215)), handle);
                    // Slightly larger hit area than the visual handle for easier grabbing.
                    _imageHandles.Add((new Rect(imgX + width - 9, yOffset + height - 9, 18, 18), img));

                    yOffset += height + 10;
                }
            }
            else if (block is DividerBlock)
            {
                orderedIndex = 0;
                if (yOffset + DividerHeight >= visTop && yOffset <= visBottom)
                {
                    double y = yOffset + DividerHeight / 2;
                    context.DrawLine(new Pen(Brushes.Gray, 1), new Point(listIndent, y), new Point(Math.Max(listIndent + 1, maxWidth - 10), y));
                }
                yOffset += DividerHeight;
            }
        }

        if (blockCaretRect.HasValue)
        {
            // Blinking bar at the block's left edge: top = caret before the block, bottom = caret after.
            if (_isCaretVisible && IsFocused && !IsReadOnly)
            {
                var r = blockCaretRect.Value;
                double cx = r.X - 3;
                double cy1 = _caretBlockAfter ? Math.Max(r.Y, r.Bottom - 20) : r.Y;
                double cy2 = _caretBlockAfter ? r.Bottom : Math.Min(r.Bottom, r.Y + 20);
                context.DrawLine(new Pen(Brushes.Black, 2), new Point(cx, cy1), new Point(cx, cy2));
            }
        }
        else if (caretPoint.HasValue)
        {
            _lastCaretPoint = caretPoint.Value;
            _lastCaretHeight = caretHeight;
            if (_isCaretVisible && IsFocused && !IsReadOnly)
            {
                context.DrawLine(new Pen(CaretBrush, 1.5), caretPoint.Value, new Point(caretPoint.Value.X, caretPoint.Value.Y + caretHeight));
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
                ? new Rect(br.X, Math.Max(0, br.Y - m), 2, br.Height + 2 * m)
                : new Rect(_lastCaretPoint.X, Math.Max(0, _lastCaretPoint.Y - m), 2, _lastCaretHeight + 2 * m);
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
                        var accent = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));
                        context.DrawRectangle(null, new Pen(accent, 2), ir);
                        var knob = new Rect(ir.Right - 5, ir.Bottom - 5, 10, 10);
                        context.FillRectangle(Brushes.White, knob);
                        context.DrawRectangle(null, new Pen(accent, 1.5), knob);
                        _inlineHandles.Add((new Rect(ir.Right - 9, ir.Bottom - 9, 18, 18), p, ii));
                    }
                    break;
                }
            }
            off += InlineLen(inl);
        }
    }
}
