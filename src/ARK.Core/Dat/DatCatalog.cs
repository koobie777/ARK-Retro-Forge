using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ARK.Core.Dat;

/// <summary>
/// The set of DAT source definitions parsed from <c>dat-sources.json</c>. This is a thin reader of
/// the source manifest, not the Phase 2 download/index/cache machinery; it is ported from v1 to
/// support the Medical Bay DAT-status diagnostic.
/// </summary>
public sealed class DatCatalog
{
    private readonly List<DatSourceDefinition> _sources;

    private DatCatalog(List<DatSourceDefinition> sources)
    {
        _sources = sources;
    }

    /// <summary>All source definitions in the manifest.</summary>
    public IReadOnlyList<DatSourceDefinition> Sources => _sources;

    /// <summary>Source definitions for a single system, or all sources when no system is given.</summary>
    public IReadOnlyList<DatSourceDefinition> FilterBySystem(string? system)
    {
        if (string.IsNullOrWhiteSpace(system))
        {
            return _sources;
        }

        return _sources
            .Where(source => source.System.Equals(system, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Loads and parses the manifest at <paramref name="catalogPath"/>.</summary>
    [RequiresUnreferencedCode("Catalog deserialization uses reflection-based System.Text.Json.")]
    public static DatCatalog Load(string catalogPath)
    {
        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException("DAT catalog not found", catalogPath);
        }

        using var stream = File.OpenRead(catalogPath);
        var sources = JsonSerializer.Deserialize<List<DatSourceDefinition>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        return new DatCatalog(sources);
    }
}
