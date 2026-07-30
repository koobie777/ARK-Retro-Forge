using System.Diagnostics.CodeAnalysis;
using ARK.Core.Dat;
using ARK.Core.Instances;

namespace ARK.Core.Diagnostics;

/// <summary>
/// Reports local DAT status per system: which systems the manifest defines and how many DAT files
/// are present for each in the current instance. Read-only and best-effort; all paths come from the
/// resolver, never composed here.
/// </summary>
public sealed class DatStatusReporter
{
    private readonly InstancePaths _paths;

    /// <summary>Creates a reporter scoped to <paramref name="paths"/>' instance.</summary>
    public DatStatusReporter(InstancePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    /// <summary>
    /// Inspects local DAT presence per system. Returns an empty list when the source manifest is
    /// missing or unreadable — a missing manifest is a status, not a failure.
    /// </summary>
    [RequiresUnreferencedCode("Loads the DAT catalog, which uses reflection-based System.Text.Json.")]
    public IReadOnlyList<DatStatus> Inspect(string? systemFilter = null)
    {
        DatCatalog catalog;
        try
        {
            catalog = DatCatalog.Load(_paths.DatSourcesManifestPath);
        }
        catch (FileNotFoundException)
        {
            return [];
        }

        var grouped = catalog.Sources
            .Where(source => string.IsNullOrWhiteSpace(systemFilter) || source.System.Equals(systemFilter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(source => source.System, StringComparer.OrdinalIgnoreCase);

        var summaries = new List<DatStatus>();
        foreach (var group in grouped)
        {
            var datRoot = _paths.DatDirectoryFor(group.Key);
            var files = Directory.Exists(datRoot)
                ? Directory.EnumerateFiles(datRoot, "*.dat", SearchOption.AllDirectories).ToList()
                : [];

            DateTime? lastUpdated = null;
            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (!lastUpdated.HasValue || info.LastWriteTimeUtc > lastUpdated.Value)
                    {
                        lastUpdated = info.LastWriteTimeUtc;
                    }
                }
                catch (IOException)
                {
                    // Best-effort: skip files we cannot stat.
                }
            }

            summaries.Add(new DatStatus
            {
                System = group.Key,
                LocalFileCount = files.Count,
                LastUpdatedUtc = lastUpdated
            });
        }

        return summaries
            .OrderBy(summary => summary.System, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
