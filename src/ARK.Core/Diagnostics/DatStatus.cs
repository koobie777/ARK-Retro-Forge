namespace ARK.Core.Diagnostics;

/// <summary>
/// Per-system DAT status for the Medical Bay report: how many local DAT files are present for a
/// system and when they were last updated, with derived catalog/staleness state. Reports presence,
/// not contents — it does not parse DAT file bodies.
/// </summary>
public sealed record DatStatus
{
    private const int StaleThresholdDays = 7;

    /// <summary>System code the summary is for.</summary>
    public required string System { get; init; }

    /// <summary>Number of local <c>.dat</c> files found for the system in this instance.</summary>
    public required int LocalFileCount { get; init; }

    /// <summary>Most recent write time across the local DAT files, or null when there are none.</summary>
    public required DateTime? LastUpdatedUtc { get; init; }

    /// <summary>True when at least one local DAT file is present.</summary>
    public bool HasCatalog => LocalFileCount > 0;

    /// <summary>True when the newest local DAT file is older than the staleness threshold.</summary>
    public bool IsStale => LastUpdatedUtc is { } timestamp && timestamp < DateTime.UtcNow.AddDays(-StaleThresholdDays);
}
