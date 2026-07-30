namespace ARK.Core.Dat;

/// <summary>Header metadata of a Logiqx DAT file, used for identity and staleness reporting.</summary>
public sealed record LogiqxHeader
{
    /// <summary>DAT name (e.g. "Nintendo - Nintendo 64"). Identifies the DAT for idempotent import.</summary>
    public string? Name { get; init; }

    /// <summary>DAT description (often carries the format qualifier, e.g. "... (BigEndian)").</summary>
    public string? Description { get; init; }

    /// <summary>Version string (No-Intro uses a timestamp; Redump a date-like token).</summary>
    public string? Version { get; init; }

    /// <summary>Publication date as written in the header.</summary>
    public string? Date { get; init; }

    /// <summary>Author/group (e.g. "No-Intro", "redump.org").</summary>
    public string? Author { get; init; }
}

/// <summary>A single ROM entry within a DAT: its game, file name, size, and available hashes.</summary>
public sealed record DatEntry
{
    /// <summary>Name of the game the ROM belongs to.</summary>
    public required string GameName { get; init; }

    /// <summary>ROM file name.</summary>
    public required string RomName { get; init; }

    /// <summary>ROM size in bytes, when declared.</summary>
    public long? Size { get; init; }

    /// <summary>CRC32 (lower-case hex), when declared.</summary>
    public string? Crc32 { get; init; }

    /// <summary>MD5 (lower-case hex), when declared.</summary>
    public string? Md5 { get; init; }

    /// <summary>SHA1 (lower-case hex), when declared.</summary>
    public string? Sha1 { get; init; }
}

/// <summary>A parsed Logiqx DAT: its header plus every ROM entry.</summary>
public sealed record LogiqxDat(LogiqxHeader Header, IReadOnlyList<DatEntry> Entries);
