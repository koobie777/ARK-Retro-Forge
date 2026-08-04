namespace ARK.Core.Reporting;

/// <summary>Where a release stands relative to the target set.</summary>
public enum CollectionState
{
    /// <summary>In the target set, on disk, and verified.</summary>
    Present = 0,

    /// <summary>In the target set, not on disk. Acquire it.</summary>
    Missing,

    /// <summary>
    /// In the target set, on disk, and failing verification. Re-acquire <i>this specific title</i>.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Missing"/>, and the distinction is what makes it actionable. A
    /// mismatched file for a title outside the target set is noise; the same file for a title
    /// inside it is a gap you can close, and you already know exactly which one.
    /// </remarks>
    Damaged,

    /// <summary>
    /// On disk, in no DAT. A bad dump, a hack, homebrew, or a file from somewhere else — worth
    /// investigating rather than acquiring.
    /// </summary>
    Unrecognized,
}

/// <summary>One row of the collection report.</summary>
/// <param name="State">Where it stands.</param>
/// <param name="Title">Canonical title.</param>
/// <param name="System">System code, when the owning DAT resolved to one.</param>
/// <param name="Regions">Regions the release carries.</param>
/// <param name="Revision">Revision text; empty means Rev 0, the original.</param>
/// <param name="DatName">DAT it came from, when it came from one.</param>
/// <param name="Path">Where it is on disk, when it is.</param>
/// <param name="Detail">Why, for anything other than Present.</param>
public sealed record CollectionRow(
    CollectionState State,
    string Title,
    string? System,
    IReadOnlyList<string> Regions,
    string Revision,
    string? DatName,
    string? Path = null,
    string? Detail = null)
{
    /// <summary>
    /// One line carrying enough to search on: this is a work queue, not prose.
    /// </summary>
    public string ToQueueLine() =>
        $"{System ?? "-"}\t{Title}\t{(Regions.Count == 0 ? "-" : string.Join(",", Regions))}\t{(Revision.Length == 0 ? "-" : Revision)}\t{DatName ?? "-"}";
}

/// <summary>A release you hold that the target set ranks below something else.</summary>
/// <param name="Held">What you have.</param>
/// <param name="Better">What the policy would rather you had.</param>
/// <param name="Path">Where the held copy is.</param>
public sealed record UpgradeRow(CollectionRow Held, CollectionRow Better, string Path);

/// <summary>
/// The join of catalog, scan, verification and policy — what the pipeline was built to produce.
/// </summary>
/// <remarks>
/// Read-only throughout: nothing moves, nothing is quarantined, and undo has no role here.
/// </remarks>
/// <param name="Root">The scanned root.</param>
/// <param name="PolicyName">Policy that produced the target set.</param>
/// <param name="DatNames">DATs the target set was derived from.</param>
/// <param name="TargetSetSize">How many releases the policy says you should have.</param>
/// <param name="Rows">Every release, in whichever state it is.</param>
/// <param name="Upgradable">Held releases with a better one available.</param>
/// <param name="RefusedGroups">Groups the policy could not rank, reported rather than guessed.</param>
/// <param name="CatalogNamesTokenized">Catalog names parsed this run.</param>
/// <param name="CatalogNameCacheHits">Catalog name lookups served from cache.</param>
public sealed record CollectionReport(
    string Root,
    string PolicyName,
    IReadOnlyList<string> DatNames,
    int TargetSetSize,
    IReadOnlyList<CollectionRow> Rows,
    IReadOnlyList<UpgradeRow> Upgradable,
    int RefusedGroups,
    int CatalogNamesTokenized,
    int CatalogNameCacheHits)
{
    /// <summary>Rows in one state.</summary>
    public IReadOnlyList<CollectionRow> InState(CollectionState state) =>
        Rows.Where(row => row.State == state).ToArray();

    /// <summary>Count per state, every state present even at zero.</summary>
    public IReadOnlyDictionary<CollectionState, int> Counts =>
        Enum.GetValues<CollectionState>().ToDictionary(state => state, state => Rows.Count(row => row.State == state));

    /// <summary>Share of the target set actually held and verified, 0..1.</summary>
    public double Completeness =>
        TargetSetSize == 0 ? 1 : (double)InState(CollectionState.Present).Count / TargetSetSize;

    /// <summary>Counts per system for a given state.</summary>
    public IReadOnlyList<(string System, int Count)> BySystem(CollectionState state) => InState(state)
        .GroupBy(row => row.System ?? "(unrecognized)", StringComparer.OrdinalIgnoreCase)
        .Select(group => (group.Key, group.Count()))
        .OrderByDescending(entry => entry.Item2)
        .ToArray();

    /// <summary>
    /// Counts per region for a given state. A release carrying several regions counts once per
    /// region, because it genuinely satisfies each of them.
    /// </summary>
    public IReadOnlyList<(string Region, int Count)> ByRegion(CollectionState state) => InState(state)
        .SelectMany(row => row.Regions.Count == 0 ? new[] { "(none)" } : row.Regions.ToArray())
        .GroupBy(region => region, StringComparer.OrdinalIgnoreCase)
        .Select(group => (group.Key, group.Count()))
        .OrderByDescending(entry => entry.Item2)
        .ToArray();

    /// <summary>Counts per system and region together.</summary>
    public IReadOnlyList<(string System, string Region, int Count)> BySystemAndRegion(CollectionState state) => InState(state)
        .SelectMany(row => (row.Regions.Count == 0 ? new[] { "(none)" } : row.Regions.ToArray())
            .Select(region => (System: row.System ?? "(unrecognized)", Region: region)))
        .GroupBy(entry => entry, EqualityComparer<(string System, string Region)>.Default)
        .Select(group => (group.Key.System, group.Key.Region, group.Count()))
        .OrderBy(entry => entry.System, StringComparer.OrdinalIgnoreCase)
        .ThenByDescending(entry => entry.Item3)
        .ToArray();

    /// <summary>The missing list as a re-acquisition queue, one line per title.</summary>
    public IReadOnlyList<string> MissingQueue() =>
        InState(CollectionState.Missing).Select(row => row.ToQueueLine()).ToArray();
}
