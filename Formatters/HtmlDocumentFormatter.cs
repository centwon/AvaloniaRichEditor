using HtmlAgilityPack;
using System.Collections.Generic;
using System.Linq;
using AvaloniaRichTextBoxPort.Documents;
using Avalonia.Media;
using System;
using System.Text;

namespace AvaloniaRichTextBoxPort.Formatters
{
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

        public static FlowDocument ParseHtml(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var flowDoc = new FlowDocument();
            var root = doc.DocumentNode.Descendants("body").FirstOrDefault() ?? doc.DocumentNode;
            WalkBlocks(root, flowDoc);

            if (flowDoc.Blocks.Count == 0)
            {
                var p = new Paragraph();
                p.Inlines.Add(new Run { Text = HtmlEntity.DeEntitize(html) });
                flowDoc.Blocks.Add(p);
            }
            return flowDoc;
        }

        // Recursively walks the DOM, emitting Paragraph/TableBlock/ImageBlock as it goes.
        // Consecutive inline siblings are accumulated into a single paragraph and flushed
        // whenever a block-level element is encountered.
        private static void WalkBlocks(HtmlNode node, FlowDocument flow, string? linkUri = null)
        {
            Paragraph? current = null;
            void Flush()
            {
                if (current != null && current.Inlines.Count > 0) flow.Blocks.Add(current);
                current = null;
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
                    Flush();
                    var tbl = ParseTable(child);
                    if (tbl != null) flow.Blocks.Add(tbl);
                }
                else if (name == "img")
                {
                    var (bmp, w, h) = LoadImage(child);
                    if (bmp != null)
                    {
                        if (w < IconMaxSize && h < IconMaxSize)
                        {
                            // Small icon/logo -> keep on a text line rather than its own block.
                            var icon = new InlineImage { Image = bmp, Width = w, Height = h };
                            if (current != null)
                                current.Inlines.Add(icon);                       // inline with pending text
                            else if (flow.Blocks.Count > 0 && flow.Blocks[flow.Blocks.Count - 1] is Paragraph lastP)
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
                            flow.Blocks.Add(new ImageBlock { Image = bmp, Width = w, Height = h });
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
                    current ??= new Paragraph();
                    current.Inlines.Add(new Run { Text = "\n" });
                }
                else if (name == "#text")
                {
                    string t = HtmlEntity.DeEntitize(child.InnerText);
                    if (!string.IsNullOrWhiteSpace(t))
                    {
                        current ??= new Paragraph();
                        current.Inlines.Add(new Run
                        {
                            Text = CollapseWhitespace(t),
                            NavigateUri = linkUri,
                            Foreground = hasLink ? Brushes.Blue : null
                        });
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
                    if (p.Inlines.Count > 0) flow.Blocks.Add(p);
                }
                else
                {
                    // Inline element (span, a, b, i, font, ...) -> accumulate into current paragraph.
                    current ??= new Paragraph();
                    ParseInlines(child, current, uri: childLink, inLink: hasLink);
                }
            }

            Flush();
        }

        // Recursively flattens a <ul>/<ol> into list-item paragraphs tagged with their nesting level.
        private static void ParseList(HtmlNode listNode, FlowDocument flow, ListKind kind, int level, string? linkUri)
        {
            foreach (var li in listNode.ChildNodes.Where(n => n.Name.Equals("li", StringComparison.OrdinalIgnoreCase)))
            {
                var p = new Paragraph { ListType = kind, ListLevel = level };
                ParseInlines(li, p, uri: linkUri, inLink: !string.IsNullOrEmpty(linkUri));
                if (p.Inlines.Count > 0) flow.Blocks.Add(p);

                foreach (var nested in li.ChildNodes.Where(n => n.Name.Equals("ul", StringComparison.OrdinalIgnoreCase) || n.Name.Equals("ol", StringComparison.OrdinalIgnoreCase)))
                    ParseList(nested, flow, nested.Name.Equals("ol", StringComparison.OrdinalIgnoreCase) ? ListKind.Ordered : ListKind.Bullet, level + 1, linkUri);
            }
        }

        // Paragraph text alignment from a node's align attr or style text-align.
        private static TextAlignment ReadAlign(HtmlNode node)
        {
            string a = node.GetAttributeValue("align", "").ToLowerInvariant();
            var style = node.GetAttributeValue("style", "").ToLowerInvariant();
            var m = System.Text.RegularExpressions.Regex.Match(style, "text-align\\s*:\\s*(left|center|right|justify)");
            if (m.Success) a = m.Groups[1].Value;
            return a switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left };
        }

        private static double HeadingSize(string name, out FontWeight weight)
        {
            switch (name)
            {
                case "h1": weight = FontWeight.Bold; return 24;
                case "h2": weight = FontWeight.Bold; return 20;
                case "h3": weight = FontWeight.Bold; return 16;
                case "h4": case "h5": case "h6": weight = FontWeight.Bold; return 14;
                default: weight = FontWeight.Normal; return 14;
            }
        }

        private static bool HasBlockOrMedia(HtmlNode n) => n.Descendants().Any(d => BlockOrMedia.Contains(d.Name));

        private static string CollapseWhitespace(string s)
        {
            return System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ");
        }

        private static TableBlock? ParseTable(HtmlNode node)
        {
            var rows = node.Descendants("tr")
                // Exclude rows belonging to a nested table.
                .Where(tr => tr.Ancestors("table").FirstOrDefault() == node)
                .ToList();
            if (rows.Count == 0) return null;

            int colCount = 0;
            foreach (var tr in rows)
                colCount = Math.Max(colCount, tr.ChildNodes.Count(n => n.Name == "td" || n.Name == "th"));
            if (colCount == 0) return null;

            var tb = new TableBlock(rows.Count, colCount);
            for (int r = 0; r < rows.Count; r++)
            {
                var cells = rows[r].ChildNodes.Where(n => n.Name == "td" || n.Name == "th").ToList();
                for (int c = 0; c < Math.Min(colCount, cells.Count); c++)
                {
                    tb.Cells[r][c].Inlines.Clear();
                    ParseInlines(cells[c], tb.Cells[r][c]);
                    tb.Cells[r][c].Background = ReadBackground(cells[c]);
                    if (tb.Cells[r][c].Inlines.Count == 0)
                        tb.Cells[r][c].Inlines.Add(new Run { Text = "" });
                }
            }
            return tb;
        }

        // Images below this size (px, both dimensions) are treated as inline icons/logos/emoji
        // and skipped — this editor renders every image as its own block line, so tiny icons
        // would otherwise land on their own awkward line after each heading.
        private const double IconMaxSize = 64;

        // Loads an <img> and returns the bitmap plus its intended display size (declared px when
        // present, otherwise natural size). Returns (null,0,0) on failure/unsupported source.
        private static (Avalonia.Media.Imaging.Bitmap?, double, double) LoadImage(HtmlNode node)
        {
            var src = node.GetAttributeValue("src", "");
            if (string.IsNullOrEmpty(src)) return (null, 0, 0);

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
                    using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    bytes = client.GetByteArrayAsync(src).Result;
                }
                else if (src.StartsWith("file:"))
                {
                    var path = new Uri(src).LocalPath;
                    if (System.IO.File.Exists(path)) bytes = System.IO.File.ReadAllBytes(path);
                }
                if (bytes == null) return (null, 0, 0);
                using var ms = new System.IO.MemoryStream(bytes);
                var bitmap = new Avalonia.Media.Imaging.Bitmap(ms);
                double w = (!double.IsNaN(declW) && declW > 0) ? declW : bitmap.Size.Width;
                double h = (!double.IsNaN(declH) && declH > 0) ? declH : bitmap.Size.Height;
                return (bitmap, w, h);
            }
            catch { return (null, 0, 0); }
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

        private static void ParseInlines(HtmlNode node, Paragraph p, FontWeight weight = FontWeight.Normal, FontStyle style = FontStyle.Normal, IBrush? color = null, string? uri = null, double baseSize = 14, bool inLink = false, IBrush? background = null, string? family = null, bool underline = false, bool strike = false)
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
                if (name == "h1") { cw = FontWeight.Bold; sz = 24; }
                if (name == "h2") { cw = FontWeight.Bold; sz = 20; }
                if (name == "h3") { cw = FontWeight.Bold; sz = 16; }

                bool childInLink = inLink || name == "a";
                if (name == "a")
                {
                    var href = child.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(href)) cu = href;
                }

                ApplyInlineStyle(child.GetAttributeValue("style", ""), ref cw, ref cs, ref cc, ref sz, ref cbg, ref cfam, ref cunder, ref cstrike);

                // Links stay visually distinct (blue) regardless of the site's own inline color
                // (e.g. dark anchors or white button text), and get underlined via NavigateUri.
                if (childInLink) cc = Brushes.Blue;

                if (name == "#text")
                {
                    string text = HtmlEntity.DeEntitize(child.InnerText);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        // Keep a single separating space between inline runs, but skip pure indentation.
                        if (p.Inlines.Count > 0 && p.Inlines[^1] is Run last && last.Text != null &&
                            !last.Text.EndsWith(" ") && !last.Text.EndsWith("\n"))
                            p.Inlines.Add(new Run { Text = " " });
                        continue;
                    }
                    p.Inlines.Add(new Run { Text = CollapseWhitespace(text), FontWeight = cw, FontStyle = cs, Foreground = cc, FontSize = sz, NavigateUri = cu, Background = cbg, FontFamily = cfam, TextDecorations = MakeDecorations(cunder, cstrike) });
                }
                else if (name == "img")
                {
                    var (bmp, w, h) = LoadImage(child);
                    if (bmp != null) p.Inlines.Add(new InlineImage { Image = bmp, Width = w, Height = h });
                }
                else
                {
                    ParseInlines(child, p, cw, cs, cc, cu, sz, childInLink, cbg, cfam, cunder, cstrike);
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

            if (s.Contains("font-weight"))
            {
                if (s.Contains("bold") || s.Contains(":600") || s.Contains(": 600") || s.Contains(":700") || s.Contains(": 700") || s.Contains(":800") || s.Contains(":900"))
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

            // Accept px and pt (Jodit/HWP paste often uses pt); convert pt -> px (96/72).
            var fm = System.Text.RegularExpressions.Regex.Match(s, "font-size\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)\\s*(px|pt)?");
            if (fm.Success && double.TryParse(fm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out double val) && val > 0)
                size = fm.Groups[2].Value == "pt" ? val * 96.0 / 72.0 : val;
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
                try { return new SolidColorBrush(Color.Parse(value)); } catch { return null; }
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
            void SyncList(ListKind kind, int level)
            {
                while (listStack.Count > level + 1) CloseOne();
                if (listStack.Count == level + 1 && listStack[^1] != kind) CloseOne();
                while (listStack.Count < level + 1) { sb.Append(kind == ListKind.Ordered ? "<ol>\n" : "<ul>\n"); listStack.Add(kind); }
            }

            foreach (var block in doc.Blocks)
            {
                if (block is Paragraph p)
                {
                    if (p.IsListItem) SyncList(p.ListType, p.ListLevel);
                    else CloseAll();

                    string tag = p.IsListItem ? "li"
                        : p.IsQuote ? "blockquote"
                        : (p.HeadingLevel >= 1 && p.HeadingLevel <= 6 ? $"h{p.HeadingLevel}" : "p");
                    string align = p.TextAlignment switch { TextAlignment.Center => "center", TextAlignment.Right => "right", _ => "left" };
                    string pStyle = $"text-align:{align};";
                    if (p.Background is ISolidColorBrush pbg) pStyle += $"background-color:{CssColor(pbg.Color)};";
                    if (p.Indent > 0) pStyle += $"margin-left:{p.Indent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}px;";
                    sb.Append($"<{tag} style='{pStyle}'>");
                    foreach (var inline in p.Inlines) EmitInline(sb, inline);
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
                    sb.Append("<table border='1' style='border-collapse:collapse; width:100%;'>\n");
                    for (int r = 0; r < tb.Rows; r++)
                    {
                        sb.Append("<tr>\n");
                        for (int c = 0; c < tb.Columns; c++)
                        {
                            var cell = tb.Cells[r][c];
                            if (cell.Background is ISolidColorBrush cbg)
                                sb.Append($"<td style='background-color:{CssColor(cbg.Color)}'>");
                            else
                                sb.Append("<td>");
                            foreach (var inline in cell.Inlines) EmitInline(sb, inline);
                            sb.Append("</td>\n");
                        }
                        sb.Append("</tr>\n");
                    }
                    sb.Append("</table>\n");
                }
                else if (block is ImageBlock ib && ib.Image != null)
                {
                    CloseAll();
                    sb.Append($"<p>{ImgTag(ib.Image, ib.Width, ib.Height)}</p>\n");
                }
            }
            CloseAll();
            return sb.ToString();
        }

        // Emits a single inline (Run with all its styling, or an inline image) as HTML.
        private static void EmitInline(StringBuilder sb, Inline inline)
        {
            if (inline is InlineImage im && im.Image != null)
            {
                sb.Append(ImgTag(im.Image, im.Width, im.Height));
                return;
            }
            if (inline is not Run r || r.Text == null) return;

            string t = HtmlEntity.Entitize(r.Text);

            var styles = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(r.FontFamily)) styles.Add($"font-family:{r.FontFamily}");
            if (r.FontSize > 0 && System.Math.Abs(r.FontSize - 14) > 0.01)
                styles.Add($"font-size:{r.FontSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}px");
            if (r.Foreground is ISolidColorBrush fg) styles.Add($"color:{CssColor(fg.Color)}");
            if (r.Background is ISolidColorBrush bg) styles.Add($"background-color:{CssColor(bg.Color)}");
            bool u = HasDecoration(r.TextDecorations, TextDecorationLocation.Underline);
            bool s = HasDecoration(r.TextDecorations, TextDecorationLocation.Strikethrough);
            string? deco = (u, s) switch
            {
                (true, true) => "underline line-through",
                (true, false) => "underline",
                (false, true) => "line-through",
                _ => null
            };
            if (deco != null) styles.Add($"text-decoration:{deco}");

            if (styles.Count > 0) t = $"<span style='{string.Join(";", styles)}'>{t}</span>";
            if (r.FontWeight == FontWeight.Bold) t = $"<b>{t}</b>";
            if (r.FontStyle == FontStyle.Italic) t = $"<i>{t}</i>";
            if (!string.IsNullOrEmpty(r.NavigateUri)) t = $"<a href='{r.NavigateUri}'>{t}</a>";
            sb.Append(t);
        }

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

        private static string ImgTag(Avalonia.Media.Imaging.Bitmap bmp, double w, double h)
        {
            string b64;
            try
            {
                using var ms = new System.IO.MemoryStream();
                bmp.Save(ms);
                b64 = System.Convert.ToBase64String(ms.ToArray());
            }
            catch { return ""; }
            string size = "";
            if (!double.IsNaN(w) && w > 0) size += $" width='{(int)w}'";
            if (!double.IsNaN(h) && h > 0) size += $" height='{(int)h}'";
            return $"<img src='data:image/png;base64,{b64}'{size}/>";
        }
    }
}
