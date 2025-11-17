using ARK.Core.Dat;

namespace ARK.Cli.Infrastructure;

internal sealed record DatStatusSummary
{
    public required string System { get; init; }
    public required int SourceCount { get; init; }
    public required int LocalFileCount { get; init; }
    public required long TotalBytes { get; init; }
    public required DateTime? LastUpdatedUtc { get; init; }

    private const int StaleThresholdDays = 7;

    public bool HasCatalog => LocalFileCount > 0;
    public bool IsStale => LastUpdatedUtc is { } timestamp && timestamp < DateTime.UtcNow.AddDays(-StaleThresholdDays);
    public string Status => !HasCatalog ? "Missing" : IsStale ? "Stale" : "Ready";
}

internal static class DatStatusReporter
{
    private static readonly string CatalogPath = Path.Combine(AppContext.BaseDirectory, "config", "dat", "dat-sources.json");

    public static IReadOnlyList<DatStatusSummary> Inspect(string? systemFilter = null)
    {
        DatCatalog catalog;
        try
        {
            catalog = DatCatalog.Load(CatalogPath);
        }
        catch
        {
            return Array.Empty<DatStatusSummary>();
        }

        var grouped = catalog.Sources
            .Where(s => string.IsNullOrWhiteSpace(systemFilter) || s.System.Equals(systemFilter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.System, StringComparer.OrdinalIgnoreCase);

        var instanceRoot = InstancePathResolver.GetInstanceRoot();
        var summaries = new List<DatStatusSummary>();

        foreach (var group in grouped)
        {
            var datRoot = Path.Combine(instanceRoot, "dat", group.Key);
            var files = Directory.Exists(datRoot)
                ? Directory.EnumerateFiles(datRoot, "*.dat", SearchOption.AllDirectories).ToList()
                : new List<string>();

            DateTime? lastUpdated = null;
            long totalBytes = 0;
            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    totalBytes += info.Length;
                    if (!lastUpdated.HasValue || info.LastWriteTimeUtc > lastUpdated.Value)
                    {
                        lastUpdated = info.LastWriteTimeUtc;
                    }
                }
                catch
                {
                    // Ignore IO failures; best-effort reporting.
                }
            }

            summaries.Add(new DatStatusSummary
            {
                System = group.Key,
                SourceCount = group.Count(),
                LocalFileCount = files.Count,
                TotalBytes = totalBytes,
                LastUpdatedUtc = lastUpdated
            });
        }

        return summaries
            .OrderBy(summary => summary.System, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
