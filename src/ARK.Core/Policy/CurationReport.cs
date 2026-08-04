using ARK.Core.Naming;
using ARK.Core.Units;
using ARK.Core.Verification;

namespace ARK.Core.Policy;

/// <summary>Why a unit took no part in curation.</summary>
public enum CurationExclusion
{
    /// <summary>Not excluded.</summary>
    None = 0,

    /// <summary>
    /// Non-game content. A Bonus Disc is not a revision of the game it shipped beside.
    /// </summary>
    NonGameContent,

    /// <summary>Its name could not be tokenized, so it cannot be placed on any axis.</summary>
    Unparseable,

    /// <summary>Still being written. Not judged.</summary>
    InProgress,

    /// <summary>Claims to be a release its bytes disagree with. Reported, never curated.</summary>
    Mismatched,

    /// <summary>Not ROM content.</summary>
    NotRomContent,
}

/// <summary>One release considered for curation.</summary>
/// <param name="Unit">The game unit. Operations act on this, never on files inside it.</param>
/// <param name="Name">Its tokenized name.</param>
/// <param name="State">Verification state.</param>
public sealed record CurationCandidate(GameUnit Unit, ParsedName Name, VerificationState State)
{
    /// <summary>Primary path of the unit.</summary>
    public string Path => Unit.PrimaryPath;

    /// <summary>Display name.</summary>
    public string FileName => Unit.Files[0].Name;
}

/// <summary>
/// A set of releases of the same game, and what the policy decided about them.
/// </summary>
/// <param name="Key">The grouping key that put them together.</param>
/// <param name="Title">Human-readable title for the report.</param>
/// <param name="Members">Every release in the group.</param>
/// <param name="Keep">Releases the policy keeps.</param>
/// <param name="Remove">Releases the policy would remove. Always empty when <see cref="IsRefused"/>.</param>
/// <param name="Refusals">Axes that could not be ordered, with reasons.</param>
/// <param name="Reason">Summary of the decision.</param>
public sealed record VariantGroup(
    string Key,
    string Title,
    IReadOnlyList<CurationCandidate> Members,
    IReadOnlyList<CurationCandidate> Keep,
    IReadOnlyList<CurationCandidate> Remove,
    IReadOnlyList<AxisOrdering.Refusal> Refusals,
    string Reason)
{
    /// <summary>
    /// True when an axis could not be ordered. The group is reported and skipped whole — a partial
    /// decision on an unorderable group is still a guess.
    /// </summary>
    public bool IsRefused => Refusals.Count > 0;

    /// <summary>Bytes reclaimed by acting on this group.</summary>
    public long ReclaimableBytes => Remove.Sum(member => member.Unit.TotalSize);
}

/// <summary>
/// The result of applying a policy. Read-only: producing this moves nothing.
/// </summary>
/// <param name="Root">The analyzed root.</param>
/// <param name="Policy">The policy applied.</param>
/// <param name="Groups">Variant groups found.</param>
/// <param name="Excluded">Units that took no part, each with a reason.</param>
/// <param name="DiscSets">Multi-disc sets among the scanned units.</param>
public sealed record CurationReport(
    string Root,
    VariantPolicy Policy,
    IReadOnlyList<VariantGroup> Groups,
    IReadOnlyList<(CurationCandidate Candidate, CurationExclusion Reason, string Detail)> Excluded,
    IReadOnlyList<DiscSet>? DiscSets = null)
{
    /// <summary>
    /// Multi-disc sets among the scanned units, so a removal breaking one can be refused.
    /// </summary>
    /// <remarks>
    /// Carried on the report rather than recomputed by each caller: the CLI has no business
    /// grouping units, and a second implementation is a second chance to group them differently.
    /// </remarks>
    public IReadOnlyList<DiscSet> Sets => DiscSets ?? Array.Empty<DiscSet>();

    /// <summary>Groups the policy resolved.</summary>
    public IReadOnlyList<VariantGroup> Resolved => Groups.Where(group => !group.IsRefused).ToArray();

    /// <summary>Groups reported and skipped because an axis could not be ordered.</summary>
    public IReadOnlyList<VariantGroup> Refused => Groups.Where(group => group.IsRefused).ToArray();

    /// <summary>Groups holding more than one release.</summary>
    public IReadOnlyList<VariantGroup> Multi => Groups.Where(group => group.Members.Count > 1).ToArray();

    /// <summary>Releases the policy would remove.</summary>
    public IReadOnlyList<CurationCandidate> Removable => Resolved.SelectMany(group => group.Remove).ToArray();

    /// <summary>Bytes reclaimable across every resolved group.</summary>
    public long ReclaimableBytes => Resolved.Sum(group => group.ReclaimableBytes);

    /// <summary>Units set aside for a given reason.</summary>
    public IReadOnlyList<CurationCandidate> ExcludedFor(CurationExclusion reason) =>
        Excluded.Where(entry => entry.Reason == reason).Select(entry => entry.Candidate).ToArray();

    /// <summary>
    /// Axes treated as identity, so releases differing on them were never compared. The report has
    /// to state this: the same collection curates differently depending on it.
    /// </summary>
    public IReadOnlyList<VariantAxis> IdentityAxes => Policy.IdentityAxes;

    /// <summary>Axes treated as variance, so releases differing on them were ranked.</summary>
    public IReadOnlyList<VariantAxis> VarianceAxes => Policy.VarianceAxes;

    /// <summary>Axes left entirely alone.</summary>
    public IReadOnlyList<VariantAxis> IgnoredAxes => Policy.IgnoredAxes;
}
