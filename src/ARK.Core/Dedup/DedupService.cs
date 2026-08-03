using ARK.Core.Hashing;
using ARK.Core.Scanning;
using ARK.Core.Verification;

namespace ARK.Core.Dedup;

/// <summary>
/// Finds byte-identical game units and decides which copy to keep.
/// </summary>
/// <remarks>
/// <para>
/// <b>Duplicates are byte-identical ROM content.</b> Variants are not duplicates: Rev 0 and Rev 1
/// have different hashes, as do Beta, Proto, Demo and Sample. Removing those is a curation
/// preference belonging to the policy engine — a different subsystem with a different risk
/// profile — and the two are never conflated here.
/// </para>
/// <para>
/// Comparison is on the ROM inside the archive, never the archive itself. Zip compression is not
/// deterministic, so two archives holding the identical ROM have entirely different archive bytes.
/// </para>
/// <para>
/// Analysis moves nothing. It produces a report; quarantining is a separate, explicit step.
/// </para>
/// </remarks>
public sealed class DedupService
{
    private readonly RomHasher _hasher;
    private readonly HashCache _cache;

    /// <summary>Creates a dedup service.</summary>
    public DedupService(RomHasher hasher, HashCache cache)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(cache);
        _hasher = hasher;
        _cache = cache;
    }

    /// <summary>
    /// Analyzes a scanned and verified set.
    /// </summary>
    public DedupReport Analyze(
        ScanReport scan,
        VerificationReport verification,
        KeepPolicy policy = KeepPolicy.ReportOnly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(verification);

        var states = verification.Units.ToDictionary(unit => unit.Path, StringComparer.OrdinalIgnoreCase);
        var excluded = new List<DedupExclusionEntry>();
        var eligible = new List<DedupCandidate>();

        foreach (var scanned in scan.Units)
        {
            var candidate = Describe(scanned, states);

            switch (candidate.State)
            {
                case VerificationState.InProgress:
                    excluded.Add(new DedupExclusionEntry(candidate, DedupExclusion.InProgress,
                        "still being written — its hash describes bytes that are about to change"));
                    continue;

                case VerificationState.Mismatched:
                    excluded.Add(new DedupExclusionEntry(candidate, DedupExclusion.Mismatched,
                        "reported, not acted on — collapsing two corrupt copies would hide the second"));
                    continue;

                case VerificationState.Excluded:
                    excluded.Add(new DedupExclusionEntry(candidate, DedupExclusion.NotRomContent, "not ROM content"));
                    continue;

                default:
                    eligible.Add(candidate);
                    break;
            }
        }

        var groups = new List<DuplicateGroup>();

        // Tier 1 — group by ROM size. Free, straight off the archive's entry list. A unique size
        // cannot have a byte-identical twin, so it is never opened.
        foreach (var sized in eligible.GroupBy(candidate => candidate.RomSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sized.Count() == 1)
            {
                excluded.Add(new DedupExclusionEntry(sized.First(), DedupExclusion.UniqueSize,
                    "unique ROM size — cannot have a byte-identical twin, so it was never hashed"));
                continue;
            }

            // Tier 2 — CRC32 the survivors, from the cache where it is already known.
            var hashed = new List<DedupCandidate>();
            foreach (var candidate in sized)
            {
                var withHash = WithCrc32(candidate);
                if (withHash.Crc32 is null)
                {
                    excluded.Add(new DedupExclusionEntry(candidate, DedupExclusion.Unhashable,
                        "could not be hashed, so it cannot be compared"));
                    continue;
                }

                hashed.Add(withHash);
            }

            foreach (var byCrc in hashed.GroupBy(candidate => candidate.Crc32, StringComparer.OrdinalIgnoreCase))
            {
                if (byCrc.Count() == 1)
                {
                    continue;
                }

                groups.AddRange(Confirm(byCrc.ToArray(), sized.Key, policy, cancellationToken));
            }
        }

        var hashedCount = _cache.Misses;
        return new DedupReport(scan.Root, groups, excluded, policy, hashedCount);
    }

    // Tier 3 — a shared CRC32 is not proof. 32-bit collisions are real at this scale, so any
    // group that survives tier 2 is confirmed by SHA1 before a single file is called redundant.
    private IReadOnlyList<DuplicateGroup> Confirm(
        IReadOnlyList<DedupCandidate> sameCrc,
        long romSize,
        KeepPolicy policy,
        CancellationToken cancellationToken)
    {
        var confirmed = new List<DuplicateGroup>();

        var withSha1 = new List<DedupCandidate>();
        foreach (var candidate in sameCrc)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var full = WithSha1(candidate);
            if (full.Sha1 is not null)
            {
                withSha1.Add(full);
            }
        }

        foreach (var bySha1 in withSha1.GroupBy(candidate => candidate.Sha1, StringComparer.OrdinalIgnoreCase))
        {
            if (bySha1.Count() < 2)
            {
                // The CRC32 matched and the SHA1 did not: a genuine collision, not a duplicate.
                continue;
            }

            var members = bySha1.ToArray();
            var (keep, reason) = KeepSelector.Choose(members, policy);
            confirmed.Add(new DuplicateGroup(romSize, members[0].Crc32!, bySha1.Key, members, keep, reason));
        }

        return confirmed;
    }

    private DedupCandidate WithCrc32(DedupCandidate candidate)
    {
        if (candidate.Crc32 is not null)
        {
            return candidate;
        }

        var file = candidate.Unit.Files[0];
        var cached = _cache.TryGet(file.FullPath, file.Size, file.ModifiedUtc);
        if (cached is not null)
        {
            return candidate with { Crc32 = cached.Crc32, Sha1 = cached.Sha1 };
        }

        var result = _hasher.Compute(file, HashPurpose.Verification);
        if (!result.Succeeded)
        {
            return candidate;
        }

        _cache.Store(file.FullPath, file.Size, file.ModifiedUtc, result.Hash!);
        return candidate with { Crc32 = result.Hash!.Crc32 };
    }

    private DedupCandidate WithSha1(DedupCandidate candidate)
    {
        if (candidate.Sha1 is not null)
        {
            return candidate;
        }

        var file = candidate.Unit.Files[0];
        var result = _hasher.Compute(file, HashPurpose.Identification);
        if (!result.Succeeded)
        {
            return candidate;
        }

        _cache.Store(file.FullPath, file.Size, file.ModifiedUtc, result.Hash!);
        return candidate with { Crc32 = result.Hash!.Crc32, Sha1 = result.Hash.Sha1 };
    }

    private static DedupCandidate Describe(ScannedUnit scanned, IReadOnlyDictionary<string, VerifiedUnit> states)
    {
        var unit = scanned.Unit;
        var file = unit.Files[0];

        // The ROM's own size, taken from the archive's entry list — known without opening the
        // entry, and the only size worth grouping on.
        var romSize = unit.Contents.Count == 1 ? unit.Contents[0].Size : file.Size;

        states.TryGetValue(unit.PrimaryPath, out var verified);

        return new DedupCandidate(
            unit,
            romSize,
            verified?.ActualCrc32,
            null,
            verified?.State ?? VerificationState.Unrecognized,
            verified?.DatName ?? scanned.Match?.DatName);
    }
}
