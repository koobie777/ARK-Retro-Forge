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

/// <summary>Something about a unit that ARK will not resolve by guessing.</summary>
/// <param name="Code">Stable machine-readable code.</param>
/// <param name="Detail">What was observed.</param>
public sealed record GameUnitAnomaly(string Code, string Detail);

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
/// <param name="Anomalies">Anything ARK declines to resolve by guessing.</param>
public sealed record GameUnit(
    GameUnitKind Kind,
    string PrimaryPath,
    IReadOnlyList<FileEntry> Files,
    ParsedName Name,
    string SetFolder,
    IReadOnlyList<string> FormatQualifiers,
    IReadOnlyList<ArchiveEntry> Contents,
    IReadOnlyList<GameUnitAnomaly> Anomalies)
{
    /// <summary>Total bytes across every file in the unit.</summary>
    public long TotalSize => Files.Sum(file => file.Size);

    /// <summary>True when something about this unit needs a human rather than a guess.</summary>
    public bool HasAnomalies => Anomalies.Count > 0;
}
