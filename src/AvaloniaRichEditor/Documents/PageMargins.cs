namespace AvaloniaRichEditor.Documents;

/// <summary>The four page margins, in MILLIMETRES — the band between the paper's edge and the text, which
/// the header, the footer and the page number are drawn in.
/// <para>Millimetres rather than device pixels because that is the unit a page is discussed in: Word and
/// HWP show margins in mm, paper sizes are defined in mm, and RTF carries them as twips of physical
/// length. A deliberate type rather than Avalonia's <see cref="Avalonia.Thickness"/>, which means device
/// pixels everywhere else in a UI framework — the same four numbers in a different unit is exactly the
/// mix-up worth making impossible.</para></summary>
/// <param name="Left">Left margin in millimetres.</param>
/// <param name="Top">Top margin in millimetres.</param>
/// <param name="Right">Right margin in millimetres.</param>
/// <param name="Bottom">Bottom margin in millimetres.</param>
public readonly record struct PageMargins(double Left, double Top, double Right, double Bottom)
{
    /// <summary>The same margin on all four sides.</summary>
    public PageMargins(double allSides) : this(allSides, allSides, allSides, allSides) { }

    /// <summary>The same margin left and right, and another top and bottom.</summary>
    public static PageMargins Symmetric(double sides, double topAndBottom)
        => new(sides, topAndBottom, sides, topAndBottom);

    internal double LeftDips => Left * PageSetup.DipsPerMm;
    internal double TopDips => Top * PageSetup.DipsPerMm;
    internal double RightDips => Right * PageSetup.DipsPerMm;
    internal double BottomDips => Bottom * PageSetup.DipsPerMm;

    internal static PageMargins FromDips(double left, double top, double right, double bottom)
        => new(left / PageSetup.DipsPerMm, top / PageSetup.DipsPerMm,
               right / PageSetup.DipsPerMm, bottom / PageSetup.DipsPerMm);

    /// <summary>"12.7 × 10.6 mm" — the sides first, as page setup dialogs read them.</summary>
    public override string ToString() => $"{Left:0.#} {Top:0.#} {Right:0.#} {Bottom:0.#} mm";
}
