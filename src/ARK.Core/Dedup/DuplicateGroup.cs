using ARK.Core.Units;
using ARK.Core.Verification;

namespace ARK.Core.Dedup;

/// <summary>Which copy of a duplicate set to keep.</summary>
public enum KeepPolicy
{
    /// <summary>
    /// List the groups and decide nothing. <b>Ships as the default</b>, and a first run with no
    /// flags moves nothing.
    /// </summary>
    ReportOnly = 0,

    /// <summary>Prefer a unit in a ROM-set directory with a resolved DAT over a loose copy.</summary>
    KeepIdentified,

    /// <summary>Prefer the least-nested location.</summary>
    KeepShortestPath,

    /// <summary>Prefer the earliest modification time.</summary>
    KeepOldest,

    /// <summary>Prefer the latest modification time.</summary>
    KeepNewest,
}

/// <summary>Why a unit took no part in deduplication.</summary>
public enum DedupExclusion
{
    /// <summary>Not excluded.</summary>
    None = 0,

    /// <summary>
    /// Still being written. Its hash describes bytes that are about to change, so it cannot be
    /// compared against anything.
    /// </summary>
    InProgress,

    /// <summary>
    /// Claims to be a release its bytes disagree with. Reported, never collapsed: two identical
    /// corrupt files are two corrupt files, and collapsing them leaves one corrupt file and hides
    /// the second.
    /// </summary>
    Mismatched,

    /// <summary>Not ROM content.</summary>
    NotRomContent,

    /// <summary>Could not be hashed, so it cannot be compared.</summary>
    Unhashable,

    /// <summary>Its ROM size is unique, so it cannot have a byte-identical twin.</summary>
    UniqueSize,
}

/// <summary>One unit considered for deduplication, with what is known about it.</summary>
/// <param name="Unit">The game unit. Operations act on this, never on files inside it.</param>
/// <param name="RomSize">Size of the ROM inside, not of the archive.</param>
/// <param name="Crc32">CRC32 of the ROM, once computed.</param>
/// <param name="Sha1">SHA1, computed only to settle a CRC32 collision.</param>
/// <param name="State">Verification state, which decides whether it may participate.</param>
/// <param name="DatName">DAT the unit's directory was compared against, when there was one.</param>
public sealed record DedupCandidate(
    GameUnit Unit,
    long RomSize,
    string? Crc32 = null,
    string? Sha1 = null,
    VerificationState State = VerificationState.Unrecognized,
    string? DatName = null)
{
    /// <summary>Primary path of the unit.</summary>
    public string Path => Unit.PrimaryPath;

    /// <summary>Display name.</summary>
    public string Name => Unit.Files[0].Name;

    /// <summary>Last write time of the unit's primary file.</summary>
    public DateTimeOffset ModifiedUtc => Unit.Files[0].ModifiedUtc;
}

/// <summary>
/// A set of units whose ROM content is byte-identical.
/// </summary>
/// <remarks>
/// Confirmed by full hash equality alone — never by size, never by name. Two files sharing a name
/// are not duplicates, and two revisions of a title are not duplicates: they have different bytes
/// and removing one is a curation preference, not a redundancy.
/// </remarks>
/// <param name="RomSize">Shared ROM size.</param>
/// <param name="Crc32">Shared CRC32.</param>
/// <param name="Sha1">Shared SHA1, when a collision forced it.</param>
/// <param name="Members">Every unit holding this content.</param>
/// <param name="Keep">The unit the policy chose to keep, or null when it could not decide.</param>
/// <param name="Reason">Why that one, or why no decision was possible.</param>
public sealed record DuplicateGroup(
    long RomSize,
    string Crc32,
    string? Sha1,
    IReadOnlyList<DedupCandidate> Members,
    DedupCandidate? Keep = null,
    string Reason = "")
{
    /// <summary>Units that would be quarantined. Empty when the policy could not decide.</summary>
    public IReadOnlyList<DedupCandidate> Removable => Keep is null
        ? []
        : Members.Where(member => !ReferenceEquals(member, Keep)).ToArray();

    /// <summary>Bytes reclaimed by collapsing this group.</summary>
    public long ReclaimableBytes => Removable.Sum(member => member.Unit.TotalSize);

    /// <summary>True when the policy could not choose and the group is reported and skipped.</summary>
    public bool IsTie => Keep is null;
}

/// <summary>A unit that took no part, and why.</summary>
/// <param name="Candidate">The unit.</param>
/// <param name="Reason">Why it was set aside.</param>
/// <param name="Detail">Human-readable explanation.</param>
public sealed record DedupExclusionEntry(DedupCandidate Candidate, DedupExclusion Reason, string Detail);

/// <summary>
/// The result of a deduplication analysis. Read-only: producing this moves nothing.
/// </summary>
/// <param name="Root">The analyzed root.</param>
/// <param name="Groups">Confirmed duplicate groups.</param>
/// <param name="Excluded">Units that took no part, each with a stated reason.</param>
/// <param name="Policy">The policy applied.</param>
/// <param name="Hashed">Units that required hashing this run.</param>
/// <param name="DiscSets">Multi-disc sets among the scanned units.</param>
public sealed record DedupReport(
    string Root,
    IReadOnlyList<DuplicateGroup> Groups,
    IReadOnlyList<DedupExclusionEntry> Excluded,
    KeepPolicy Policy,
    int Hashed,
    IReadOnlyList<DiscSet>? DiscSets = null)
{
    /// <summary>
    /// Multi-disc sets present in the collection, so a removal breaking one can be refused.
    /// </summary>
    /// <remarks>
    /// This is where the hazard bites hardest. A Disc 2 shared byte-for-byte between two releases
    /// is a genuine duplicate by every measure dedup has, and collapsing it guts the set it was
    /// holding up.
    /// </remarks>
    public IReadOnlyList<DiscSet> Sets => DiscSets ?? Array.Empty<DiscSet>();

    /// <summary>Groups the policy resolved.</summary>
    public IReadOnlyList<DuplicateGroup> Resolved => Groups.Where(group => !group.IsTie).ToArray();

    /// <summary>Groups reported and skipped because the policy could not choose.</summary>
    public IReadOnlyList<DuplicateGroup> Ties => Groups.Where(group => group.IsTie).ToArray();

    /// <summary>Units that would be quarantined across every resolved group.</summary>
    public IReadOnlyList<DedupCandidate> Removable => Resolved.SelectMany(group => group.Removable).ToArray();

    /// <summary>Bytes reclaimable across every resolved group.</summary>
    public long ReclaimableBytes => Resolved.Sum(group => group.ReclaimableBytes);

    /// <summary>Units set aside for a given reason.</summary>
    public IReadOnlyList<DedupExclusionEntry> ExcludedFor(DedupExclusion reason) =>
        Excluded.Where(entry => entry.Reason == reason).ToArray();
}
