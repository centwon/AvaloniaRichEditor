using System.Collections.Generic;
using Avalonia.Media;

namespace AvaloniaRichEditor.Documents;

/// <summary>List style for a <see cref="Paragraph"/>.</summary>
public enum ListKind
{
    /// <summary>Not a list item.</summary>
    None,
    /// <summary>Unordered bullet list.</summary>
    Bullet,
    /// <summary>Ordered (numbered) list.</summary>
    Ordered
}

/// <summary>The specific bullet glyph or number format of a list item, refining <see cref="ListKind"/>.
/// <see cref="Default"/> means the kind's default (• for bullets, "1." for numbers).</summary>
public enum ListMarkerStyle
{
    /// <summary>The list kind's default marker (• / "1.").</summary>
    Default,
    /// <summary>Filled disc bullet (•).</summary>
    Disc,
    /// <summary>Hollow circle bullet (◦).</summary>
    Circle,
    /// <summary>Filled square bullet (▪).</summary>
    Square,
    /// <summary>Dash bullet (–).</summary>
    Dash,
    /// <summary>Decimal with a dot (1.).</summary>
    Decimal,
    /// <summary>Decimal with a parenthesis (1)).</summary>
    DecimalParen,
    /// <summary>Lowercase letters (a)).</summary>
    LowerAlpha,
    /// <summary>Uppercase letters (A)).</summary>
    UpperAlpha,
    /// <summary>Lowercase Roman numerals (i)).</summary>
    LowerRoman
}

/// <summary>A block of inline content (<see cref="Run"/>s and <see cref="InlineImage"/>s) with
/// paragraph-level formatting: alignment, line spacing, indent, list style, heading level, background.
/// Also serves as the content of a table cell.</summary>
public class Paragraph : Block
{
    /// <summary>The inline elements (runs and inline images) that make up the paragraph.</summary>
    public List<Inline> Inlines { get; set; } = new();
    /// <summary>Horizontal text alignment. Default: Left.</summary>
    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;
    /// <summary>Absolute line-box height in device-independent pixels ("exactly" spacing, like Word's
    /// fixed value). <see cref="double.NaN"/> = unset. Overridden by <see cref="LineSpacing"/> when that
    /// is set. For proportional spacing that scales with font size, prefer <see cref="LineSpacing"/>.</summary>
    public double LineHeight { get; set; } = double.NaN;
    /// <summary>Proportional line spacing as a multiple of the natural single-line height (1.0 = single,
    /// 1.5 = 1.5 lines, 2.0 = double — i.e. HWP's % ÷ 100 or Word's "Multiple"). Scales with font size.
    /// <see cref="double.NaN"/> = unset (falls back to <see cref="LineHeight"/>, then the font's natural
    /// height). Takes priority over <see cref="LineHeight"/> when set.</summary>
    public double LineSpacing { get; set; } = double.NaN;
    /// <summary>Right margin in device-independent pixels — narrows the wrap width. Paragraph-only:
    /// nothing flows around images/tables, so a right margin would be invisible there
    /// (the left margin is <see cref="Block.Indent"/>). Default: 0.</summary>
    public double MarginRight { get; set; } = 0;
    /// <summary>List style for this paragraph. Default: None.</summary>
    public ListKind ListType { get; set; } = ListKind.None;
    /// <summary>The bullet glyph / number format for this list item (refines <see cref="ListType"/>).
    /// Default = the kind's default (• / "1."). Ignored when <see cref="ListType"/> is None.</summary>
    public ListMarkerStyle ListMarker { get; set; } = ListMarkerStyle.Default;
    /// <summary>Heading level: 0 = body text, 1–6 = h1–h6.</summary>
    public int HeadingLevel { get; set; } = 0;
    /// <summary>Paragraph or table-cell background fill brush.</summary>
    public IBrush? Background { get; set; }
    /// <summary>Whether this paragraph is a blockquote.</summary>
    public bool IsQuote { get; set; } = false;
    /// <summary>Nested list depth (0 = top level).</summary>
    public int ListLevel { get; set; } = 0;

    /// <summary><see langword="true"/> if this paragraph is a bullet or numbered list item.</summary>
    public bool IsListItem => ListType != ListKind.None;

    /// <summary>Copies every paragraph-level formatting field from <paramref name="source"/> (the
    /// inlines are not touched). Single source for the edit paths that derive a new paragraph from an
    /// existing one — Enter's split, the paste tail, list splitting — which each used to copy a
    /// hand-picked subset and silently dropped the rest (line spacing, quote bar, marker style…).
    /// <see cref="Clone"/> uses this list too — it used to mirror it by hand, which is a rule only a
    /// person can keep. A caller that must diverge (Enter resets the heading level to body text)
    /// overrides the field afterwards.</summary>
    public void CopyFormatFrom(Paragraph source)
    {
        MarginTop = source.MarginTop;
        MarginBottom = source.MarginBottom;
        MarginRight = source.MarginRight;
        TextAlignment = source.TextAlignment;
        LineHeight = source.LineHeight;
        LineSpacing = source.LineSpacing;
        ListType = source.ListType;
        ListMarker = source.ListMarker;
        HeadingLevel = source.HeadingLevel;
        Background = source.Background;
        Indent = source.Indent;
        IsQuote = source.IsQuote;
        ListLevel = source.ListLevel;
    }

    /// <inheritdoc/>
    /// <remarks>The format fields come from <see cref="CopyFormatFrom"/> — the ONE list — because this
    /// used to be a second hand-written copy of it. A paragraph property added without updating both
    /// lists survives normal editing and then disappears at the first undo, since an undo state is a
    /// clone; that is the same failure CopyFormatFrom's own note records for the split paths.</remarks>
    public override TextElement Clone()
    {
        var p = new Paragraph();
        p.CopyFormatFrom(this);
        foreach (var inline in Inlines)
        {
            var inlineClone = inline.Clone() as Inline;
            if (inlineClone != null)
            {
                inlineClone.Parent = p;
                p.Inlines.Add(inlineClone);
            }
        }
        return p;
    }
}
