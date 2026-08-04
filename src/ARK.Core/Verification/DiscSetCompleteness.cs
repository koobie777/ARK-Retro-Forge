using ARK.Core.Units;

namespace ARK.Core.Verification;

/// <summary>How a multi-disc set stands once every disc in it has been judged.</summary>
public enum DiscSetState
{
    /// <summary>Every disc verified.</summary>
    Complete = 0,

    /// <summary>At least one disc is Mismatched — present, named right, wrong bytes.</summary>
    Damaged,

    /// <summary>At least one disc was never judged, or is still being written.</summary>
    Unresolved,
}

/// <summary>
/// One set's verdict, derived from its discs.
/// </summary>
/// <param name="Set">The set.</param>
/// <param name="State">The verdict.</param>
/// <param name="Detail">Which discs caused it.</param>
public sealed record DiscSetVerdict(DiscSet Set, DiscSetState State, string Detail)
{
    /// <summary>True when every disc verified.</summary>
    public bool IsComplete => State == DiscSetState.Complete;
}

/// <summary>
/// Joins a verification report to the multi-disc sets it covers.
/// </summary>
/// <remarks>
/// <b>A set is only as complete as its worst disc.</b> Reporting Disc 1 as Verified while Disc 2 is
/// corrupt describes a game the user cannot finish as though nothing were wrong — and the disc that
/// fails is usually the one they reach hours in.
/// </remarks>
public static class DiscSetCompleteness
{
    /// <summary>Judges every multi-disc set against a verification report.</summary>
    public static IReadOnlyList<DiscSetVerdict> Evaluate(
        IEnumerable<DiscSet> sets,
        VerificationReport verification)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(verification);

        var states = new Dictionary<string, VerificationState>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in verification.Units)
        {
            states[unit.Path] = unit.State;
        }

        return sets.Where(set => set.IsMultiDisc).Select(set => Judge(set, states)).ToArray();
    }

    private static DiscSetVerdict Judge(DiscSet set, IReadOnlyDictionary<string, VerificationState> states)
    {
        var damaged = new List<string>();
        var unresolved = new List<string>();

        foreach (var unit in set.Units)
        {
            var state = states.TryGetValue(unit.PrimaryPath, out var found) ? found : (VerificationState?)null;

            switch (state)
            {
                case VerificationState.Verified:
                    break;

                case VerificationState.Mismatched:
                    damaged.Add(unit.Name.Title);
                    break;

                default:
                    // Unrecognized, In Progress, Excluded, or never judged. None of them say the
                    // disc is good, and a set is not complete on an absence of bad news.
                    unresolved.Add(unit.Name.Title);
                    break;
            }
        }

        if (damaged.Count > 0)
        {
            return new DiscSetVerdict(set, DiscSetState.Damaged,
                $"{damaged.Count} of {set.Units.Count} discs mismatched — the set is incomplete until every disc verifies");
        }

        if (unresolved.Count > 0)
        {
            return new DiscSetVerdict(set, DiscSetState.Unresolved,
                $"{unresolved.Count} of {set.Units.Count} discs were not verified");
        }

        return new DiscSetVerdict(set, DiscSetState.Complete, $"all {set.Units.Count} discs verified");
    }
}
