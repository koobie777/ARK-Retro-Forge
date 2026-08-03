using ARK.Core.Naming;

namespace ARK.Core.Policy;

/// <summary>
/// An independent dimension along which releases of one game differ.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no single "best" copy.</b> A user's preferences are independent per axis, and
/// collapsing them into one ranking produces silent, wrong deletions. Someone who wants no
/// prototypes has said nothing about homebrew — development status covers 245 files on the
/// reference corpus and licensing covers 390, and they are different files.
/// </para>
/// <para>
/// Hardware flags are deliberately absent. <c>(SGB Enhanced)</c>, <c>(GB Compatible)</c> and
/// <c>(NDSi Enhanced)</c> describe cartridge capability, not release lineage, and must not
/// participate in grouping or ranking at all.
/// </para>
/// </remarks>
public enum VariantAxis
{
    /// <summary>Rev 0 (untagged) · Rev 1..N · Rev A..Z.</summary>
    Revision = 0,

    /// <summary><c>v1.0</c>, <c>v1.1</c>. A separate scheme from revision, never merged with it.</summary>
    Version,

    /// <summary>Retail (untagged) · Alpha · Beta · Proto · Sample · Demo · Kiosk · Preview · Debug.</summary>
    DevStatus,

    /// <summary>USA · Europe · Japan · World · lists.</summary>
    Region,

    /// <summary>Licensed (untagged) · Unl · Aftermarket · Pirate. Independent of dev status.</summary>
    Licensing,

    /// <summary>Original (untagged) · Virtual Console · e-Reader · Switch Online · Arcade.</summary>
    Distribution,

    /// <summary>Language token sets. Rarely a curation axis; exposed, ignored by default.</summary>
    Language,
}

/// <summary>How an axis participates in grouping and selection.</summary>
public enum AxisRole
{
    /// <summary>
    /// Part of the grouping key: releases differing on this axis are <b>different games</b> and
    /// are never compared against each other.
    /// </summary>
    Identity = 0,

    /// <summary>
    /// Releases differing on this axis are variants of one game and are ranked against each other.
    /// </summary>
    Variance,

    /// <summary>Not used for grouping and not used for selection. Left entirely alone.</summary>
    Ignored,
}

/// <summary>What to keep along an axis marked <see cref="AxisRole.Variance"/>.</summary>
public enum AxisSelection
{
    /// <summary>Keep every value. Nothing is removed on this axis.</summary>
    KeepAll = 0,

    /// <summary>
    /// Keep the highest-ranked value. Only meaningful where the axis has a real order —
    /// revision, version, and dev status do; region and licensing do not.
    /// </summary>
    KeepBest,

    /// <summary>Keep only the values named in the rule, dropping the rest.</summary>
    KeepMatching,
}

/// <summary>
/// One axis's rule. A policy is nothing but a set of these — every shipped preset is expressible
/// as per-axis rules, so no preset is special-cased in code.
/// </summary>
/// <param name="Axis">The axis.</param>
/// <param name="Role">Whether it identifies a game, varies within one, or is ignored.</param>
/// <param name="Selection">What to keep, when the role is <see cref="AxisRole.Variance"/>.</param>
/// <param name="Values">
/// Values to keep under <see cref="AxisSelection.KeepMatching"/>. The untagged case is named by
/// <see cref="AxisRule.Untagged"/> — "retail" and "licensed" are both the absence of a token.
/// </param>
public sealed record AxisRule(
    VariantAxis Axis,
    AxisRole Role,
    AxisSelection Selection = AxisSelection.KeepAll,
    IReadOnlyList<string>? Values = null)
{
    /// <summary>
    /// Stands for the absence of a token on an axis: retail, licensed, original distribution.
    /// Real data expresses these by carrying no tag at all, so they need a name to be selectable.
    /// </summary>
    public const string Untagged = "(untagged)";

    /// <summary>Values this rule keeps, empty when it keeps everything.</summary>
    public IReadOnlyList<string> Keep => Values ?? [];

    /// <summary>True when this rule can remove anything.</summary>
    public bool Removes => Role == AxisRole.Variance && Selection != AxisSelection.KeepAll;
}

/// <summary>Maps token categories onto curation axes.</summary>
public static class VariantAxes
{
    /// <summary>The axis a token category belongs to, or null when it is not a curation axis.</summary>
    public static VariantAxis? For(TokenCategory category) => category switch
    {
        TokenCategory.Revision => VariantAxis.Revision,
        TokenCategory.Version => VariantAxis.Version,
        TokenCategory.DevStatus => VariantAxis.DevStatus,
        TokenCategory.Region => VariantAxis.Region,
        TokenCategory.Licensing => VariantAxis.Licensing,
        TokenCategory.Distribution => VariantAxis.Distribution,
        TokenCategory.Language => VariantAxis.Language,
        _ => null,
    };

    /// <summary>
    /// Categories that never take part in grouping or ranking.
    /// </summary>
    /// <remarks>
    /// Hardware describes what the cartridge can do, not which release it is. Date is evidence
    /// used to order pre-release builds, not a thing to curate on — two protos differing only by
    /// date are the same game, and the date is what tells them apart in time.
    /// </remarks>
    public static bool IsNeverCurated(TokenCategory category) =>
        category is TokenCategory.Hardware or TokenCategory.Date;
}
