using AvaloniaRichEditor.Controls;

namespace AvaloniaRichEditor.Documents;

/// <summary>Per-document page setup: paper size, orientation, page boundaries, header/footer, and page
/// numbers. Persisted in the JSON/.flow format and applied to the editor on load (like a word processor's
/// page setup). View-only state such as zoom is deliberately NOT part of this — it isn't a document property.
/// <para>The page-size/orientation enums live in the Controls namespace but are plain value enums (no
/// rendering dependency), so the model/formatters can use them freely.</para></summary>
public class PageSetup
{
    /// <summary>Paper size. <see cref="RichEditorPageSize.Continuous"/> (the default, matching the control)
    /// reflows to width.</summary>
    public RichEditorPageSize PageSize { get; set; } = RichEditorPageSize.Continuous;
    /// <summary>Page orientation (ignored for Continuous).</summary>
    public RichEditorPageOrientation Orientation { get; set; } = RichEditorPageOrientation.Portrait;
    /// <summary>Whether page boundaries are drawn for a concrete paper size.</summary>
    public bool ShowPageBoundaries { get; set; } = true;
    /// <summary>Header text drawn in each page's top margin (null/empty = none).</summary>
    public string? Header { get; set; }
    /// <summary>Footer text drawn in each page's bottom margin (null/empty = none).</summary>
    public string? Footer { get; set; }
    /// <summary>Whether "page / total" is drawn in the bottom margin.</summary>
    public bool ShowPageNumbers { get; set; }

    /// <summary>Returns a shallow value copy of this page setup.</summary>
    /// <summary>The page margin, in DIPs, that the editor draws and that the header/footer band lives in.</summary>
    // Here rather than on the control because the RTF writer needs it too, and a formatter reaching for a
    // control's statics is how a headless formatter stops being headless.
    internal const double MarginX = 48;
    internal const double MarginY = 40;

    /// <summary>Paper size in DIPs for a page size + orientation. Single source: the control's layout and
    /// the RTF writer's tab stops must agree, and two copies of a table like this drift.</summary>
    internal static (double W, double H) PaperDips(Controls.RichEditorPageSize size, Controls.RichEditorPageOrientation orientation)
    {
        var (w, h) = size switch
        {
            Controls.RichEditorPageSize.A3 => (1123.0, 1587.0),      // 297 x 420 mm
            Controls.RichEditorPageSize.A5 => (559.0, 794.0),        // 148 x 210 mm
            Controls.RichEditorPageSize.B4 => (971.0, 1376.0),       // JIS 257 x 364 mm
            Controls.RichEditorPageSize.B5 => (688.0, 971.0),        // JIS 182 x 257 mm
            Controls.RichEditorPageSize.Letter => (816.0, 1056.0),   // 8.5 x 11 in
            Controls.RichEditorPageSize.Legal => (816.0, 1344.0),    // 8.5 x 14 in
            Controls.RichEditorPageSize.Tabloid => (1056.0, 1632.0), // 11 x 17 in
            _ => (794.0, 1123.0),                                    // A4, and Continuous's print fallback
        };
        return orientation == Controls.RichEditorPageOrientation.Landscape ? (h, w) : (w, h);
    }

    public PageSetup Clone() => new()
    {
        PageSize = PageSize,
        Orientation = Orientation,
        ShowPageBoundaries = ShowPageBoundaries,
        Header = Header,
        Footer = Footer,
        ShowPageNumbers = ShowPageNumbers,
    };

    /// <summary>True when the setup carries no real information (Continuous paper, no header/footer/page
    /// numbers) — such a setup is omitted from serialization so plain documents keep their original format.
    /// Orientation/boundaries are irrelevant while Continuous, so they don't count here.</summary>
    public bool IsDefault =>
        PageSize == RichEditorPageSize.Continuous
        && string.IsNullOrEmpty(Header)
        && string.IsNullOrEmpty(Footer)
        && !ShowPageNumbers;
}
