using ARK.Core.Hashing;

namespace ARK.Core.Verification;

/// <summary>
/// One unit's verdict.
/// </summary>
/// <param name="Path">The unit's primary path.</param>
/// <param name="Name">Display name.</param>
/// <param name="SetFolder">Directory the unit came from.</param>
/// <param name="DatName">DAT it was compared against, or null when none applied.</param>
/// <param name="State">The verdict.</param>
/// <param name="Detail">Why — always populated for anything other than Verified.</param>
/// <param name="ExpectedCrc32">CRC32 the DAT entry declares, when there was a DAT entry.</param>
/// <param name="ActualCrc32">CRC32 actually computed, when hashing produced one.</param>
/// <param name="Format">Format detected from the ROM's own bytes, when it was read.</param>
/// <param name="FormatQualifier">Qualifier declared by the folder, when it declared one.</param>
/// <param name="FormatContradiction">
/// Set when the detected format disagrees with the folder's declared qualifier. Neither is
/// trusted over the other; the disagreement is the finding.
/// </param>
/// <param name="Signal">Which in-progress signal fired, when one did.</param>
/// <param name="Normalized">Whether bytes were normalized in memory before hashing.</param>
public sealed record VerifiedUnit(
    string Path,
    string Name,
    string SetFolder,
    string? DatName,
    VerificationState State,
    string Detail,
    string? ExpectedCrc32 = null,
    string? ActualCrc32 = null,
    RomFormat Format = RomFormat.Unknown,
    string? FormatQualifier = null,
    string? FormatContradiction = null,
    InProgressSignal Signal = InProgressSignal.None,
    bool Normalized = false)
{
    /// <summary>
    /// Whether this unit may later be renamed to its canonical name.
    /// </summary>
    /// <remarks>
    /// Only <see cref="VerificationState.Verified"/> qualifies, and the flag is enforced now even
    /// though renaming does not exist yet. A corrupt file renamed to its canonical name looks
    /// verified forever after — worse than v1's damage, which at least announced itself in the
    /// filename.
    /// </remarks>
    public bool IsRenameEligible => State == VerificationState.Verified;
}

/// <summary>
/// The result of a verification run: counts per state and the units behind them.
/// </summary>
/// <remarks>
/// Read-only, like scan. Nothing is deleted, moved or quarantined — quarantine arrives with
/// Phase 7, after undo exists.
/// </remarks>
/// <param name="Root">The verified root.</param>
/// <param name="Units">Every unit's verdict.</param>
/// <param name="Hashed">Units that required hashing this run.</param>
/// <param name="CacheHits">Units served from the hash cache.</param>
/// <param name="Elapsed">Wall-clock duration.</param>
/// <param name="BytesHashed">Total ROM bytes read this run.</param>
/// <param name="Cancelled">Whether the run was interrupted before finishing.</param>
/// <param name="DiscSets">
/// Verdicts for multi-disc sets. A set is only as complete as its worst disc, so this says
/// something the per-unit states do not: that a game is unplayable, not merely that one file
/// failed.
/// </param>
public sealed record VerificationReport(
    string Root,
    IReadOnlyList<VerifiedUnit> Units,
    int Hashed,
    int CacheHits,
    TimeSpan Elapsed,
    long BytesHashed,
    bool Cancelled = false,
    IReadOnlyList<DiscSetVerdict>? DiscSets = null)
{
    /// <summary>Multi-disc sets and how each stands.</summary>
    public IReadOnlyList<DiscSetVerdict> Sets => DiscSets ?? Array.Empty<DiscSetVerdict>();

    /// <summary>Sets with at least one disc that did not verify.</summary>
    public IReadOnlyList<DiscSetVerdict> IncompleteSets => Sets.Where(set => !set.IsComplete).ToArray();

    /// <summary>Units in one state.</summary>
    public IReadOnlyList<VerifiedUnit> InState(VerificationState state) =>
        Units.Where(unit => unit.State == state).ToArray();

    /// <summary>Count per state, every state present even at zero.</summary>
    public IReadOnlyDictionary<VerificationState, int> Counts =>
        Enum.GetValues<VerificationState>().ToDictionary(state => state, state => Units.Count(unit => unit.State == state));

    /// <summary>Units whose detected format disagrees with their folder's declared qualifier.</summary>
    public IReadOnlyList<VerifiedUnit> FormatContradictions =>
        Units.Where(unit => unit.FormatContradiction is not null).ToArray();

    /// <summary>Units eligible to be renamed later.</summary>
    public IReadOnlyList<VerifiedUnit> RenameEligible => Units.Where(unit => unit.IsRenameEligible).ToArray();

    /// <summary>Throughput in bytes per second, or 0 when nothing was hashed.</summary>
    public double BytesPerSecond => Elapsed.TotalSeconds <= 0 ? 0 : BytesHashed / Elapsed.TotalSeconds;
}

/// <summary>Progress during a long run. Reporting it is mandatory, not decorative.</summary>
/// <param name="Completed">Units finished.</param>
/// <param name="Total">Units to do.</param>
/// <param name="CurrentName">Unit currently being read.</param>
/// <param name="BytesHashed">Bytes read so far.</param>
public sealed record VerificationProgress(int Completed, int Total, string CurrentName, long BytesHashed);
