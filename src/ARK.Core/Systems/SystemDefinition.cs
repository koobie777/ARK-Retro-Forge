namespace ARK.Core.Systems;

/// <summary>
/// A supported system, loaded from <c>config/systems/*.json</c>. Definitions are data, not code:
/// adding a system is editing a file, not recompiling.
/// </summary>
public sealed record SystemDefinition
{
    /// <summary>Short identifier (e.g. <c>n64</c>, <c>snes</c>, <c>nes</c>).</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable name (e.g. "Nintendo 64").</summary>
    public required string DisplayName { get; init; }

    /// <summary>Folder- and input-matching aliases (e.g. "Nintendo - Nintendo 64", "N64").</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>ROM file extensions (e.g. <c>.z64</c>, <c>.n64</c>, <c>.v64</c>).</summary>
    public IReadOnlyList<string> RomExtensions { get; init; } = [];

    /// <summary>Archive extensions reference collections ship in (e.g. <c>.zip</c>, <c>.7z</c>).</summary>
    public IReadOnlyList<string> ArchiveExtensions { get; init; } = [];

    /// <summary>
    /// Format qualifiers (byte order <c>BigEndian/ByteSwapped/LittleEndian</c>, header
    /// <c>Headered/Headerless</c>). A headered ROM will not match a headerless DAT; the qualifier
    /// participates in choosing which DAT variant to match. Recorded here — header stripping and
    /// byte-order normalization belong to the hashing phase, not this one.
    /// </summary>
    public IReadOnlyList<string> FormatQualifiers { get; init; } = [];

    /// <summary>Names of the DAT sources (in <c>dat-sources.json</c>) that cover this system.</summary>
    public IReadOnlyList<string> DatSources { get; init; } = [];
}
