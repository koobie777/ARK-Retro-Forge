using ARK.Core.Instances;
using ARK.Core.Naming;
using ARK.Core.Scanning;
using ARK.Core.Verification;

namespace ARK.Core.Renaming;

/// <summary>
/// Decides what each unit should be called.
/// </summary>
/// <remarks>
/// <para>
/// <b>ARK never derives a canonical name from a filename.</b> The tokenizer exists to understand
/// names, never to authorize them. A unit with no DAT match has no known canonical name and is
/// refused — that single rule is what separates this phase from v1, which reformatted whatever it
/// read and turned <c>Crash Bandicoot (USA)</c> into <c>(USA) (USA) (USA)</c> one run at a time.
/// </para>
/// <para>
/// Deciding moves nothing. This produces a report; planning and applying are separate steps.
/// </para>
/// </remarks>
public sealed class RenameService
{
    private readonly NameFormatter _formatter;

    /// <summary>Creates a rename service.</summary>
    public RenameService(NameFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _formatter = formatter;
    }

    /// <summary>Decides names for every unit in a scanned and verified set.</summary>
    public RenameReport Decide(ScanReport scan, VerificationReport verification, RenameMode mode)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(verification);

        var states = verification.Units.ToDictionary(unit => unit.Path, StringComparer.OrdinalIgnoreCase);
        var decisions = new List<RenameDecision>();

        foreach (var scanned in scan.Units)
        {
            var unit = scanned.Unit;
            var file = unit.Files[0];
            states.TryGetValue(unit.PrimaryPath, out var verified);

            // Checked before either mode. Renaming a file an active client owns breaks the
            // transfer, and on a completed torrent still seeding it silently breaks the seed —
            // true whether the new name came from the DAT or from reformatting the old one.
            if (verified?.State == VerificationState.InProgress)
            {
                decisions.Add(Refuse(unit, file, RenameRefusal.ActiveDownload,
                    "still being written — renaming it would break the transfer"));
                continue;
            }

            decisions.Add(mode == RenameMode.Canonicalize
                ? Canonicalize(scanned, verified, file)
                : Normalize(scanned, file));
        }

        // Collisions are settled across the whole batch, because a name is only ambiguous relative
        // to what else wants it.
        return new RenameReport(scan.Root, mode, ResolveCollisions(decisions));
    }

    private static RenameDecision Canonicalize(ScannedUnit scanned, VerifiedUnit? verified, FileEntry file)
    {
        var unit = scanned.Unit;

        if (scanned.Match is null)
        {
            return Refuse(unit, file, RenameRefusal.NoDatMatch,
                "no DAT entry matched, so there is no known canonical name — one is never invented from the filename");
        }

        // The hash is what makes the DAT entry authoritative. The name proposed the entry; the
        // hash proved it. Without that proof a rename would launder a corrupt file into something
        // that looks verified forever after.
        if (verified?.State != VerificationState.Verified)
        {
            return Refuse(unit, file, RenameRefusal.NotVerified,
                $"state is {verified?.State.ToString() ?? "unknown"}; only Verified units are canonicalized");
        }

        var entry = scanned.Match;
        var canonical = entry.GameName is { Length: > 0 } gameName ? gameName : StripExtension(entry.RomName);

        // The archive is renamed; the entry inside keeps its name. Rewriting the archive would
        // mean recompressing it — slow, changes the archive bytes, and touches ROM data for a
        // cosmetic gain that DAT verification does not care about.
        return Decide(unit, file, canonical + file.Extension, $"DAT entry in '{entry.DatName}'");
    }

    private RenameDecision Normalize(ScannedUnit scanned, FileEntry file)
    {
        var unit = scanned.Unit;

        if (!unit.Name.IsTokenizable)
        {
            return Refuse(unit, file, RenameRefusal.Unparseable,
                $"name flagged {unit.Name.Flag} — it cannot be reformatted safely");
        }

        // Repair, not identification: this asserts the name is well-formed, never that it is the
        // right name for these bytes.
        return Decide(unit, file, _formatter.Format(unit.Name) + file.Extension, "reformatted from its own name");
    }

    private static RenameDecision Decide(Units.GameUnit unit, FileEntry file, string proposed, string source)
    {
        // Ordinal, deliberately. A case-insensitive comparison would skip
        // `game (usa).zip` -> `Game (USA).zip`, which is a real correction.
        return string.Equals(file.Name, proposed, StringComparison.Ordinal)
            ? new RenameDecision(unit, file.Name, proposed, RenameOutcome.AlreadyCorrect,
                Detail: "already carries this name — no filesystem write", Source: source)
            : new RenameDecision(unit, file.Name, proposed, RenameOutcome.Rename, Source: source);
    }

    private static RenameDecision Refuse(Units.GameUnit unit, FileEntry file, RenameRefusal refusal, string detail) =>
        new(unit, file.Name, file.Name, RenameOutcome.Refused, refusal, detail);

    /// <summary>
    /// Refuses every unit in a group that wants the same name.
    /// </summary>
    /// <remarks>
    /// Two units canonicalizing to one name means either they are duplicates — a different phase's
    /// job — or one is misidentified. Both are reported. Never overwrite, and never append a
    /// disambiguating suffix: a fabricated name is the thing this phase must never produce.
    /// </remarks>
    private static IReadOnlyList<RenameDecision> ResolveCollisions(List<RenameDecision> decisions)
    {
        var wanted = decisions
            .Where(decision => decision.Writes)
            .GroupBy(decision => RenamePaths.DestinationFor(decision.Path, decision.ProposedName),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToHashSet();

        if (wanted.Count == 0)
        {
            return decisions;
        }

        return decisions
            .Select(decision => wanted.Contains(decision)
                ? decision with
                {
                    Outcome = RenameOutcome.Refused,
                    Refusal = RenameRefusal.Collision,
                    Detail = $"another unit also wants '{decision.ProposedName}' — either they are duplicates or one is misidentified",
                }
                : decision)
            .ToArray();
    }

    private static string StripExtension(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot > 0 && value.Length - dot <= 5 ? value[..dot] : value;
    }
}
