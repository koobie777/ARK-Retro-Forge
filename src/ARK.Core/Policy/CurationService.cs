using ARK.Core.Naming;
using ARK.Core.Scanning;
using ARK.Core.Units;
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

        // The same engine the collection report runs over the catalog. Curation asks it what to
        // remove from your files; the report asks it what you should have. One implementation, so
        // the two answers can never describe different collections.
        var groups = VariantEngine
            .Group(eligible, candidate => candidate.Name, _vocabulary, policy)
            .Select(group => new VariantGroup(
                group.Key, group.Title, group.Members, group.Keep, group.Remove, group.Refusals, group.Reason))
            .OrderByDescending(group => group.Members.Count)
            .ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Grouped over every scanned disc unit, not just the eligible ones: a set is broken just as
        // thoroughly when the disc that would be left behind was excluded from curation.
        var sets = DiscSetGrouper.Group(scan.Units.Select(unit => unit.Unit), _vocabulary);

        return new CurationReport(scan.Root, policy, groups, excluded, sets);
    }

}
