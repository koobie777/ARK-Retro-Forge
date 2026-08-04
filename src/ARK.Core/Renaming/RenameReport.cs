using ARK.Core.Units;

namespace ARK.Core.Renaming;

/// <summary>Where a new name comes from.</summary>
public enum RenameMode
{
    /// <summary>
    /// The name comes from the DAT entry the unit's hash confirmed. Authoritative, and the only
    /// mode that runs without a flag.
    /// </summary>
    Canonicalize = 0,

    /// <summary>
    /// The unit's existing name, reformatted. Repair for a collection previously damaged — stacked
    /// tags, scrambled order, <c>Disk</c>/<c>Disc</c> variants, un-inverted articles.
    /// </summary>
    /// <remarks>
    /// <b>Makes no claim that the result is correct</b>, only that it is well-formed. It is not
    /// identification, and it never runs by default.
    /// </remarks>
    Normalize,
}

/// <summary>What happened to one unit.</summary>
public enum RenameOutcome
{
    /// <summary>The name would change.</summary>
    Rename = 0,

    /// <summary>Already carrying the right name. No filesystem write at all.</summary>
    AlreadyCorrect,

    /// <summary>Left alone, with a stated reason.</summary>
    Refused,
}

/// <summary>Why a unit was not renamed.</summary>
public enum RenameRefusal
{
    /// <summary>Not refused.</summary>
    None = 0,

    /// <summary>
    /// No DAT entry matched, so there is no known canonical name. ARK does not invent one from
    /// the filename — that is exactly what v1 did.
    /// </summary>
    NoDatMatch,

    /// <summary>
    /// Not Verified. A corrupt file renamed to its canonical name looks verified forever after.
    /// </summary>
    NotVerified,

    /// <summary>Its name could not be tokenized, so normalizing it is not possible.</summary>
    Unparseable,

    /// <summary>
    /// Another unit wants the same name. Either they are duplicates — a different phase's job — or
    /// one is misidentified. Both are reported; neither is overwritten or suffixed.
    /// </summary>
    Collision,

    /// <summary>Inside a directory showing active-download signals.</summary>
    ActiveDownload,
}

/// <summary>One unit's rename decision.</summary>
/// <param name="Unit">The game unit. Operations take whole units, never files inside them.</param>
/// <param name="CurrentName">The file name it has now.</param>
/// <param name="ProposedName">The file name it would have. Equal to <paramref name="CurrentName"/> when already correct.</param>
/// <param name="Outcome">What happens.</param>
/// <param name="Refusal">Why not, when refused.</param>
/// <param name="Detail">Human-readable explanation.</param>
/// <param name="Source">Where the proposed name came from — the DAT entry, or the existing name.</param>
public sealed record RenameDecision(
    GameUnit Unit,
    string CurrentName,
    string ProposedName,
    RenameOutcome Outcome,
    RenameRefusal Refusal = RenameRefusal.None,
    string Detail = "",
    string? Source = null)
{
    /// <summary>Primary path of the unit.</summary>
    public string Path => Unit.PrimaryPath;

    /// <summary>True when this decision results in a filesystem write.</summary>
    public bool Writes => Outcome == RenameOutcome.Rename;
}

/// <summary>
/// The result of planning renames. Read-only: producing this moves nothing.
/// </summary>
/// <param name="Root">The analyzed root.</param>
/// <param name="Mode">Which mode produced the proposed names.</param>
/// <param name="Decisions">Every unit's decision.</param>
public sealed record RenameReport(string Root, RenameMode Mode, IReadOnlyList<RenameDecision> Decisions)
{
    /// <summary>Units that would be renamed.</summary>
    public IReadOnlyList<RenameDecision> Renames =>
        Decisions.Where(decision => decision.Outcome == RenameOutcome.Rename).ToArray();

    /// <summary>
    /// Units already carrying the right name. On a conformant set this is everything, and the
    /// whole operation is a no-op.
    /// </summary>
    public IReadOnlyList<RenameDecision> AlreadyCorrect =>
        Decisions.Where(decision => decision.Outcome == RenameOutcome.AlreadyCorrect).ToArray();

    /// <summary>Units left alone, each with a reason.</summary>
    public IReadOnlyList<RenameDecision> Refused =>
        Decisions.Where(decision => decision.Outcome == RenameOutcome.Refused).ToArray();

    /// <summary>Units refused for a given reason.</summary>
    public IReadOnlyList<RenameDecision> RefusedFor(RenameRefusal refusal) =>
        Decisions.Where(decision => decision.Refusal == refusal).ToArray();
}
