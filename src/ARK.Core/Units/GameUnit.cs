using ARK.Core.Naming;
using ARK.Core.Scanning;

namespace ARK.Core.Units;

/// <summary>The shape of a game unit.</summary>
public enum GameUnitKind
{
    /// <summary>One archive containing one ROM, or one bare ROM file.</summary>
    Cartridge = 0,

    /// <summary>One CUE plus every BIN it references, grouped with its disc siblings.</summary>
    Disc,
}

/// <summary>Whether a unit is malformed, or merely a format no resolver handles yet.</summary>
public enum UnitIssueKind
{
    /// <summary>
    /// Genuinely malformed or unexpected — an unreadable archive, or two distinct games in one
    /// archive. These are findings about the collection and warrant attention.
    /// </summary>
    Anomaly = 0,

    /// <summary>
    /// Correct structure that no resolver handles yet, such as a <c>.cue</c> plus its <c>.bin</c>
    /// tracks while the disc resolver is stubbed. <b>Not a defect in the collection</b> and must
    /// not read as one.
    /// </summary>
    UnsupportedFormat,
}

/// <summary>Something about a unit that ARK will not resolve by guessing.</summary>
/// <remarks>
/// The two kinds are kept apart because conflating them destroys the report. On the reference
/// drive 1,762 of 1,765 PlayStation archives hold a <c>.bin</c> + <c>.cue</c> pair — the correct
/// structure of a disc image — and listing them as anomalies buried the five genuinely corrupt
/// archives underneath. An anomaly list that is 99.8% normal files is a list nobody reads.
/// </remarks>
/// <param name="Kind">Malformed, or just unhandled.</param>
/// <param name="Code">Stable machine-readable code.</param>
/// <param name="Detail">What was observed.</param>
public sealed record GameUnitIssue(UnitIssueKind Kind, string Code, string Detail);

/// <summary>One entry inside an archive. Metadata only — nothing is extracted to obtain it.</summary>
/// <param name="Name">Entry path within the archive.</param>
/// <param name="Size">Uncompressed size in bytes, or 0 when the format does not report it.</param>
public sealed record ArchiveEntry(string Name, long Size);

/// <summary>
/// The atom every later operation acts on. Nothing downstream ever sees a bare file.
/// </summary>
/// <remarks>
/// <para>
/// A cartridge unit looks trivially like one file today, which is exactly why the type exists
/// now: when disc support lands, a CUE plus its BINs and disc siblings becomes one more resolver
/// rather than a rewrite of everything downstream. Deleting a byte-identical Disc 2 out of an
/// otherwise complete set is the failure class this prevents.
/// </para>
/// <para>
/// <see cref="FormatQualifiers"/> is recorded, never acted on. Header stripping and byte-order
/// normalization belong to the hashing phase.
/// </para>
/// </remarks>
/// <param name="Kind">Cartridge or disc.</param>
/// <param name="PrimaryPath">The archive, or the bare ROM.</param>
/// <param name="Files">Every file belonging to this unit. Operations take all of them or none.</param>
/// <param name="Name">Tokenized form of the unit's parseable name — the archive's, not the ROM's inside.</param>
/// <param name="SetFolder">Leaf name of the directory the unit was found in.</param>
/// <param name="FormatQualifiers">Qualifiers read off the set folder, e.g. <c>BigEndian</c>, <c>Headered</c>.</param>
/// <param name="Contents">Archive entry list; empty for a bare ROM.</param>
/// <param name="Issues">Anything ARK declines to resolve by guessing.</param>
public sealed record GameUnit(
    GameUnitKind Kind,
    string PrimaryPath,
    IReadOnlyList<FileEntry> Files,
    ParsedName Name,
    string SetFolder,
    IReadOnlyList<string> FormatQualifiers,
    IReadOnlyList<ArchiveEntry> Contents,
    IReadOnlyList<GameUnitIssue> Issues)
{
    /// <summary>Total bytes across every file in the unit.</summary>
    public long TotalSize => Files.Sum(file => file.Size);

    /// <summary>Issues that are genuine findings about the collection.</summary>
    public IReadOnlyList<GameUnitIssue> Anomalies =>
        Issues.Where(issue => issue.Kind == UnitIssueKind.Anomaly).ToArray();

    /// <summary>Issues that only mean "no resolver for this yet".</summary>
    public IReadOnlyList<GameUnitIssue> UnsupportedFormats =>
        Issues.Where(issue => issue.Kind == UnitIssueKind.UnsupportedFormat).ToArray();

    /// <summary>True when something about this unit is malformed and needs a human.</summary>
    public bool HasAnomalies => Issues.Any(issue => issue.Kind == UnitIssueKind.Anomaly);

    /// <summary>True when the unit is well-formed but no resolver handles its shape yet.</summary>
    public bool IsUnsupportedFormat => Issues.Any(issue => issue.Kind == UnitIssueKind.UnsupportedFormat);
}
