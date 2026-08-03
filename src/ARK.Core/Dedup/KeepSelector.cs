namespace ARK.Core.Dedup;

/// <summary>
/// Chooses which copy of a duplicate set to keep.
/// </summary>
/// <remarks>
/// <b>A policy that cannot break a tie reports the group and skips it.</b> Picking arbitrarily
/// would be a coin flip presented as a decision — a guess wearing a confident label, which is the
/// failure mode Prohibition 6 exists to prevent. The user is shown the group and decides.
/// </remarks>
public static class KeepSelector
{
    /// <summary>Applies <paramref name="policy"/> to <paramref name="members"/>.</summary>
    /// <returns>The unit to keep and why, or null and the reason no decision was possible.</returns>
    public static (DedupCandidate? Keep, string Reason) Choose(
        IReadOnlyList<DedupCandidate> members,
        KeepPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(members);

        if (members.Count == 0)
        {
            return (null, "no members");
        }

        return policy switch
        {
            KeepPolicy.ReportOnly => (null, "report only — nothing is chosen and nothing moves"),
            KeepPolicy.KeepIdentified => Best(
                members,
                candidate => candidate.DatName is { Length: > 0 } ? 1 : 0,
                "identified against a DAT",
                "every copy is equally identified"),
            KeepPolicy.KeepShortestPath => Best(
                members,
                candidate => -Depth(candidate.Path),
                "least-nested path",
                "every copy is equally nested"),
            KeepPolicy.KeepOldest => Best(
                members,
                candidate => -candidate.ModifiedUtc.UtcTicks,
                "oldest copy",
                "every copy shares the same timestamp"),
            KeepPolicy.KeepNewest => Best(
                members,
                candidate => candidate.ModifiedUtc.UtcTicks,
                "newest copy",
                "every copy shares the same timestamp"),
            _ => (null, $"unknown policy '{policy}'"),
        };
    }

    private static (DedupCandidate? Keep, string Reason) Best(
        IReadOnlyList<DedupCandidate> members,
        Func<DedupCandidate, long> score,
        string why,
        string tieReason)
    {
        var ranked = members
            .Select(candidate => (Candidate: candidate, Score: score(candidate)))
            .OrderByDescending(entry => entry.Score)
            .ToArray();

        // A single best score is a decision; a shared best score is a tie, and a tie is reported
        // rather than resolved.
        return ranked.Count(entry => entry.Score == ranked[0].Score) == 1
            ? (ranked[0].Candidate, $"kept: {why}")
            : (null, $"tie — {tieReason}; reported and skipped");
    }

    private static int Depth(string path) =>
        path.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar);
}
