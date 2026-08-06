using HtmlAgilityPack;
using System.Collections.Generic;
using System.Linq;
using AvaloniaRichEditor.Documents;
using Avalonia.Media;
using System;
using System.Text;

namespace AvaloniaRichEditor.Formatters
{
    /// <summary>Converts between <see cref="FlowDocument"/> and HTML.
    /// Supports full round-trip for bold/italic/underline/strikethrough, colors, sizes, alignment,
    /// headings, lists, tables (with cell merge, per-cell background, nested tables), images,
    /// hyperlinks, and horizontal rules.
    /// <para>HTML has no inline table, so an <see cref="InlineTable"/> is emitted as a
    /// <c>&lt;table&gt;</c> carrying a <c>data-are-inline</c> marker; this parser reads that back onto the
    /// text line, while HTML from other applications keeps producing a block-level
    /// <see cref="TableBlock"/>.</para></summary>
    public static class HtmlDocumentFormatter
    {
        // Tags that introduce/contain block-level structure. Their presence means we must
        // recurse rather than flatten a container's whole subtree into one paragraph.
        private static readonly HashSet<string> BlockOrMedia = new(StringComparer.OrdinalIgnoreCase)
        {
            "div","p","table","ul","ol","li","img","h1","h2","h3","h4","h5","h6",
            "section","article","figure","figcaption","header","footer","main","aside","tr","blockquote","hr","pre"
        };

        // Block-level leaf tags that map to their own Paragraph when they have no nested blocks.
        private static readonly HashSet<string> BlockLeaf = new(StringComparer.OrdinalIgnoreCase)
        {
            "p","h1","h2","h3","h4","h5","h6","li","blockquote","div","section","article",
            "figure","figcaption","header","footer","main","aside","pre","caption"
        };

        // Shared across all parses: a new HttpClient per <img> leaks sockets.
        private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly TimeSpan RemoteImageBudget = TimeSpan.FromSeconds(5);
        // Per-parse flags (set from the parameters below); LoadImage is deeply nested, so these ride
        // on the thread instead of being threaded through every walker.
        [ThreadStatic] private static bool _blockLocalFileImages;
        // When set, remote (http) <img> sources are skipped entirely (no fetch) — the privacy opt-out
        // pasting HTML otherwise issues HTTP requests, e.g. to tracking pixels.
        [ThreadStatic] private static bool _blockRemoteImages;
        // When set (by ParseHtmlAsync), remote <img> bytes have already been fetched off the UI
        // thread; LoadImage reads them from here instead of blocking on a synchronous download.
        [ThreadStatic] private static System.Collections.Generic.Dictionary<string, byte[]?>? _prefetchedRemoteImages;

        /// <summary>Parses an HTML string into a <see cref="FlowDocument"/>.
        /// When <paramref name="allowLocalFileImages"/> is false, <c>file://</c> image sources are
        /// skipped instead of read from disk (see <see cref="Controls.RichEditor.AllowLocalFileImages"/>).
        /// Remote (<c>http</c>) images are <b>not</b> loaded: this overload never performs network I/O,
        /// so it cannot stall the calling thread. Use <see cref="ParseHtmlAsync"/> to fetch them.</summary>
        public static FlowDocument ParseHtml(string html, bool allowLocalFileImages = true, bool allowRemoteImages = true)
        {
            _blockLocalFileImages = !allowLocalFileImages;
            _blockRemoteImages = !allowRemoteImages;
            _prefetchedRemoteImages = null; // no prefetch => remote images are skipped, never fetched here
            var doc = LoadHtmlDoc(ref html);
            return BuildDocument(doc, html);
        }

        /// <summary>Same as <see cref="ParseHtml"/> but downloads remote (<c>http</c>) images
        /// concurrently off the UI thread first, so a slow network can't freeze the UI while pasting
        /// web content. The document model is still built on the calling thread (Avalonia model
        /// objects are thread-affine), so await this from the UI thread.</summary>
        public static async System.Threading.Tasks.Task<FlowDocument> ParseHtmlAsync(string html, bool allowLocalFileImages = true, bool allowRemoteImages = true)
        {
            var doc = LoadHtmlDoc(ref html);
            // Off-thread: network only — no model objects created here. Skip the network entirely when
            // remote images are opted out (privacy).
            var prefetched = allowRemoteImages
                ? await PrefetchRemoteImagesAsync(doc).ConfigureAwait(true)
                : new System.Collections.Generic.Dictionary<string, byte[]?>();
            // Back on the calling (UI) thread: build the model, reading the prefetched image bytes.
            _blockLocalFileImages = !allowLocalFileImages;
            _blockRemoteImages = !allowRemoteImages;
            _prefetchedRemoteImages = prefetched;
            try { return BuildDocument(doc, html); }
            finally { _prefetchedRemoteImages = null; }
        }

        // Excel's CF_HTML fragment markers can sit *inside* the <table>, so the extracted fragment
        // has orphan <tr>/<td> with no wrapping <table>. Wrap it so it's recognized.
        private static HtmlDocument LoadHtmlDoc(ref string html)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(html, "<tr[\\s>]", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && !System.Text.RegularExpressions.Regex.IsMatch(html, "<table", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                html = "<table>" + html + "</table>";
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }

        private static FlowDocument BuildDocument(HtmlDocument doc, string html)
        {
            var flowDoc = new FlowDocument();
            var root = doc.DocumentNode.Descendants("body").FirstOrDefault() ?? doc.DocumentNode;
            WalkBlocks(root, flowDoc);

            if (flowDoc.Blocks.Count == 0)
            {
                // The fallback is for input that was never markup — a caller handing us plain text should
                // get that text, not an empty document. It must NOT fire for input that WAS markup and
                // simply had no content: our own export of an empty document is `<p style="…"></p>`, whose
                // walk yields no block, and dumping the source then put the editor's own tags on screen as
                // literal body text (save an empty document as HTML, reopen it, and there they were).
                // An element node anywhere is the discriminator: markup in, empty paragraph out.
                bool wasMarkup = root.Descendants().Any(n => n.NodeType == HtmlNodeType.Element);
                var p = new Paragraph();
                if (!wasMarkup) p.Inlines.Add(new Run { Text = HtmlEntity.DeEntitize(html) });
                flowDoc.Blocks.Add(p);
            }
            return flowDoc;
        }

        // Downloads every distinct remote <img src="http…"> concurrently, sharing one 5 s budget for
        // the whole batch. Failures/timeouts map to null (the image is skipped, the rest is kept).
        private static async System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<string, byte[]?>> PrefetchRemoteImagesAsync(HtmlDocument doc)
        {
            var result = new System.Collections.Generic.Dictionary<string, byte[]?>();
            var urls = doc.DocumentNode.Descendants("img")
                .Select(n => n.GetAttributeValue("src", ""))
                .Where(s => s.StartsWith("http"))
                .Distinct()
                .ToList();
            if (urls.Count == 0) return result;

            using var cts = new System.Threading.CancellationTokenSource(RemoteImageBudget);
            async System.Threading.Tasks.Task<(string, byte[]?)> Fetch(string url)
            {
                try { return (url, await Http.GetByteArrayAsync(url, cts.Token).ConfigureAwait(false)); }
                catch (Exception ex) { RichEditorDiagnostics.Report(ex); return (url, null); }
            }
            foreach (var (url, bytes) in await System.Threading.Tasks.Task.WhenAll(urls.Select(Fetch)).ConfigureAwait(false))
                result[url] = bytes;
            return result;
        }

        // Recursively walks the DOM, emitting Paragraph/TableBlock/ImageBlock as it goes.
        // Consecutive inline siblings are accumulated into a single paragraph and flushed
        // whenever a block-level element is encountered.
        private static void WalkBlocks(HtmlNode node, FlowDocument flow, string? linkUri = null)
        {
            Paragraph? current = null;

            // A whitespace-only #text between inline siblings is a WORD SEPARATOR, not layout padding:
            // `<span>a</span> <span>b</span>` reads "a b" everywhere, and dropping it merged them ("ab").
            // MergeCells joins a covered cell's text with exactly that space, which is how a merged cell
            // lost a word boundary on the second HTML round trip.
            //
            // It is DEFERRED rather than appended on sight, and that distinction is the whole design: the
            // same whitespace before `</p>` is padding, which a browser drops — appending eagerly grew a
            // trailing space on every cycle, the "separator becomes content and accumulates" failure this
            // codebase has now hit several times.
            bool pendingSpace = false;
            void Flush()
            {
                if (current != null && current.Inlines.Count > 0) flow.Blocks.Add(current);
                current = null;
                pendingSpace = false; // never carries across a block boundary
            }

            // Call immediately before adding inline content, once that content is certain.
            void TakeSpace()
            {
                if (!pendingSpace) return;
                pendingSpace = false;
                if (current is { } p && p.Inlines.Count > 0 && p.Inlines[^1] is Run prev
                    && !string.IsNullOrEmpty(prev.Text)
                    && !prev.Text.EndsWith(" ", StringComparison.Ordinal)
                    && !prev.Text.EndsWith("\n", StringComparison.Ordinal))
                    p.Inlines.Add(new Run { Text = " " });
            }

            foreach (var child in node.ChildNodes)
            {
                string name = child.Name.ToLowerInvariant();

                // Propagate hyperlink context: an <a href> may wrap block-level content
                // (cards, headings, images). Children rendered as their own paragraphs must
                // still carry the link so the whole card is clickable.
                string? childLink = linkUri;
                if (name == "a")
                {
                    var href = child.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(href)) childLink = href;
                }
                bool hasLink = !string.IsNullOrEmpty(childLink);

                if (name == "table")
                {
                    var tbl = ParseTable(child);
                    // Our own export marks a table that was inline (see EmitTable): put it back on the text
                    // line instead of flushing the paragraph, following the same ladder as the small-icon
                    // <img> case — the pending paragraph, else the preceding one, else a new one.
                    if (tbl != null && child.GetAttributeValue("data-are-inline", "") == "1")
                    {
                        var it = new InlineTable { Table = tbl };
                        // `data-are-opens` says the table was the FIRST thing in its paragraph. There is
                        // then no earlier paragraph of its own to rejoin, and taking the preceding one
                        // merges two paragraphs and swallows it — and "a paragraph holding nothing but
                        // the table" is the ordinary shape of an inline table, so this is the common case.
                        bool opensParagraph = child.GetAttributeValue("data-are-opens", "") == "1";
                        if (current == null && !opensParagraph
                            && flow.Blocks.Count > 0 && flow.Blocks[flow.Blocks.Count - 1] is Paragraph lastPara)
                        {
                            // Reopen that paragraph as the pending one (Flush re-adds it): HTML parsers
                            // close a <p> when a <table> starts, so the text that followed the table
                            // arrives as a later sibling and has to land back on the same line.
                            flow.Blocks.RemoveAt(flow.Blocks.Count - 1);
                            current = lastPara;
                        }
                        current ??= new Paragraph();
                        TakeSpace();
                        current.Inlines.Add(it);
                        continue;
                    }
                    Flush();
                    if (tbl != null) flow.Blocks.Add(tbl);
                }
                else if (name == "img")
                {
                    var (bytes, bmp, w, h) = LoadImage(child);
                    if (bmp != null && bytes != null)
                    {
                        if (w < IconMaxSize && h < IconMaxSize)
                        {
                            // Small icon/logo -> keep on a text line rather than its own block.
                            var icon = new InlineImage { Width = w, Height = h };
                            icon.SetImageData(bytes, ImageMime.Detect(bytes), bmp);
                            TakeSpace();
                            // Same rule as an inline table: `data-are-opens` says the image began its own
                            // paragraph. A <p> holding nothing but an image is walked as a block (an <img>
                            // is block-or-media), so `current` is null by the time we get here, and
                            // rejoining the PRECEDING paragraph swallowed the image's line — on every
                            // second round trip a picture on its own line jumped up into the paragraph
                            // above it.
                            bool imgOpens = child.GetAttributeValue("data-are-opens", "") == "1";
                            if (current != null)
                                current.Inlines.Add(icon);                       // inline with pending text
                            else if (!imgOpens && flow.Blocks.Count > 0 && flow.Blocks[flow.Blocks.Count - 1] is Paragraph lastP)
                                lastP.Inlines.Add(icon);                          // join the preceding line (e.g. a title)
                            else
                            {
                                current = new Paragraph();
                                current.Inlines.Add(icon);
                            }
                        }
                        else
                        {
                            Flush();
                            var ib = new ImageBlock { Width = w, Height = h };
                            ib.SetImageData(bytes, ImageMime.Detect(bytes), bmp);
                            flow.Blocks.Add(ib);
                        }
                    }
                }
                else if (name == "ul" || name == "ol")
                {
                    Flush();
                    ParseList(child, flow, name == "ol" ? ListKind.Ordered : ListKind.Bullet, 0, linkUri);
                }
                else if (name == "hr")
                {
                    Flush();
                    flow.Blocks.Add(new DividerBlock());
                }
                else if (name == "br")
                {
                    // A bare space before a <br/> is padding: it renders at the end of a line, invisibly.
                    // A space this library MEANT to keep there is written as &nbsp; (see EmitInline), so
                    // it arrives as content and never reaches this branch.
                    pendingSpace = false;
                    current ??= new Paragraph();
                    current.Inlines.Add(new Run { Text = "\n" });
                }
                else if (name == "#text")
                {
                    string t = HtmlEntity.DeEntitize(child.InnerText);
                    if (!IsCollapsibleWhitespace(t))
                    {
                        TakeSpace();
                        current ??= new Paragraph();
                        current.Inlines.Add(new Run
                        {
                            Text = CollapseWhitespace(t),
                            NavigateUri = linkUri,
                            Foreground = hasLink ? Brushes.Blue : null
                        });
                    }
                    else if (current is { Inlines.Count: > 0 })
                    {
                        // Only between inline siblings: after a Flush() there is no pending paragraph, so
                        // the newlines a pretty-printer puts BETWEEN blocks stay ignored as before.
                        pendingSpace = true;
                    }
                }
                else if (name == "#comment" || name == "script" || name == "style" || name == "head" || name == "meta" || name == "link")
                {
                    // ignore
                }
                else if (HasBlockOrMedia(child))
                {
                    // Container with nested block/media content -> recurse to preserve structure.
                    Flush();
                    WalkBlocks(child, flow, childLink);
                }
                else if (BlockLeaf.Contains(name))
                {
                    // Block-level element with only inline content -> its own paragraph.
                    Flush();
                    int hl = (name.Length == 2 && name[0] == 'h' && name[1] >= '1' && name[1] <= '6') ? name[1] - '0' : 0;
                    var p = new Paragraph
                    {
                        HeadingLevel = hl,
                        Background = ReadBackground(child),
                        Indent = ReadIndentPx(child),
                        IsQuote = name == "blockquote",
                        TextAlignment = ReadAlign(child)
                    };
                    double size = HeadingSize(name, out var headingWeight);
                    ParseInlines(child, p, headingWeight, FontStyle.Normal, null, childLink, size, hasLink);
                    // Empty elements are dropped — foreign HTML uses them for spacing — unless this export
                    // marked one as a blank line the author actually typed (see data-are-empty).
                    if (p.Inlines.Count > 0 || child.GetAttributeValue("data-are-empty", "") == "1")
                        flow.Blocks.Add(p);
                }
                else
                {
                    // Inline element (span, a, b, i, font, ...) -> accumulate into current paragraph.
                    current ??= new Paragraph();
                    // Unlike the branches above, this one may contribute NOTHING (an empty or ignorable
                    // element), and a separator with no content after it is a trailing space — so take
                    // it back if nothing followed.
                    int before = current.Inlines.Count;
                    TakeSpace();
                    int afterSpace = current.Inlines.Count;
                    ParseInlines(child, current, uri: childLink, inLink: hasLink);
                    if (afterSpace > before && current.Inlines.Count == afterSpace)
                        current.Inlines.RemoveAt(afterSpace - 1);
                }
            }

            Flush();
        }

        // Recursively flattens a <ul>/<ol> into list-item paragraphs tagged with their nesting level.
        private static void ParseList(HtmlNode listNode, FlowDocument flow, ListKind kind, int level, string? linkUri)
        {
            // Bullet glyph / number format from the list's CSS list-style-type (Default when absent/unknown).
            var marker = ListMarkerFromCss(ReadStyleValue(listNode, "list-style-type"));

            // ONE pass, in document order. Two things live side by side here and the order between them
            // is the content's order, not a category order:
            //   <li>            — an item at this level.
            //   <ul>/<ol>       — a sublist that is a DIRECT child, with no <li> wrapping it. Our own
            //                     export makes exactly that shape whenever a deeper item follows a
            //                     shallower one (A / B-indented / C emits
            //                     <ul><li>A</li><ul><li>B</li></ul><li>C</li></ul>), and also for an item
            //                     with no shallower item above it at all (indent the only list item in a
            //                     document and you get <ol><ol><li>…).
            // Iterating only <li> never reached the second shape: those items VANISHED, and when they
            // were the whole document the parse produced zero blocks and the raw-text fallback dumped the
            // entire file as literal markup. Handling the two in separate passes — sublists first, then
            // items — fixes that but silently REORDERS the first, lifting every nested item above the one
            // it belongs under. Walking the children once is what gets both right.
            foreach (var child in listNode.ChildNodes)
            {
                bool isSub = child.Name.Equals("ul", StringComparison.OrdinalIgnoreCase)
                          || child.Name.Equals("ol", StringComparison.OrdinalIgnoreCase);
                if (isSub)
                {
                    ParseList(child, flow, child.Name.Equals("ol", StringComparison.OrdinalIgnoreCase) ? ListKind.Ordered : ListKind.Bullet,
                              level + 1, linkUri);
                    continue;
                }
                if (!child.Name.Equals("li", StringComparison.OrdinalIgnoreCase)) continue;

                var p = new Paragraph { ListType = kind, ListLevel = level, ListMarker = marker };
                // An <li> that was also a heading (see the export's data-are-h): HTML has no tag for both.
                int liHeading = child.GetAttributeValue("data-are-h", 0);
                if (liHeading >= 1 && liHeading <= 6) p.HeadingLevel = liHeading;
                ParseInlines(child, p, uri: linkUri, inLink: !string.IsNullOrEmpty(linkUri));
                if (p.Inlines.Count > 0) flow.Blocks.Add(p);

                // A sublist nested INSIDE the item (the shape most other producers emit) still follows it.
                foreach (var nested in child.ChildNodes.Where(n => n.Name.Equals("ul", StringComparison.OrdinalIgnoreCase) || n.Name.Equals("ol", StringComparison.OrdinalIgnoreCase)))
                    ParseList(nested, flow, nested.Name.Equals("ol", StringComparison.OrdinalIgnoreCase) ? ListKind.Ordered : ListKind.Bullet, level + 1, linkUri);
            }
        }

        // The raw value of a CSS property from a node's style attribute (e.g. "list-style-type" -> "circle"),
        // or null if absent.
        private static string? ReadStyleValue(HtmlNode node, string prop)
        {
            var style = node.GetAttributeValue("style", "");
            if (string.IsNullOrEmpty(style)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(style, prop + @"\s*:\s*([^;]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        // Paragraph text alignment from a node's align attr or style text-align.
        private static TextAlignment ReadAlign(HtmlNode node)
        {
            string a = node.GetAttributeValue("align", "").ToLowerInvariant();
            var style = node.GetAttributeValue("style", "").ToLowerInvariant();
            var m = System.Text.RegularExpressions.Regex.Match(style, "text-align\\s*:\\s*(left|center|right|justify)");
            if (m.Success) a = m.Groups[1].Value;
            return a switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, "justify" => TextAlignment.Justify, _ => TextAlignment.Left };
        }

        // Heading sizes in points (pt), mirroring RichEditor.HeadingFontSize so headings render and
        // round-trip consistently.
        private static double HeadingSize(string name, out FontWeight weight)
        {
            switch (name)
            {
                case "h1": weight = FontWeight.Bold; return 20;
                case "h2": weight = FontWeight.Bold; return 16;
                case "h3": weight = FontWeight.Bold; return 14;
                case "h4": weight = FontWeight.Bold; return 12;
                case "h5": weight = FontWeight.Bold; return 11;
                case "h6": weight = FontWeight.Bold; return 10;
                default: weight = FontWeight.Normal; return 10;
            }
        }

        private static bool HasBlockOrMedia(HtmlNode n) => n.Descendants().Any(d => BlockOrMedia.Contains(d.Name));

        // HTML collapses runs of COLLAPSIBLE whitespace to one space. A non-breaking space is not
        // collapsible — that is the whole point of it, and the export relies on it to carry the editor's
        // consecutive spaces (see PreserveRunsOfSpaces). Regex `\s` matches U+00A0 (Unicode class Zs), so
        // the old `\s+` folded exactly the character that was there to survive folding.
        //
        // The model has no non-breaking space of its own, so an nbsp becomes a plain space AFTER the
        // fold. Foreign HTML gains from this too: Word and HWP pad with runs of &nbsp;, which used to
        // arrive as a single space and now keep their width.
        private static string CollapseWhitespace(string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s, "[ \t\r\n\f\v]+", " ");
            return s.Replace(' ', ' ');
        }

        // Whitespace that HTML would collapse away, i.e. what makes a text node pure layout rather than
        // content. A node of nothing but &nbsp; is CONTENT and must not be mistaken for a separator,
        // which is why this is not string.IsNullOrWhiteSpace (that counts U+00A0 as whitespace).
        private static bool IsCollapsibleWhitespace(string s)
        {
            if (s.Length == 0) return true;
            foreach (char ch in s)
                if (ch is not (' ' or '\t' or '\r' or '\n' or '\f' or '\v')) return false;
            return true;
        }

        // Ceiling on the column count an imported table may claim. Foreign HTML controls colspan, and
        // the grid is allocated from it. Far beyond any real document (Word tops out at 63 columns).
        private const int MaxTableColumns = 1000;

        private static TableBlock? ParseTable(HtmlNode node)
        {
            var rows = node.Descendants("tr")
                // Exclude rows belonging to a nested table.
                .Where(tr => tr.Ancestors("table").FirstOrDefault() == node)
                .ToList();
            if (rows.Count == 0) return null;
            int R = rows.Count;

            var cellNodes = new List<List<HtmlNode>>();
            foreach (var tr in rows)
                cellNodes.Add(tr.ChildNodes.Where(n => n.Name == "td" || n.Name == "th").ToList());

            // Occupancy-fill pass: place each <td>/<th> at the next free column of its row, reserving
            // colspan×rowspan cells in a growable grid. This yields each cell's anchor column and the
            // true column count (which colspan/rowspan can push beyond a single row's cell count).
            var occupied = new List<List<bool>>();
            for (int i = 0; i < R; i++) occupied.Add(new List<bool>());
            var placements = new List<List<(int col, int cs, int rs, HtmlNode node)>>();
            for (int i = 0; i < R; i++) placements.Add(new List<(int, int, int, HtmlNode)>());
            int colCount = 0;

            static void Ensure(List<bool> row, int upTo) { while (row.Count <= upTo) row.Add(false); }

            for (int r = 0; r < R; r++)
            {
                int col = 0;
                foreach (var td in cellNodes[r])
                {
                    Ensure(occupied[r], col);
                    while (col < occupied[r].Count && occupied[r][col]) col++;
                    // Both spans are attacker-controlled (any pasted web page is foreign input) and the
                    // occupancy grid is sized from them, so both need a ceiling. rowspan is naturally
                    // bounded by the rows that actually exist; colspan had none, so a single
                    // colspan="100000000" grew the grid — and then the TableBlock — until the process
                    // ran out of memory. No real table is anywhere near the cap.
                    int cs = Math.Clamp(td.GetAttributeValue("colspan", 1), 1, MaxTableColumns);
                    int rs = Math.Max(1, Math.Min(td.GetAttributeValue("rowspan", 1), R - r));
                    placements[r].Add((col, cs, rs, td));
                    for (int rr = r; rr < r + rs; rr++)
                    {
                        Ensure(occupied[rr], col + cs - 1);
                        for (int cc = col; cc < col + cs; cc++) occupied[rr][cc] = true;
                    }
                    col += cs;
                    colCount = Math.Max(colCount, col);
                }
            }
            if (colCount == 0) return null;
            if (colCount > MaxTableColumns) colCount = MaxTableColumns;

            var tb = new TableBlock(R, colCount);
            // Restore per-column widths from <colgroup><col style="width:Npx">, if the export emitted them
            // (excludes a nested table's own cols). Falls back to the default width when absent.
            var colNodes = node.Descendants("col")
                .Where(co => co.Ancestors("table").FirstOrDefault() == node)
                .ToList();
            if (colNodes.Count > 0)
            {
                tb.ColumnWidths.Clear();
                for (int c = 0; c < colCount; c++)
                {
                    double cw = c < colNodes.Count ? ReadPx(colNodes[c], "width", "width") : 0;
                    tb.ColumnWidths.Add(cw > 0 ? cw : 100);
                }
            }
            for (int r = 0; r < R; r++)
                foreach (var (col, cs, rs, td) in placements[r])
                {
                    if (cs > 1 || rs > 1) tb.SetSpan(r, col, cs, rs);
                    var cell = tb.Cells[r][col];
                    cell.Background = ReadBackground(td); // cell-level background lives on the cell
                    // Parse the cell as blocks so nested tables / block images / multiple paragraphs survive
                    // the round-trip (mirrors the export's per-cell block emit). WalkBlocks yields the same
                    // block types as a top-level walk; a plain inline cell yields a single paragraph.
                    var cellFlow = new FlowDocument();
                    WalkBlocks(td, cellFlow);
                    cell.Blocks.Clear();
                    foreach (var b in cellFlow.Blocks) cell.Blocks.Add(b);
                    if (cell.Blocks.Count == 0) cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "" } } });
                }
            return tb;
        }

        // Images below this size (px, both dimensions) are treated as inline icons/logos/emoji
        // and skipped — this editor renders every image as its own block line, so tiny icons
        // would otherwise land on their own awkward line after each heading.
        private const double IconMaxSize = 64;

        // Loads an <img> and returns the original encoded bytes, the decoded bitmap, and its
        // intended display size (declared px when present, otherwise natural size).
        // Returns (null,null,0,0) on failure/unsupported source.
        private static (byte[]?, Avalonia.Media.Imaging.Bitmap?, double, double) LoadImage(HtmlNode node)
        {
            var src = node.GetAttributeValue("src", "");
            if (string.IsNullOrEmpty(src)) return (null, null, 0, 0);

            double declW = ReadPx(node, "width", "width");
            double declH = ReadPx(node, "height", "height");

            try
            {
                byte[]? bytes = null;
                if (src.StartsWith("data:image"))
                {
                    var comma = src.IndexOf(',');
                    if (comma >= 0) bytes = System.Convert.FromBase64String(src.Substring(comma + 1));
                }
                else if (src.StartsWith("http"))
                {
                    if (_blockRemoteImages) return (null, null, 0, 0); // remote images opted out
                    // Only the async path fetches. The synchronous parse never touches the network:
                    // downloading on the calling thread froze the UI for up to the whole budget, and a
                    // hung UI is a worse failure than a missing image (ParseHtmlAsync loads them).
                    if (_prefetchedRemoteImages == null) return (null, null, 0, 0);
                    // ParseHtmlAsync already fetched these off the UI thread; null = failed/timed out.
                    _prefetchedRemoteImages.TryGetValue(src, out bytes);
                    if (bytes == null) return (null, null, 0, 0);
                }
                else if (src.StartsWith("file:"))
                {
                    if (_blockLocalFileImages) return (null, null, 0, 0);
                    var path = new Uri(src).LocalPath;
                    if (System.IO.File.Exists(path)) bytes = System.IO.File.ReadAllBytes(path);
                }
                if (bytes == null) return (null, null, 0, 0);
                using var ms = new System.IO.MemoryStream(bytes);
                var bitmap = new Avalonia.Media.Imaging.Bitmap(ms);
                double w = (!double.IsNaN(declW) && declW > 0) ? declW : bitmap.Size.Width;
                double h = (!double.IsNaN(declH) && declH > 0) ? declH : bitmap.Size.Height;
                return (bytes, bitmap, w, h);
            }
            catch (Exception ex) { RichEditorDiagnostics.Report(ex); return (null, null, 0, 0); }
        }

        private static double ReadPx(HtmlNode node, string attr, string cssProp)
        {
            var a = node.GetAttributeValue(attr, "");
            if (double.TryParse(a, System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
            var style = node.GetAttributeValue("style", "");
            if (!string.IsNullOrEmpty(style))
            {
                var m = System.Text.RegularExpressions.Regex.Match(style, cssProp + "\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)px",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var px)) return px;
            }
            return double.NaN;
        }

        // `ownColor` is set once a `data-are-fg` span has been entered: the colour in scope is the
        // document's own, so the link-blue rule below must leave it alone. It is INHERITED rather than
        // re-read per node, because the marker sits on the span while the text it colours is its child.
        private static void ParseInlines(HtmlNode node, Paragraph p, FontWeight weight = FontWeight.Normal, FontStyle style = FontStyle.Normal, IBrush? color = null, string? uri = null, double baseSize = 10, bool inLink = false, IBrush? background = null, string? family = null, bool underline = false, bool strike = false, bool ownColor = false)
        {
            foreach (var child in node.ChildNodes)
            {
                var cw = weight;
                var cs = style;
                var cc = color;
                var cu = uri;
                double sz = baseSize;
                var cbg = background;
                var cfam = family;
                bool cunder = underline;
                bool cstrike = strike;

                string name = child.Name.ToLowerInvariant();

                if (name == "br") { p.Inlines.Add(new Run { Text = "\n" }); continue; }
                // Nested lists are block-level (handled by ParseList) — don't fold their text inline.
                if (name == "ul" || name == "ol") continue;

                if (name == "b" || name == "strong") cw = FontWeight.Bold;
                if (name == "i" || name == "em") cs = FontStyle.Italic;
                if (name == "u") cunder = true;
                if (name == "s" || name == "strike" || name == "del") cstrike = true;
                if (name == "h1") { cw = FontWeight.Bold; sz = 20; } // pt, mirrors HeadingSize
                if (name == "h2") { cw = FontWeight.Bold; sz = 16; }
                if (name == "h3") { cw = FontWeight.Bold; sz = 14; }

                bool childInLink = inLink || name == "a";
                if (name == "a")
                {
                    var href = child.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(href)) cu = href;
                }

                bool childOwnColor = ownColor || child.GetAttributeValue("data-are-fg", "") == "1";

                ApplyInlineStyle(child.GetAttributeValue("style", ""), ref cw, ref cs, ref cc, ref sz, ref cbg, ref cfam, ref cunder, ref cstrike);

                // Links stay visually distinct (blue) regardless of the SITE'S own inline color (e.g.
                // dark anchors or white button text), and get underlined via NavigateUri. A colour this
                // library wrote is not a site's styling, though, and overriding it lost the user's own
                // choice of link colour on every HTML save/load; `data-are-fg` marks that case.
                if (childInLink && !childOwnColor) cc = Brushes.Blue;

                if (name == "#text")
                {
                    string text = HtmlEntity.DeEntitize(child.InnerText);
                    if (IsCollapsibleWhitespace(text))
                    {
                        // Keep a single separating space between inline runs, but skip pure indentation.
                        // A node of nothing but &nbsp; is content, not indentation, and falls through.
                        if (p.Inlines.Count > 0 && p.Inlines[^1] is Run last && last.Text != null &&
                            !last.Text.EndsWith(" ") && !last.Text.EndsWith("\n"))
                            p.Inlines.Add(new Run { Text = " " });
                        continue;
                    }
                    p.Inlines.Add(new Run { Text = CollapseWhitespace(text), FontWeight = cw, FontStyle = cs, Foreground = cc, FontSize = sz, NavigateUri = cu, Background = cbg, FontFamily = cfam, TextDecorations = MakeDecorations(cunder, cstrike) });
                }
                else if (name == "img")
                {
                    var (bytes, bmp, w, h) = LoadImage(child);
                    if (bmp != null && bytes != null)
                    {
                        var im = new InlineImage { Width = w, Height = h };
                        im.SetImageData(bytes, ImageMime.Detect(bytes), bmp);
                        p.Inlines.Add(im);
                    }
                }
                else
                {
                    ParseInlines(child, p, cw, cs, cc, cu, sz, childInLink, cbg, cfam, cunder, cstrike, childOwnColor);
                }
            }
        }

        private static TextDecorationCollection? MakeDecorations(bool underline, bool strike)
        {
            if (!underline && !strike) return null;
            var c = new TextDecorationCollection();
            if (underline) c.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
            if (strike) c.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
            return c;
        }

        private static void ApplyInlineStyle(string styleAttr, ref FontWeight weight, ref FontStyle style, ref IBrush? color, ref double size, ref IBrush? background, ref string? family, ref bool underline, ref bool strike)
        {
            if (string.IsNullOrEmpty(styleAttr)) return;
            string s = styleAttr.ToLowerInvariant();

            // Scope to the declaration's own value: searching the whole style string made
            // "font-weight:normal;width:600px" bold. A number is compared (>= 600), so 650 works too.
            var fw = System.Text.RegularExpressions.Regex.Match(s, @"(?<![\w-])font-weight\s*:\s*([^;]+)");
            if (fw.Success)
            {
                string v = fw.Groups[1].Value.Trim();
                if (v.Contains("bold") // bold / bolder
                    || (double.TryParse(v, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double n) && n >= 600))
                    weight = FontWeight.Bold;
            }
            if (s.Contains("font-style:italic") || s.Contains("font-style: italic")) style = FontStyle.Italic;
            if (System.Text.RegularExpressions.Regex.IsMatch(s, "text-decoration[^;]*underline")) underline = true;
            if (System.Text.RegularExpressions.Regex.IsMatch(s, "text-decoration[^;]*line-through")) strike = true;

            // color: (but not background-color)
            var m = System.Text.RegularExpressions.Regex.Match(s, "(?<!background-)color\\s*:\\s*([^;]+)");
            if (m.Success)
            {
                var brush = ParseCssColor(m.Groups[1].Value.Trim());
                if (brush != null) color = brush;
            }

            var bm = System.Text.RegularExpressions.Regex.Match(s, "background(?:-color)?\\s*:\\s*([^;]+)");
            if (bm.Success)
            {
                var brush = ParseCssColor(bm.Groups[1].Value.Trim());
                if (brush != null) background = brush;
            }

            // font-family: read from the original (non-lowercased) attribute to preserve casing.
            var famMatch = System.Text.RegularExpressions.Regex.Match(styleAttr, "font-family\\s*:\\s*([^;]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (famMatch.Success)
            {
                // Take the first family from a fallback list and strip quotes, e.g. "'Times New Roman', serif" -> Times New Roman.
                string fam = famMatch.Groups[1].Value.Split(',')[0].Trim().Trim('\'', '"').Trim();
                if (!string.IsNullOrEmpty(fam)) family = fam;
            }

            // Accept px and pt (external editors/HWP paste often uses pt). The model stores pt, so pt
            // passes through and px (or a bare number) converts px -> pt (×72/96 = ×0.75).
            var fm = System.Text.RegularExpressions.Regex.Match(s, "font-size\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)\\s*(px|pt)?");
            if (fm.Success && double.TryParse(fm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out double val) && val > 0)
                size = fm.Groups[2].Value == "pt" ? val : val * 72.0 / 96.0;
        }

        // Left indent (px) from style margin-left / padding-left (px or pt).
        private static double ReadIndentPx(HtmlNode node)
        {
            var style = node.GetAttributeValue("style", "").ToLowerInvariant();
            var m = System.Text.RegularExpressions.Regex.Match(style, "margin-left\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)\\s*(px|pt)?");
            if (!m.Success) m = System.Text.RegularExpressions.Regex.Match(style, "padding-left\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)\\s*(px|pt)?");
            if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0)
                return m.Groups[2].Value == "pt" ? v * 96.0 / 72.0 : v;
            return 0;
        }

        // Background color from a node's style="background[-color]:..." or legacy bgcolor="..." attr.
        private static IBrush? ReadBackground(HtmlNode node)
        {
            var style = node.GetAttributeValue("style", "");
            if (!string.IsNullOrEmpty(style))
            {
                var m = System.Text.RegularExpressions.Regex.Match(style.ToLowerInvariant(), "background(?:-color)?\\s*:\\s*([^;]+)");
                if (m.Success)
                {
                    var b = ParseCssColor(m.Groups[1].Value.Trim());
                    if (b != null) return b;
                }
            }
            var bg = node.GetAttributeValue("bgcolor", "");
            return string.IsNullOrEmpty(bg) ? null : ParseCssColor(bg.Trim());
        }

        private static IBrush? ParseCssColor(string value)
        {
            value = value.Trim();
            var rgb = System.Text.RegularExpressions.Regex.Match(value, "rgba?\\(\\s*(\\d+)\\s*,\\s*(\\d+)\\s*,\\s*(\\d+)");
            if (rgb.Success)
            {
                byte r = (byte)Math.Clamp(int.Parse(rgb.Groups[1].Value), 0, 255);
                byte g = (byte)Math.Clamp(int.Parse(rgb.Groups[2].Value), 0, 255);
                byte b = (byte)Math.Clamp(int.Parse(rgb.Groups[3].Value), 0, 255);
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }
            if (value.StartsWith("#"))
            {
                try { return new SolidColorBrush(Color.Parse(value)); }
                catch (Exception ex) { RichEditorDiagnostics.Report(ex); return null; }
            }
            return value switch
            {
                "red" => Brushes.Red,
                "blue" => Brushes.Blue,
                "green" => Brushes.Green,
                "black" => Brushes.Black,
                "white" => Brushes.White,
                "gray" or "grey" => Brushes.Gray,
                "orange" => Brushes.Orange,
                _ => null
            };
        }

        // Maps a list marker style to the closest CSS list-style-type. Dash bullets and the ")" number
        // suffix have no CSS equivalent and degrade to disc/decimal (HTML export is lossy by design).
        private static string CssListStyle(ListKind kind, ListMarkerStyle marker) => marker switch
        {
            ListMarkerStyle.Circle => "circle",
            ListMarkerStyle.Square => "square",
            ListMarkerStyle.LowerAlpha => "lower-alpha",
            ListMarkerStyle.UpperAlpha => "upper-alpha",
            ListMarkerStyle.LowerRoman => "lower-roman",
            _ => kind == ListKind.Ordered ? "decimal" : "disc",
        };

        // Reverse of CssListStyle: a CSS list-style-type value to a marker style (Default when unknown).
        private static ListMarkerStyle ListMarkerFromCss(string? cssValue) => (cssValue ?? "").Trim().ToLowerInvariant() switch
        {
            "circle" => ListMarkerStyle.Circle,
            "square" => ListMarkerStyle.Square,
            "lower-alpha" or "lower-latin" => ListMarkerStyle.LowerAlpha,
            "upper-alpha" or "upper-latin" => ListMarkerStyle.UpperAlpha,
            "lower-roman" => ListMarkerStyle.LowerRoman,
            _ => ListMarkerStyle.Default,
        };

        /// <summary>Serializes <paramref name="doc"/> to an HTML string.</summary>
        public static string ToHtml(FlowDocument doc)
        {
            var sb = new StringBuilder();
            var listStack = new System.Collections.Generic.List<ListKind>(); // open <ul>/<ol> per nesting level

            void CloseOne()
            {
                sb.Append(listStack[^1] == ListKind.Ordered ? "</ol>\n" : "</ul>\n");
                listStack.RemoveAt(listStack.Count - 1);
            }
            void CloseAll() { while (listStack.Count > 0) CloseOne(); }
            void SyncList(ListKind kind, ListMarkerStyle marker, int level)
            {
                while (listStack.Count > level + 1) CloseOne();
                if (listStack.Count == level + 1 && listStack[^1] != kind) CloseOne();
                // Explicit list-style-type: Word's clipboard import otherwise renders <ol> as bullets
                // instead of numbers. The bullet glyph / number format maps to the closest CSS value
                // (the ")" suffix and the dash bullet have no CSS equivalent — lossy, by design for HTML).
                while (listStack.Count < level + 1)
                {
                    string lst = CssListStyle(kind, marker);
                    sb.Append(kind == ListKind.Ordered ? $"<ol style=\"list-style-type:{lst}\">\n" : $"<ul style=\"list-style-type:{lst}\">\n");
                    listStack.Add(kind);
                }
            }

            foreach (var block in doc.Blocks)
            {
                if (block is Paragraph p)
                {
                    if (p.IsListItem) SyncList(p.ListType, p.ListMarker, p.ListLevel);
                    else CloseAll();

                    string tag = p.IsListItem ? "li"
                        : p.IsQuote ? "blockquote"
                        : (p.HeadingLevel >= 1 && p.HeadingLevel <= 6 ? $"h{p.HeadingLevel}" : "p");
                    string align = p.TextAlignment switch { TextAlignment.Center => "center", TextAlignment.Right => "right", TextAlignment.Justify => "justify", _ => "left" };
                    string pStyle = $"text-align:{align};";
                    if (p.Background is ISolidColorBrush pbg) pStyle += $"background-color:{CssColor(pbg.Color)};";
                    if (p.Indent > 0) pStyle += $"margin-left:{p.Indent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}px;";
                    // A paragraph can be a list item AND a heading, but the tag can only be one of
                    // <li>/<h1..6>, and <li> wins because the list structure is what HTML cannot
                    // otherwise express. The heading level would then be dropped outright, so it rides
                    // along as a marker. An empty paragraph is likewise a blank LINE the author typed,
                    // and the importer drops elements with no inline content (foreign HTML is full of
                    // empty <p>/<div> used for spacing) — so it is marked too.
                    string extraAttr = p.IsListItem && p.HeadingLevel >= 1 && p.HeadingLevel <= 6
                        ? $" data-are-h=\"{p.HeadingLevel}\"" : "";
                    if (p.Inlines.Count == 0) extraAttr += " data-are-empty=\"1\"";
                    sb.Append($"<{tag}{extraAttr} style=\"{pStyle}\">");
                    for (int i = 0; i < p.Inlines.Count; i++)
                        EmitInline(sb, p.Inlines[i], i == 0, i == p.Inlines.Count - 1);
                    sb.Append($"</{tag}>\n");
                }
                else if (block is DividerBlock)
                {
                    CloseAll();
                    sb.Append("<hr/>\n");
                }
                else if (block is TableBlock tb)
                {
                    CloseAll();
                    EmitTable(sb, tb);
                }
                else if (block is ImageBlock ib && (ib.RawBytes != null || ib.Image != null))
                {
                    // RawBytes checked first so export doesn't force a lazy bitmap decode.
                    CloseAll();
                    sb.Append($"<p>{ImgTag(ib.RawBytes, ib.MimeType, ib.RawBytes == null ? ib.Image : null, ib.Width, ib.Height)}</p>\n");
                }
            }
            CloseAll();
            return sb.ToString();
        }

        // Emits a table as an HTML <table>. Shared by block tables and inline tables (milestone B). Cell
        // content emits every paragraph (separated by <br>), block images, and nested tables (recursing),
        // so the structure survives a copy to Word/HWP.
        private static double SumColumnWidths(TableBlock tb)
        {
            double w = 0;
            for (int c = 0; c < tb.Columns; c++) w += c < tb.ColumnWidths.Count ? tb.ColumnWidths[c] : 100;
            return w;
        }

        // `opensParagraph` says this inline table was the FIRST thing in its paragraph, so on import there
        // is no earlier paragraph of its own to rejoin — see the reader's data-are-opens handling.
        private static void EmitTable(StringBuilder sb, TableBlock tb, bool asInline = false, bool opensParagraph = false)
        {
            // `data-are-inline` is ours: HTML has no inline table, so an InlineTable came back from our own
            // export as a block table, splitting the paragraph it lived in. External HTML never carries the
            // attribute and keeps landing as a block table, as before.
            string mark = asInline ? " data-are-inline=\"1\"" : "";
            if (asInline && opensParagraph) mark += " data-are-opens=\"1\"";
            // A block table fills the text column; an inline table is a character-sized object, so
            // stretching it to 100% turned it into a full-width band on its own line in every consumer
            // but our own importer. Size it to its own columns and let it sit in the line instead.
            string sizing = asInline
                ? $"width:{(int)System.Math.Max(1, SumColumnWidths(tb))}px; display:inline-table; vertical-align:middle;"
                : "width:100%;";
            sb.Append($"<table{mark} border=\"1\" style=\"border-collapse:collapse; {sizing}\">\n");
            // Per-column widths as a <colgroup> so the import restores the exact column proportions
            // (without it every column came back at the default width — the table looked squished).
            if (tb.ColumnWidths.Count > 0)
            {
                sb.Append("<colgroup>");
                for (int c = 0; c < tb.Columns; c++)
                {
                    double cw = c < tb.ColumnWidths.Count ? tb.ColumnWidths[c] : 100;
                    sb.Append($"<col style=\"width:{(int)System.Math.Max(1, cw)}px\"/>");
                }
                sb.Append("</colgroup>\n");
            }
            for (int r = 0; r < tb.Rows; r++)
            {
                sb.Append("<tr>\n");
                for (int c = 0; c < tb.Columns; c++)
                {
                    if (tb.IsCovered(r, c)) continue; // covered cells are emitted via their anchor's span
                    var cell = tb.Cells[r][c];
                    var (cs, rs) = tb.SpanOf(r, c);
                    var span = (cs > 1 ? $" colspan=\"{cs}\"" : "") + (rs > 1 ? $" rowspan=\"{rs}\"" : "");
                    if (cell.Background is ISolidColorBrush cbg)
                        sb.Append($"<td{span} style=\"background-color:{CssColor(cbg.Color)}\">");
                    else
                        sb.Append($"<td{span}>");
                    bool firstCellPara = true;
                    foreach (var cblk in cell.Blocks)
                    {
                        if (cblk is Paragraph cpara)
                        {
                            if (!firstCellPara) sb.Append("<br>");
                            firstCellPara = false;
                            // Same boundary rule as a top-level paragraph: a <td>'s content is parsed as
                            // inline, so a space at its end is dropped unless it goes out non-breaking.
                            for (int i = 0; i < cpara.Inlines.Count; i++)
                                EmitInline(sb, cpara.Inlines[i], i == 0, i == cpara.Inlines.Count - 1);
                        }
                        else if (cblk is ImageBlock cib && (cib.RawBytes != null || cib.Image != null))
                            sb.Append(ImgTag(cib.RawBytes, cib.MimeType, cib.RawBytes == null ? cib.Image : null, cib.Width, cib.Height));
                        else if (cblk is TableBlock nt)
                            EmitTable(sb, nt); // nested table
                        else if (cblk is DividerBlock)
                            sb.Append("<hr/>");
                    }
                    sb.Append("</td>\n");
                }
                sb.Append("</tr>\n");
            }
            // An inline table sits INSIDE a text line, so the pretty-printing newline after </table>
            // becomes a whitespace text node between the table and the text that follows it — which the
            // parser normalizes to a space, inserting one after every inline table on each save/load.
            sb.Append(asInline ? "</table>" : "</table>\n");
        }

        // Emits a single inline (Run with all its styling, an inline image, or an inline table) as HTML.
        // `opensParagraph`/`closesParagraph` mark the first and last inline of their paragraph. The first
        // drives the "this opened its own paragraph" marker on images and tables; the last gates the
        // trailing-space encoding, because HTML drops whitespace at the end of a block.
        private static void EmitInline(StringBuilder sb, Inline inline, bool opensParagraph = false, bool closesParagraph = false)
        {
            if (inline is InlineImage im && (im.RawBytes != null || im.Image != null))
            {
                sb.Append(ImgTag(im.RawBytes, im.MimeType, im.RawBytes == null ? im.Image : null, im.Width, im.Height, opensParagraph));
                return;
            }
            // An inline table has no HTML inline equivalent; emit it as a <table> so its content survives
            // (it pastes as a block-level table into Word/HWP rather than truly in-line — best effort).
            if (inline is InlineTable itbl)
            {
                EmitTable(sb, itbl.Table, asInline: true, opensParagraph);
                return;
            }
            if (inline is not Run r || r.Text == null) return;

            string t = HtmlEntity.Entitize(r.Text);
            t = PreserveRunsOfSpaces(t);
            t = PreserveDroppableSpaces(t, closesParagraph);

            var styles = new System.Collections.Generic.List<string>();
            // Quote the family name: a multi-word value (e.g. Times New Roman) unquoted is invalid CSS,
            // and Word/HWP then drop the ENTIRE style declaration — taking size/colour with it.
            if (!string.IsNullOrEmpty(r.FontFamily)) styles.Add($"font-family:'{AttrEscape(r.FontFamily).Replace("'", "")}'");
            // Size in pt, not px: Word/HWP clipboard import ignores px font-size (a well-known quirk) but
            // honours pt. The model already stores pt, so emit it directly (skip the 10pt body default).
            if (r.FontSize > 0 && System.Math.Abs(r.FontSize - 10) > 0.01)
                styles.Add($"font-size:{r.FontSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}pt");
            if (r.Foreground is ISolidColorBrush fg) styles.Add($"color:{CssColor(fg.Color)}");
            if (r.Background is ISolidColorBrush bg) styles.Add($"background-color:{CssColor(bg.Color)}");

            // `data-are-fg` says the colour on this span is the DOCUMENT'S, not a site's styling. The
            // reader paints links blue on top of whatever colour the source declared (a deliberate rule:
            // foreign pages give anchors dark or white button text that would vanish here), and that rule
            // used to eat the user's own choice of link colour on every save/load. The marker is what
            // tells the two apart — same idiom as data-are-inline. Emitted only where it can matter.
            bool markOwnColor = r.Foreground is ISolidColorBrush && !string.IsNullOrEmpty(r.NavigateUri);
            if (styles.Count > 0)
                t = $"<span{(markOwnColor ? " data-are-fg=\"1\"" : "")} style=\"{string.Join(";", styles)}\">{t}</span>";
            // Underline/strikethrough as <u>/<s> TAGS, not CSS text-decoration: clipboard importers
            // (Word/HWP) reliably honour the tags but routinely drop CSS text-decoration — and an
            // unrecognized decoration declaration can make Word discard the whole style (losing colour).
            if (HasDecoration(r.TextDecorations, TextDecorationLocation.Underline)) t = $"<u>{t}</u>";
            if (HasDecoration(r.TextDecorations, TextDecorationLocation.Strikethrough)) t = $"<s>{t}</s>";
            if (r.FontWeight == FontWeight.Bold) t = $"<b>{t}</b>";
            if (r.FontStyle == FontStyle.Italic) t = $"<i>{t}</i>";
            if (!string.IsNullOrEmpty(r.NavigateUri)) t = $"<a href=\"{AttrEscape(r.NavigateUri)}\">{t}</a>";
            sb.Append(t);
        }

        // Escapes a value placed inside a DOUBLE-quoted HTML attribute (style/href/src). Double quotes
        // are used throughout the export because clipboard-HTML consumers (Word/HWP) parse single-quoted
        // attributes unreliably — single-quoted style attributes were silently dropped on paste.
        private static string AttrEscape(string s) =>
            s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static bool HasDecoration(TextDecorationCollection? decos, TextDecorationLocation loc)
        {
            if (decos == null) return false;
            foreach (var d in decos) if (d.Location == loc) return true;
            return false;
        }

        // CSS-safe color: #RRGGBB when opaque, else rgba(). (Avalonia's Color.ToString() is #AARRGGBB, invalid in CSS.)
        private static string CssColor(Color c) =>
            c.A == 255
                ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)})";

        // HTML collapses a run of whitespace to ONE space, so `a  b` came back as `a b` — the editor's own
        // double space, gone on the first save/load. Encode every space that FOLLOWS a space as &nbsp;,
        // which is what Word emits and what every browser renders identically.
        //
        // Why alternate instead of making them all non-breaking: a solid run of &nbsp; is unbreakable, so
        // a line padded with spaces would refuse to wrap and push the layout wide. Keeping the first space
        // of each run collapsible leaves a legal wrap point exactly where one belongs.
        //
        // Only runs of two or more are touched, so ordinary prose exports byte-for-byte as before.
        private static string PreserveRunsOfSpaces(string s)
        {
            if (s.Length < 2 || !s.Contains("  ", StringComparison.Ordinal)) return s;
            var sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == ' ' && i > 0 && s[i - 1] == ' ') sb.Append("&nbsp;");
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // Spaces that land where HTML throws whitespace away, made non-breaking so they come back. Three
        // positions, all found by a fuzz rather than by reading:
        //
        // · Before a soft break. `t.Replace("\n", "<br/>")` splits the run's text, so `" \nx"` leaves a
        //   whitespace-ONLY text node in front of the <br/>; when that node opens the paragraph there is
        //   no previous inline to hang a separator on and the space is simply gone.
        // · At the very end of a block, which HTML drops outright. A paragraph ending in a plain `" "` run
        //   — what MergeCells leaves when it joins a covered cell — lost it on every other round trip.
        // · A run of NOTHING BUT SPACES, wherever it sits. It goes out as a whitespace-only text node and
        //   the reader cannot tell that from a pretty-printer's indentation, so its separator logic
        //   decides the fate of authored content. A run made only of spaces is authored by construction —
        //   it exists as its own run — so it is written as content and the question never arises.
        //   Cost, accepted: that one space is non-breaking, so a line cannot wrap at it.
        //
        // Deliberately NOT every boundary space: making every run-boundary space non-breaking would weld
        // words together and stop the line wrapping between them, which is the one thing &nbsp; must not
        // be used for. Encoding the leading spaces of any opening run was tried in the port and reverted —
        // it turned one leading space into two on the next cycle in 71 of 3000 fuzz seeds.
        private static string PreserveDroppableSpaces(string s, bool atEnd)
        {
            if (s.Length == 0) return s;
            if (s.AsSpan().TrimStart(' ').Length == 0)
                return string.Concat(Enumerable.Repeat("&nbsp;", s.Length));
            if (s.Contains(" \n", StringComparison.Ordinal))
            {
                var sb = new StringBuilder(s.Length + 16);
                for (int i = 0; i < s.Length; i++)
                {
                    if (s[i] == ' ' && i + 1 < s.Length && s[i + 1] == '\n') sb.Append("&nbsp;");
                    else sb.Append(s[i]);
                }
                s = sb.ToString();
            }
            if (!atEnd || s.Length == 0 || s[^1] != ' ') return s;
            int j = s.Length;
            while (j > 0 && s[j - 1] == ' ') j--;
            return s[..j] + string.Concat(Enumerable.Repeat("&nbsp;", s.Length - j));
        }

        // Emits a data: URI <img>. RawBytes (with their MIME type) are used verbatim when present;
        // a bitmap set without bytes is PNG-encoded as before.
        // `opensParagraph` carries the same meaning as it does for an inline table: this image was the
        // FIRST thing in its paragraph, so on import there is no earlier paragraph of its own to rejoin.
        private static string ImgTag(byte[]? raw, string? mime, Avalonia.Media.Imaging.Bitmap? bmp, double w, double h, bool opensParagraph = false)
        {
            string b64, m;
            if (raw != null)
            {
                b64 = System.Convert.ToBase64String(raw);
                m = mime ?? "image/png";
            }
            else if (bmp != null)
            {
                try
                {
                    using var ms = new System.IO.MemoryStream();
                    bmp.Save(ms);
                    b64 = System.Convert.ToBase64String(ms.ToArray());
                }
                catch (Exception ex) { RichEditorDiagnostics.Report(ex); return ""; }
                m = "image/png";
            }
            else return "";
            string size = "";
            if (!double.IsNaN(w) && w > 0) size += $" width=\"{(int)w}\"";
            if (!double.IsNaN(h) && h > 0) size += $" height=\"{(int)h}\"";
            if (opensParagraph) size += " data-are-opens=\"1\"";
            return $"<img src=\"data:{m};base64,{b64}\"{size}/>";
        }
    }
}
