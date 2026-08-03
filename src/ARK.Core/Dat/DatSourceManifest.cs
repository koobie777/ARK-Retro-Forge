using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ARK.Core.Serialization;

namespace ARK.Core.Dat;

/// <summary>
/// The set of DAT source definitions parsed from <c>dat-sources.json</c>. This is a thin reader of
/// the source manifest, not the Phase 2 download/index/cache machinery; it is ported from v1 to
/// support the Medical Bay DAT-status diagnostic.
/// </summary>
public sealed class DatSourceManifest
{
    private readonly List<DatSourceDefinition> _sources;

    private DatSourceManifest(List<DatSourceDefinition> sources)
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

    /// <summary>Loads and parses the manifest at <paramref name="manifestPath"/>.</summary>
    [RequiresUnreferencedCode("Manifest deserialization uses reflection-based System.Text.Json.")]
    public static DatSourceManifest Load(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("DAT source manifest not found", manifestPath);
        }

        using var stream = File.OpenRead(manifestPath);
        var sources = JsonSerializer.Deserialize<List<DatSourceDefinition>>(stream, ArkJson.Read) ?? [];

        return new DatSourceManifest(sources);
    }
}
