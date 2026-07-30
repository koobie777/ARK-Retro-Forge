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
}
