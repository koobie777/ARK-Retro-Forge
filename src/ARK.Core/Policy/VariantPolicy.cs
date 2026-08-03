namespace ARK.Core.Policy;

/// <summary>
/// A complete curation policy: one rule per axis, plus a name.
/// </summary>
/// <remarks>
/// Policies are data, saved to instance settings and selectable. A preservationist and a casual
/// player have opposite correct answers, so none of this is hardcoded — the shipped presets are
/// just named sets of per-axis rules, built by the same constructor a custom policy uses.
/// </remarks>
/// <param name="Name">Preset name, or a user's own label.</param>
/// <param name="Rules">One rule per axis. Axes with no rule default to <see cref="AxisRole.Identity"/>.</param>
/// <param name="PreferredRegions">
/// Region preference order, consulted when the region axis keeps a single value. Ordered, because
/// "USA then Europe then World" is a real preference and alphabetical is not.
/// </param>
public sealed record VariantPolicy(
    string Name,
    IReadOnlyList<AxisRule> Rules,
    IReadOnlyList<string>? PreferredRegions = null)
{
    /// <summary>The rule for an axis. Unmentioned axes are identity — they never cause a removal.</summary>
    public AxisRule RuleFor(VariantAxis axis) =>
        Rules.FirstOrDefault(rule => rule.Axis == axis) ?? new AxisRule(axis, AxisRole.Identity);

    /// <summary>Axes treated as identity: differing here means a different game.</summary>
    public IReadOnlyList<VariantAxis> IdentityAxes => Enum.GetValues<VariantAxis>()
        .Where(axis => RuleFor(axis).Role == AxisRole.Identity)
        .ToArray();

    /// <summary>Axes treated as variance: differing here means variants of one game.</summary>
    public IReadOnlyList<VariantAxis> VarianceAxes => Enum.GetValues<VariantAxis>()
        .Where(axis => RuleFor(axis).Role == AxisRole.Variance)
        .ToArray();

    /// <summary>Axes left entirely alone.</summary>
    public IReadOnlyList<VariantAxis> IgnoredAxes => Enum.GetValues<VariantAxis>()
        .Where(axis => RuleFor(axis).Role == AxisRole.Ignored)
        .ToArray();

    /// <summary>True when no rule can remove anything, so the policy is purely a report.</summary>
    public bool IsReportOnly => !Rules.Any(rule => rule.Removes);

    /// <summary>Region preference order, defaulting to none.</summary>
    public IReadOnlyList<string> Regions => PreferredRegions ?? [];
}

/// <summary>
/// The shipped presets, each expressed as per-axis rules.
/// </summary>
/// <remarks>
/// Nothing here is special-cased downstream: the engine sees only <see cref="AxisRule"/>s, so a
/// custom policy behaves exactly like a preset and a preset can be edited into a custom one.
/// </remarks>
public static class VariantPresets
{
    /// <summary>Name of the default preset.</summary>
    public const string ReportOnlyName = "report-only";

    /// <summary>
    /// Ships as the default. Every axis is identity, so every release is its own group and nothing
    /// is ever a removal candidate.
    /// </summary>
    public static VariantPolicy ReportOnly { get; } = new(
        ReportOnlyName,
        Enum.GetValues<VariantAxis>().Select(axis => new AxisRule(axis, AxisRole.Identity)).ToArray());

    /// <summary>
    /// No removal, but every curation axis is treated as variance so groups are formed and ranked.
    /// Used to drive collection reports without curating anything.
    /// </summary>
    public static VariantPolicy Everything { get; } = new(
        "everything",
        Enum.GetValues<VariantAxis>()
            .Select(axis => new AxisRule(axis, AxisRole.Variance, AxisSelection.KeepAll))
            .ToArray());

    /// <summary>Drop pre-release builds. Every other axis is left alone.</summary>
    public static VariantPolicy RetailOnly { get; } = new(
        "retail-only",
        new[]
        {
            new AxisRule(VariantAxis.DevStatus, AxisRole.Variance, AxisSelection.KeepMatching, new[] { AxisRule.Untagged }),
        });

    /// <summary>Collapse revisions to the newest. Every other axis is left alone.</summary>
    public static VariantPolicy LatestRevision { get; } = new(
        "latest-revision",
        new[]
        {
            new AxisRule(VariantAxis.Revision, AxisRole.Variance, AxisSelection.KeepBest),
        });

    /// <summary>
    /// One game, one ROM: a single region, the latest revision, retail only. Licensing and
    /// distribution stay identity — asking for one region has said nothing about homebrew.
    /// </summary>
    public static VariantPolicy OneGameOneRom { get; } = new(
        "1g1r",
        new[]
        {
            new AxisRule(VariantAxis.Region, AxisRole.Variance, AxisSelection.KeepBest),
            new AxisRule(VariantAxis.Revision, AxisRole.Variance, AxisSelection.KeepBest),
            new AxisRule(VariantAxis.Version, AxisRole.Variance, AxisSelection.KeepBest),
            new AxisRule(VariantAxis.DevStatus, AxisRole.Variance, AxisSelection.KeepMatching, new[] { AxisRule.Untagged }),
            new AxisRule(VariantAxis.Language, AxisRole.Ignored),
        },
        new[] { "USA", "World", "Europe", "Japan" });

    /// <summary>Every shipped preset, by name.</summary>
    public static IReadOnlyDictionary<string, VariantPolicy> All { get; } =
        new[] { ReportOnly, Everything, RetailOnly, LatestRevision, OneGameOneRom }
            .ToDictionary(policy => policy.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves a preset by name, or null when unrecognized — never a silent fallback.</summary>
    public static VariantPolicy? Find(string? name) =>
        name is { Length: > 0 } && All.TryGetValue(name, out var policy) ? policy : null;
}
