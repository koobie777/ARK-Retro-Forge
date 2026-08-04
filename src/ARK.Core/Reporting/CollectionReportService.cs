using ARK.Core.Naming;
using ARK.Core.Policy;
using ARK.Core.Scanning;
using ARK.Core.Verification;

namespace ARK.Core.Reporting;

/// <summary>
/// Joins the target set against what is on disk.
/// </summary>
/// <remarks>
/// <para>
/// No new subsystem: the catalog says what exists, the scan says what you have, verification says
/// whether it is correct, and the policy says what you want. This only joins them. Anything a
/// report needs that does not already exist is a sign the join is being done wrong.
/// </para>
/// <para>
/// The join is <b>indexed</b>, keyed on the token-set match key both sides already produce.
/// Comparing 1.5M catalog entries against 10,000 units pairwise is not a slow implementation of
/// this — it is a different, unusable program.
/// </para>
/// </remarks>
public sealed class CollectionReportService
{
    private readonly TokenVocabulary _vocabulary;

    /// <summary>Creates the service.</summary>
    public CollectionReportService(TokenVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        _vocabulary = vocabulary;
    }

    /// <summary>Builds the report.</summary>
    /// <param name="scan">What is on disk.</param>
    /// <param name="verification">Whether it is correct.</param>
    /// <param name="target">What the policy says should be there.</param>
    /// <param name="cache">Catalog tokenization cache, for reporting its own effectiveness.</param>
    public CollectionReport Build(
        ScanReport scan,
        VerificationReport verification,
        TargetSet target,
        CatalogNameCache cache)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(cache);

        var states = verification.Units.ToDictionary(unit => unit.Path, StringComparer.OrdinalIgnoreCase);

        // Index the target set once by match key. Every on-disk unit is then a single lookup.
        var wanted = new Dictionary<string, TargetEntry>(StringComparer.Ordinal);
        foreach (var entry in target.Entries.Where(entry => entry.MatchKey.Length > 0))
        {
            wanted.TryAdd(entry.MatchKey, entry);
        }

        // Superseded entries are indexed too: holding one is not "missing", it is "upgradable".
        var below = new Dictionary<string, TargetEntry>(StringComparer.Ordinal);
        foreach (var entry in target.Superseded.Where(entry => entry.MatchKey.Length > 0))
        {
            below.TryAdd(entry.MatchKey, entry);
        }

        var rows = new List<CollectionRow>();
        var upgrades = new List<UpgradeRow>();
        var held = new HashSet<string>(StringComparer.Ordinal);

        foreach (var scanned in scan.Units)
        {
            var unit = scanned.Unit;
            var name = unit.Name;
            var key = name.IsTokenizable ? NameMatchKey.For(name, _vocabulary) : string.Empty;
            states.TryGetValue(unit.PrimaryPath, out var verified);
            var state = verified?.State ?? VerificationState.Unrecognized;

            if (key.Length > 0 && wanted.TryGetValue(key, out var target1))
            {
                held.Add(key);

                // Damaged only means anything because we know this title is one the user wants.
                rows.Add(state == VerificationState.Mismatched
                    ? Row(CollectionState.Damaged, target1, unit.PrimaryPath,
                        verified?.Detail ?? "fails verification")
                    : Row(CollectionState.Present, target1, unit.PrimaryPath));
                continue;
            }

            if (key.Length > 0 && below.TryGetValue(key, out var lesser))
            {
                held.Add(key);

                var better = target.BestByGroup.TryGetValue(
                    VariantGrouping.KeyFor(name, _vocabulary, target.Policy), out var best) ? best : null;

                var heldRow = Row(CollectionState.Present, lesser, unit.PrimaryPath);
                rows.Add(heldRow);

                if (better is not null)
                {
                    // The sleeper finding: this file is fine, and something better exists.
                    upgrades.Add(new UpgradeRow(heldRow, Row(CollectionState.Present, better, null), unit.PrimaryPath));
                }

                continue;
            }

            // On disk, in no DAT the policy drew from. Investigate rather than acquire.
            rows.Add(new CollectionRow(
                CollectionState.Unrecognized,
                name.IsTokenizable ? name.Title : unit.Files[0].Name,
                scanned.Match?.System,
                name.IsTokenizable ? name.Regions : [],
                name.IsTokenizable ? name.Revision.ToString() : string.Empty,
                scanned.Match?.DatName,
                unit.PrimaryPath,
                state == VerificationState.Mismatched
                    ? "on disk and failing verification, but outside the target set"
                    : "on disk, in no DAT the policy drew from"));
        }

        // Whatever the target set holds and the disk does not.
        foreach (var entry in target.Entries.Where(entry => entry.MatchKey.Length > 0 && !held.Contains(entry.MatchKey)))
        {
            rows.Add(Row(CollectionState.Missing, entry, null, "in the target set, not on disk"));
        }

        return new CollectionReport(
            scan.Root,
            target.Policy.Name,
            target.DatNames,
            target.Count,
            rows,
            upgrades,
            target.Refused.Count,
            cache.Tokenized,
            cache.Hits);
    }

    private static CollectionRow Row(CollectionState state, TargetEntry entry, string? path, string? detail = null) =>
        new(state, entry.Title, entry.System, entry.Regions, entry.Revision, entry.Entry.DatName, path, detail);
}
