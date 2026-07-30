using ARK.Core.Systems;

namespace ARK.Core.Dat;

/// <summary>The outcome of an import run across one or more DATs.</summary>
public sealed record DatImportSummary(int DatsImported, int EntriesIndexed, IReadOnlyList<DatImportResult> Results);

/// <summary>
/// Imports user-supplied DATs into the <see cref="DatCatalog"/>: a single <c>.dat</c>, an archive of
/// DATs (Datomatic Daily pack), or a directory of DATs. The primary path for No-Intro, which is
/// import-only. Each DAT's system is inferred from its header name via the <see cref="SystemRegistry"/>;
/// import is idempotent because the catalog keys on the DAT header name.
/// </summary>
public sealed class DatImporter
{
    private readonly DatCatalog _catalog;
    private readonly SystemRegistry _systems;

    /// <summary>Creates an importer over the given catalog and system registry.</summary>
    public DatImporter(DatCatalog catalog, SystemRegistry systems)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(systems);
        _catalog = catalog;
        _systems = systems;
    }

    /// <summary>Imports from a file (single DAT or DAT archive) or a directory of DATs.</summary>
    public DatImportSummary Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var files = ResolveFiles(path);
        var results = new List<DatImportResult>();

        foreach (var file in files)
        {
            using var stream = File.OpenRead(file);
            foreach (var (entryName, dat) in DatReader.ReadDats(stream))
            {
                var origin = entryName is null ? file : $"{file}::{entryName}";
                results.Add(_catalog.Import(dat, InferSystem(dat), origin));
            }
        }

        return new DatImportSummary(results.Count, results.Sum(result => result.EntryCount), results);
    }

    private static IReadOnlyList<string> ResolveFiles(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory
                .EnumerateFiles(path, "*.dat", SearchOption.AllDirectories)
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToList();
        }

        if (File.Exists(path))
        {
            return [path];
        }

        throw new FileNotFoundException($"Import path not found: {path}", path);
    }

    private string? InferSystem(LogiqxDat dat) =>
        _systems.ResolveByAlias(dat.Header.Name)?.Code
        ?? _systems.ResolveByAlias(dat.Header.Description)?.Code;
}
