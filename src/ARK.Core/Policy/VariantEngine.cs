using ARK.Core.Naming;

namespace ARK.Core.Policy;

/// <summary>
/// One group of same-game releases after a policy has ranked them, independent of what the
/// releases actually are.
/// </summary>
/// <typeparam name="T">Whatever carries the release — a scanned unit, or a catalog entry.</typeparam>
/// <param name="Key">The grouping key.</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="Members">Every release in the group.</param>
/// <param name="Keep">Releases the policy keeps.</param>
/// <param name="Remove">Releases the policy drops. Always empty when refused.</param>
/// <param name="Refusals">Axes that could not be ordered.</param>
/// <param name="Reason">Summary of the decision.</param>
public sealed record RankedGroup<T>(
    string Key,
    string Title,
    IReadOnlyList<T> Members,
    IReadOnlyList<T> Keep,
    IReadOnlyList<T> Remove,
    IReadOnlyList<AxisOrdering.Refusal> Refusals,
    string Reason)
{
    /// <summary>True when an axis could not be ordered, so the group is reported and skipped whole.</summary>
    public bool IsRefused => Refusals.Count > 0;
}

/// <summary>
/// Groups releases of the same game and applies a policy, over anything that can produce a
/// <see cref="ParsedName"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately generic, because the same engine has to answer two different questions with the
/// same rules. Curation runs the policy over <b>your files</b> to decide what to remove; the
/// collection report runs the identical policy over <b>the catalog</b> to decide what you should
/// have. If those two ever diverged, the reports would describe a collection the user is not
/// actually curating toward.
/// </para>
/// <para>
/// A user's target set is nothing more than this engine's output over the DAT.
/// </para>
/// </remarks>
public static class VariantEngine
{
    /// <summary>Groups and ranks <paramref name="items"/> under <paramref name="policy"/>.</summary>
    /// <param name="items">The releases.</param>
    /// <param name="nameOf">How to get a parsed name from one.</param>
    /// <param name="vocabulary">Vocabulary for grouping normalization.</param>
    /// <param name="policy">The policy to apply.</param>
    public static IReadOnlyList<RankedGroup<T>> Group<T>(
        IEnumerable<T> items,
        Func<T, ParsedName> nameOf,
        TokenVocabulary vocabulary,
        VariantPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(nameOf);
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(policy);

        return items
            .GroupBy(item => VariantGrouping.KeyFor(nameOf(item), vocabulary, policy), StringComparer.Ordinal)
            .Select(group => Decide(group.Key, group.ToArray(), nameOf, policy))
            .ToArray();
    }

    private static RankedGroup<T> Decide<T>(
        string key,
        IReadOnlyList<T> members,
        Func<T, ParsedName> nameOf,
        VariantPolicy policy)
    {
        var title = nameOf(members[0]).Title;

        if (members.Count == 1)
        {
            return new RankedGroup<T>(key, title, members, members, [], [], "single release — nothing to choose between");
        }

        // Refusals are collected across every variance axis before anything is decided. A group
        // with one unorderable axis is skipped whole: deciding the other axes would still be
        // acting on a group we do not fully understand.
        var refusals = new List<AxisOrdering.Refusal>();
        foreach (var axis in policy.VarianceAxes)
        {
            for (var i = 0; i < members.Count && refusals.All(refusal => refusal.Axis != axis); i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    if (AxisOrdering.Compare(axis, nameOf(members[i]), nameOf(members[j]), out var reason) == AxisComparison.Incomparable)
                    {
                        refusals.Add(new AxisOrdering.Refusal(axis, reason!));
                        break;
                    }
                }
            }
        }

        if (refusals.Count > 0)
        {
            return new RankedGroup<T>(key, title, members, members, [], refusals,
                "reported and skipped — an axis could not be ordered");
        }

        var keep = members.ToList();
        var reasons = new List<string>();

        foreach (var axis in policy.VarianceAxes)
        {
            var rule = policy.RuleFor(axis);
            if (!rule.Removes || keep.Count <= 1)
            {
                continue;
            }

            var survivors = Select(keep, nameOf, axis, rule, policy, out var why);
            if (survivors.Count > 0 && survivors.Count < keep.Count)
            {
                reasons.Add(why);
                keep = survivors;
            }
        }

        var remove = members.Where(member => !keep.Contains(member)).ToArray();

        return new RankedGroup<T>(
            key, title, members, keep, remove, [],
            remove.Length == 0
                ? (policy.IsReportOnly ? "report only — nothing is chosen and nothing moves" : "policy removes nothing from this group")
                : string.Join("; ", reasons));
    }

    private static List<T> Select<T>(
        List<T> members,
        Func<T, ParsedName> nameOf,
        VariantAxis axis,
        AxisRule rule,
        VariantPolicy policy,
        out string reason)
    {
        if (rule.Selection == AxisSelection.KeepMatching)
        {
            var wanted = rule.Keep;
            reason = $"{axis}: kept {string.Join(", ", wanted)}";
            return members.Where(member => Matches(nameOf(member), axis, wanted)).ToList();
        }

        // Region has no inherent order, so it is resolved by the user's stated preference rather
        // than by ranking — and with no preference stated, nothing is dropped.
        if (axis == VariantAxis.Region)
        {
            foreach (var preferred in policy.Regions)
            {
                var survivors = members.Where(member => SatisfiesRegion(nameOf(member), preferred)).ToList();
                if (survivors.Count > 0)
                {
                    reason = $"Region: kept {preferred} by preference order";
                    return survivors;
                }
            }

            reason = "Region: no preferred region present, kept all";
            return members;
        }

        var best = members[0];
        foreach (var member in members.Skip(1))
        {
            if (AxisOrdering.Compare(axis, nameOf(member), nameOf(best), out _) == AxisComparison.LeftWins)
            {
                best = member;
            }
        }

        // Everything tied with the winner survives, so an axis only removes what it can actually
        // rank below something else.
        reason = $"{axis}: kept the highest-ranked";
        return members
            .Where(member => AxisOrdering.Compare(axis, nameOf(member), nameOf(best), out _) == AxisComparison.Equal
                || ReferenceEquals(member, best))
            .ToList();
    }

    /// <summary>
    /// Whether a release satisfies a region target.
    /// </summary>
    /// <remarks>
    /// <b>Set intersection, not equality.</b> A release tagged <c>(USA, Europe)</c> satisfies a USA
    /// target — it is that release. And <c>(World)</c> means all regions, so it satisfies every
    /// region target; it is never collapsed into a region list, because "World" is a statement
    /// about scope rather than a member of one.
    /// </remarks>
    public static bool SatisfiesRegion(ParsedName name, string target)
    {
        ArgumentNullException.ThrowIfNull(name);

        var regions = name.Regions;
        return regions.Contains(target, StringComparer.OrdinalIgnoreCase)
            || regions.Contains("World", StringComparer.OrdinalIgnoreCase);
    }

    private static bool Matches(ParsedName name, VariantAxis axis, IReadOnlyList<string> wanted)
    {
        if (axis == VariantAxis.Region)
        {
            return wanted.Any(target => SatisfiesRegion(name, target));
        }

        var values = ValuesFor(name, axis);

        // No token on the axis means the untagged case: retail, licensed, original distribution.
        return values.Count == 0
            ? wanted.Contains(AxisRule.Untagged, StringComparer.OrdinalIgnoreCase)
            : values.Any(value => wanted.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ValuesFor(ParsedName name, VariantAxis axis) => axis switch
    {
        VariantAxis.Revision => name.TokensOf(TokenCategory.Revision).Select(token => token.Value).ToArray(),
        VariantAxis.Version => name.TokensOf(TokenCategory.Version).Select(token => token.Value).ToArray(),
        VariantAxis.DevStatus => name.TokensOf(TokenCategory.DevStatus).Select(token => token.Value).ToArray(),
        VariantAxis.Region => name.Regions,
        VariantAxis.Licensing => name.TokensOf(TokenCategory.Licensing).Select(token => token.Value).ToArray(),
        VariantAxis.Distribution => name.TokensOf(TokenCategory.Distribution).Select(token => token.Value).ToArray(),
        VariantAxis.Language => name.Languages,
        _ => [],
    };
}
