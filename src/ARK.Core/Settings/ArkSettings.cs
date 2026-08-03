namespace ARK.Core.Settings;

/// <summary>
/// Persisted, system-agnostic settings for an instance. Deliberately minimal — fields for later
/// phases (e.g. policy profiles, which belong to Phase 8) are not speculated here.
/// </summary>
public sealed record ArkSettings
{
    /// <summary>Current on-disk schema version, stamped into new settings.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of this settings payload; present from day one so migration is possible.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Configured ROM root, or null when unset.</summary>
    public string? RomRoot { get; init; }

    /// <summary>Active system code, persisted so it survives exit and defaults the next launch.</summary>
    public string? ActiveSystem { get; init; }

    /// <summary>
    /// Directories the user has declared as incomplete-download locations. Anything inside one is
    /// treated as still being written and is never judged against a DAT.
    /// </summary>
    /// <remarks>
    /// This is the one in-progress signal a client cannot fail to provide. Extensions depend on
    /// the client being configured to append them, and mtime depends on the transfer being active
    /// right now; a declared directory is true regardless.
    /// </remarks>
    public IReadOnlyList<string> IncompleteDownloadDirectories { get; init; } = [];
}
