using System.CommandLine;
using ARK.Cli.Commands;
using ARK.Core.Dat;
using ARK.Core.Diagnostics;
using ARK.Core.Execution;
using ARK.Core.Instances;
using ARK.Core.Settings;
using ARK.Core.Systems;
using ARK.Core.Tools;
using Serilog;
using Spectre.Console;

// Composition root. Serilog is wired here from the first commit so every command inherits it.
// Core composition (services, resolvers) lives here; command files stay presentation-only.
var paths = new InstancePaths();
Log.Logger = ArkLog.Create(paths);
var settingsStore = new SettingsStore(paths);
var executor = new Executor(paths);
var systems = SystemRegistry.Load(paths.SystemsDirectory);
var catalog = new DatCatalog(paths);
var importer = new DatImporter(catalog, systems);
using var httpClient = new HttpClient();
var syncService = new DatSyncService(catalog, httpClient);

try
{
    var root = new RootCommand(
        "ARK Retro Forge — universal ROM management: identify, verify, dedupe, curate, rename, and organize.");
    root.Add(MedicalBayCommand.Build(AnsiConsole.Console, BuildMedicalBayReport));
    root.Add(ConfigCommand.Build(AnsiConsole.Console, settingsStore, executor));
    root.Add(DatCommand.Build(AnsiConsole.Console, catalog, importer, syncService, LoadManifestSources(), EnsureProvisioned));

    // Handle exceptions here (see catch below) rather than letting System.CommandLine dump a raw
    // stack trace for a user-fixable condition like a malformed settings file.
    var invocation = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
    return await root.Parse(args).InvokeAsync(invocation);
}
catch (SettingsFormatException ex)
{
    // Fail loud, but cleanly: a malformed settings file is a user-fixable error, not a crash.
    AnsiConsole.MarkupLineInterpolated($"[red]Settings error:[/] {ex.Message}");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

MedicalBayReport BuildMedicalBayReport()
{
    var settings = settingsStore.Read();
    var tools = new ToolManager(new ToolLocator(paths.ToolsRoot));
    return new MedicalBayService(paths, tools, catalog, systems)
        .Generate(new MedicalBayContext(settings.RomRoot, settings.ActiveSystem));
}

IReadOnlyList<DatSourceDefinition> LoadManifestSources()
{
    try
    {
        return DatSourceManifest.Load(paths.DatSourcesManifestPath).Sources;
    }
    catch (FileNotFoundException)
    {
        return [];
    }
}

// The DAT catalog lives under the instance's db/ directory, which only the Executor may create.
// Provision the instance tree (idempotently) the first time a catalog write is needed.
void EnsureProvisioned()
{
    if (!Directory.Exists(paths.Db))
    {
        executor.Execute(paths.BuildProvisionPlan(), apply: true);
    }
}
