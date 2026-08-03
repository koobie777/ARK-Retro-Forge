using ARK.Core.Naming;
using ARK.Core.Scanning;
using ARK.Core.Verification;

namespace ARK.Core.Policy;

/// <summary>
/// Groups releases of the same game and applies a policy to decide what to keep.
/// </summary>
/// <remarks>
/// <para>
/// Everything here has <b>different bytes</b> and is therefore a legitimate, distinct release.
/// Deduplication could argue a removed file was recoverable from its byte-identical twin; nothing
/// here can. Every removal is a real loss of a real release, which is why report-only ships as the
/// default and why any group the policy cannot order is skipped whole.
/// </para>
/// <para>
/// Analysis moves nothing. It produces a report; quarantining and sorting are separate steps.
/// </para>
/// </remarks>
public sealed class CurationService
{
    private readonly NameTokenizer _tokenizer;
    private readonly TokenVocabulary _vocabulary;

    /// <summary>Creates a curation service.</summary>
    public CurationService(NameTokenizer tokenizer, TokenVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(vocabulary);
        _tokenizer = tokenizer;
        _vocabulary = vocabulary;
    }

    /// <summary>Applies <paramref name="policy"/> to a scanned and verified set.</summary>
    public CurationReport Analyze(ScanReport scan, VerificationReport verification, VariantPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(policy);

        var states = verification.Units.ToDictionary(unit => unit.Path, StringComparer.OrdinalIgnoreCase);
        var excluded = new List<(CurationCandidate, CurationExclusion, string)>();
        var eligible = new List<CurationCandidate>();

        foreach (var scanned in scan.Units)
        {
            var unit = scanned.Unit;
            states.TryGetValue(unit.PrimaryPath, out var verified);
            var candidate = new CurationCandidate(
                unit, unit.Name, verified?.State ?? VerificationState.Unrecognized);

            if (!unit.Name.IsTokenizable)
            {
                excluded.Add((candidate, CurationExclusion.Unparseable,
                    $"name flagged {unit.Name.Flag} — it cannot be placed on any axis"));
                continue;
            }

            if (VariantGrouping.IsNonGameContent(unit.Name))
            {
                excluded.Add((candidate, CurationExclusion.NonGameContent,
                    "non-game content — comparing it against retail is a category error"));
                continue;
            }

            switch (candidate.State)
            {
                case VerificationState.InProgress:
                    excluded.Add((candidate, CurationExclusion.InProgress, "still being written — not judged"));
                    continue;
                case VerificationState.Mismatched:
                    excluded.Add((candidate, CurationExclusion.Mismatched,
                        "claims to be a release its bytes disagree with — reported, never curated"));
                    continue;
                case VerificationState.Excluded:
                    excluded.Add((candidate, CurationExclusion.NotRomContent, "not ROM content"));
                    continue;
                default:
                    eligible.Add(candidate);
                    break;
            }
        }

        var groups = eligible
            .GroupBy(candidate => VariantGrouping.KeyFor(candidate.Name, _vocabulary, policy), StringComparer.Ordinal)
            .Select(group => Decide(group.Key, group.ToArray(), policy))
            .OrderByDescending(group => group.Members.Count)
            .ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CurationReport(scan.Root, policy, groups, excluded);
    }

    private static VariantGroup Decide(string key, IReadOnlyList<CurationCandidate> members, VariantPolicy policy)
    {
        var title = members[0].Name.Title;

        if (members.Count == 1)
        {
            return new VariantGroup(key, title, members, members, [], [], "single release — nothing to choose between");
        }

        // Deliberately no early exit for a policy that removes nothing. A policy can treat axes as
        // variance purely to form and rank groups — that is what drives collection reports — and
        // short-circuiting here would hide the very refusals such a report exists to surface.

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
                    if (AxisOrdering.Compare(axis, members[i].Name, members[j].Name, out var reason) == AxisComparison.Incomparable)
                    {
                        refusals.Add(new AxisOrdering.Refusal(axis, reason!));
                        break;
                    }
                }
            }
        }

        if (refusals.Count > 0)
        {
            return new VariantGroup(key, title, members, members, [], refusals,
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

            var survivors = Select(keep, axis, rule, policy, out var why);
            if (survivors.Count > 0 && survivors.Count < keep.Count)
            {
                reasons.Add(why);
                keep = survivors;
            }
        }

        var remove = members.Where(member => !keep.Contains(member)).ToArray();

        return new VariantGroup(
            key, title, members, keep, remove, [],
            remove.Length == 0
                ? (policy.IsReportOnly ? "report only — nothing is chosen and nothing moves" : "policy removes nothing from this group")
                : string.Join("; ", reasons));
    }

    private static List<CurationCandidate> Select(
        List<CurationCandidate> members,
        VariantAxis axis,
        AxisRule rule,
        VariantPolicy policy,
        out string reason)
    {
        if (rule.Selection == AxisSelection.KeepMatching)
        {
            var wanted = rule.Keep;
            var survivors = members.Where(member => Matches(member.Name, axis, wanted)).ToList();
            reason = $"{axis}: kept {string.Join(", ", wanted)}";
            return survivors;
        }

        // KeepBest. Region has no inherent order, so it is resolved by the user's stated
        // preference rather than by ranking — and with no preference stated, nothing is dropped.
        if (axis == VariantAxis.Region)
        {
            foreach (var preferred in policy.Regions)
            {
                var survivors = members.Where(member => member.Name.Regions.Contains(preferred, StringComparer.OrdinalIgnoreCase)).ToList();
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
            if (AxisOrdering.Compare(axis, member.Name, best.Name, out _) == AxisComparison.LeftWins)
            {
                best = member;
            }
        }

        // Everything tied with the winner survives, so an axis only removes what it can actually
        // rank below something else.
        var kept = members
            .Where(member => AxisOrdering.Compare(axis, member.Name, best.Name, out _) == AxisComparison.Equal
                || ReferenceEquals(member, best))
            .ToList();

        reason = $"{axis}: kept the highest-ranked";
        return kept;
    }

    private static bool Matches(ParsedName name, VariantAxis axis, IReadOnlyList<string> wanted)
    {
        var values = ValuesFor(name, axis);

        // No token on the axis means the untagged case: retail, licensed, original distribution.
        if (values.Count == 0)
        {
            return wanted.Contains(AxisRule.Untagged, StringComparer.OrdinalIgnoreCase);
        }

        return values.Any(value => wanted.Contains(value, StringComparer.OrdinalIgnoreCase));
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
