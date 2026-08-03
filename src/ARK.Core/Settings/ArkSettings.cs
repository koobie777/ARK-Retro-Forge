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

    /// <summary>
    /// Name of the curation policy to use, or null for the report-only default.
    /// </summary>
    /// <remarks>
    /// Policies are data, not code. A preservationist and a casual player have opposite correct
    /// answers, so the choice is persisted rather than compiled in — and an unrecognized name
    /// resolves to nothing rather than silently falling back to one that removes files.
    /// </remarks>
    public string? CurationPolicy { get; init; }

    /// <summary>
    /// A user's own policy, stored as per-axis rules. Present only when they have composed one;
    /// the shipped presets are resolved by <see cref="CurationPolicy"/> name instead.
    /// </summary>
    public CustomPolicySettings? CustomCurationPolicy { get; init; }
}

/// <summary>
/// A user-composed curation policy, persisted as the same per-axis rules the presets use.
/// </summary>
/// <param name="Name">The user's label for it.</param>
/// <param name="Rules">One entry per axis they configured.</param>
/// <param name="PreferredRegions">Region preference order.</param>
public sealed record CustomPolicySettings(
    string Name,
    IReadOnlyList<AxisRuleSettings> Rules,
    IReadOnlyList<string>? PreferredRegions = null);

/// <summary>One axis's persisted rule.</summary>
/// <param name="Axis">Axis name.</param>
/// <param name="Role">Identity, Variance, or Ignored.</param>
/// <param name="Selection">What to keep when the role is Variance.</param>
/// <param name="Values">Values kept under KeepMatching.</param>
public sealed record AxisRuleSettings(
    string Axis,
    string Role,
    string Selection,
    IReadOnlyList<string>? Values = null);
