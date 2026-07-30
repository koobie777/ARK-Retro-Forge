using System.CommandLine;
using ARK.Cli.Rendering;
using ARK.Core.Dat;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// Wires the <c>dat</c> verb (<c>import</c>, <c>sync</c>, <c>list</c>). Presentation and wiring only:
/// parsing, indexing, syncing, and coverage all live in Core. A provisioning callback is invoked
/// before catalog writes so the instance's <c>db/</c> directory exists (created via the Executor,
/// never here).
/// </summary>
public static class DatCommand
{
    /// <summary>Builds the command tree.</summary>
    public static Command Build(
        IAnsiConsole console,
        DatCatalog catalog,
        DatImporter importer,
        DatSyncService sync,
        IReadOnlyList<DatSourceDefinition> sources,
        Action ensureProvisioned)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(ensureProvisioned);

        var dat = new Command("dat", "Import, sync, and inspect DAT catalogs.");
        dat.Add(BuildImport(console, importer, ensureProvisioned));
        dat.Add(BuildSync(console, sync, sources, ensureProvisioned));
        dat.Add(BuildList(console, catalog));
        return dat;
    }

    private static Command BuildImport(IAnsiConsole console, DatImporter importer, Action ensureProvisioned)
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "A .dat file, an archive of DATs (Daily pack), or a directory of DATs."
        };
        var command = new Command("import", "Import DATs from a file, archive, or directory.");
        command.Add(pathArgument);
        command.SetAction(parseResult =>
        {
            ensureProvisioned();
            DatRenderer.RenderImport(console, importer.Import(parseResult.GetValue(pathArgument)!));
            return 0;
        });
        return command;
    }

    private static Command BuildSync(
        IAnsiConsole console,
        DatSyncService sync,
        IReadOnlyList<DatSourceDefinition> sources,
        Action ensureProvisioned)
    {
        var systemOption = new Option<string>("--system") { Description = "Limit to sources for one system code." };
        var forceOption = new Option<bool>("--force") { Description = "Refetch even if already cached." };
        var command = new Command("sync", "Fetch DAT sources that expose a direct URL (e.g. Redump).");
        command.Add(systemOption);
        command.Add(forceOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ensureProvisioned();
            var system = parseResult.GetValue(systemOption);
            var selected = string.IsNullOrWhiteSpace(system)
                ? sources
                : sources.Where(source => source.System.Equals(system, StringComparison.OrdinalIgnoreCase)).ToList();

            var summary = await sync.SyncAsync(selected, parseResult.GetValue(forceOption), cancellationToken);
            DatRenderer.RenderSync(console, summary);
            return 0;
        });
        return command;
    }

    private static Command BuildList(IAnsiConsole console, DatCatalog catalog)
    {
        var command = new Command("list", "Show catalog coverage per system.");
        command.SetAction(_ =>
        {
            DatRenderer.RenderCoverage(console, catalog.Coverage());
            return 0;
        });
        return command;
    }
}
