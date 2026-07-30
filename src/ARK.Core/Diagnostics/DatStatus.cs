using System.Globalization;

namespace ARK.Core.Diagnostics;

/// <summary>
/// Real per-DAT catalog coverage for the Medical Bay report: which system a DAT covers, how many
/// entries it indexed, its version/date, and whether that date is old enough to be worth refreshing.
/// Backed by the SQLite catalog, not by counting files on disk.
/// </summary>
public sealed record DatStatus
{
    private const int StaleThresholdDays = 90;

    /// <summary>System code the DAT covers, or a marker when the DAT's system was not recognized.</summary>
    public required string System { get; init; }

    /// <summary>The DAT's header name.</summary>
    public required string DatName { get; init; }

    /// <summary>Number of ROM entries indexed from the DAT.</summary>
    public required int EntryCount { get; init; }

    /// <summary>DAT version string from the header, if any.</summary>
    public required string? Version { get; init; }

    /// <summary>DAT publication date from the header, if any.</summary>
    public required string? Date { get; init; }

    /// <summary>True when <see cref="Date"/> parses and is older than the staleness threshold.</summary>
    public bool IsStale =>
        DateTime.TryParse(Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
        && date < DateTime.UtcNow.AddDays(-StaleThresholdDays);
}
