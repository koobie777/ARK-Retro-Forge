using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ARK.Core.Dat;

public sealed class DatCatalog
{
    private readonly List<DatSourceDefinition> _sources;

    private DatCatalog(List<DatSourceDefinition> sources)
    {
        _sources = sources;
    }

    public IReadOnlyList<DatSourceDefinition> Sources => _sources;

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

    [RequiresUnreferencedCode("System.Text.Json source generation not configured for catalog deserialization when trimming.")]
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
        }) ?? new List<DatSourceDefinition>();

        return new DatCatalog(sources);
    }
}
