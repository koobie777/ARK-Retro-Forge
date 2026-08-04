using ARK.Core.Dat;
using ARK.Core.Naming;
using ARK.Core.Policy;

namespace ARK.Core.Reporting;

/// <summary>One release the user should have, according to their policy.</summary>
/// <param name="Entry">The catalog entry.</param>
/// <param name="Name">Its tokenized name.</param>
/// <param name="MatchKey">Token-set key, used to join against what is on disk.</param>
public sealed record TargetEntry(CatalogEntry Entry, ParsedName Name, string MatchKey)
{
    /// <summary>Title for the report.</summary>
    public string Title => Name.Title;

    /// <summary>System the owning DAT resolved to.</summary>
    public string? System => Entry.System;

    /// <summary>Regions this release carries.</summary>
    public IReadOnlyList<string> Regions => Name.Regions;

    /// <summary>Revision, as text. Empty when untagged — which is Rev 0, the original.</summary>
    public string Revision => Name.Revision.ToString();
}

/// <summary>
/// What the user should have: the policy's output over the catalog.
/// </summary>
/// <param name="Policy">The policy that produced it.</param>
/// <param name="DatNames">The DATs it was derived from.</param>
/// <param name="Entries">The releases in the target set.</param>
/// <param name="Superseded">
/// Releases the policy ranked below a kept one. These are what makes "upgradable" answerable: a
/// file matching one of these is fine, but something better exists in the target set.
/// </param>
/// <param name="Refused">
/// Groups the policy could not rank. Reported, never guessed at — the same refusal curation makes.
/// </param>
public sealed record TargetSet(
    VariantPolicy Policy,
    IReadOnlyList<string> DatNames,
    IReadOnlyList<TargetEntry> Entries,
    IReadOnlyList<TargetEntry> Superseded,
    IReadOnlyList<RankedGroup<TargetEntry>> Refused)
{
    /// <summary>Number of releases the user should have.</summary>
    public int Count => Entries.Count;

    /// <summary>The best entry for each grouping key, for upgrade comparison.</summary>
    public IReadOnlyDictionary<string, TargetEntry> BestByGroup { get; init; } =
        new Dictionary<string, TargetEntry>(StringComparer.Ordinal);
}

/// <summary>
/// Derives a target set by running a curation policy over the catalog.
/// </summary>
/// <remarks>
/// <para>
/// <b>The key inversion of this phase.</b> Curation runs the policy over your files to decide what
/// to remove; this runs the identical policy over the catalog to decide what you should have. Same
/// grouping, same ranking, same axes, same refusals — different input.
/// </para>
/// <para>
/// Completeness is meaningless without this. A full No-Intro DAT carries every region, revision,
/// proto, beta, sample and aftermarket release; measured against a USA retail collection it would
/// report tens of thousands missing, nearly all of them Japanese releases and prototypes nobody
/// asked for. True, and useless.
/// </para>
/// </remarks>
public sealed class TargetSetBuilder
{
    private readonly CatalogNameCache _names;
    private readonly TokenVocabulary _vocabulary;

    /// <summary>Creates a builder.</summary>
    public TargetSetBuilder(CatalogNameCache names, TokenVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(vocabulary);
        _names = names;
        _vocabulary = vocabulary;
    }

    /// <summary>Builds the target set from the supplied catalog entries.</summary>
    public TargetSet Build(IEnumerable<CatalogEntry> entries, VariantPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(policy);

        var candidates = new List<TargetEntry>();
        var datNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            datNames.Add(entry.DatName);

            var raw = entry.GameName is { Length: > 0 } gameName ? gameName : entry.RomName;
            var parsed = _names.Parse(raw);

            // A catalog entry ARK cannot tokenize cannot be placed on any axis, and non-game
            // content is not a release the user is missing.
            if (!parsed.IsTokenizable || VariantGrouping.IsNonGameContent(parsed))
            {
                continue;
            }

            candidates.Add(new TargetEntry(entry, parsed, NameMatchKey.For(parsed, _vocabulary)));
        }

        var groups = VariantEngine.Group(candidates, target => target.Name, _vocabulary, policy);

        var kept = new List<TargetEntry>();
        var superseded = new List<TargetEntry>();
        var best = new Dictionary<string, TargetEntry>(StringComparer.Ordinal);

        foreach (var group in groups.Where(group => !group.IsRefused))
        {
            kept.AddRange(group.Keep);
            superseded.AddRange(group.Remove);

            if (group.Keep.Count > 0)
            {
                best[group.Key] = group.Keep[0];
            }
        }

        // A refused group still contributes its members: the policy could not rank them, so none
        // of them is superseded and every one remains something the user may legitimately hold.
        var refused = groups.Where(group => group.IsRefused).ToArray();
        kept.AddRange(refused.SelectMany(group => group.Members));

        return new TargetSet(policy, datNames.ToArray(), kept, superseded, refused) { BestByGroup = best };
    }
}
