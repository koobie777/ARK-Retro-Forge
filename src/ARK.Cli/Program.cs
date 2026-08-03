using System.CommandLine;
using ARK.Cli.Commands;
using ARK.Core.Configuration;
using ARK.Core.Dat;
using ARK.Core.Diagnostics;
using ARK.Core.Execution;
using ARK.Core.Hashing;
using ARK.Core.Instances;
using ARK.Core.Naming;
using ARK.Core.Scanning;
using ARK.Core.Settings;
using ARK.Core.Systems;
using ARK.Core.Tools;
using ARK.Core.Units;
using ARK.Core.Verification;
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

var vocabulary = NamingVocabularyLoader.Load(paths.NamingDirectory);
var tokenizer = new NameTokenizer(vocabulary);
var formatter = new NameFormatter(tokenizer);

var scanRules = ScanRulesLoader.Load(paths.ScanRulesPath);
var fileSystem = new FileSystemReader();
var archiveExtensions = systems.All.SelectMany(system => system.ArchiveExtensions).Distinct(StringComparer.OrdinalIgnoreCase);
var resolvers = new IGameUnitResolver[]
{
    new CartridgeUnitResolver(tokenizer, new ArchiveInspector(fileSystem, archiveExtensions), scanRules.DiscDescriptorExtensions),
    new DiscUnitResolver(),
};
var scanService = new ScanService(
    fileSystem,
    new DirectoryProfiler(tokenizer, scanRules, systems.All.SelectMany(system => system.FormatQualifiers)),
    resolvers,
    scanRules);

var archiveInspector = new ArchiveInspector(fileSystem, archiveExtensions);
var hashCache = new HashCache(paths);
var verificationService = new VerificationService(
    new RomHasher(fileSystem, archiveInspector),
    hashCache,
    new InProgressDetector(
        scanRules.IncompleteDownloadExtensions,
        settingsStore.Read().IncompleteDownloadDirectories));

try
{
    var root = new RootCommand(
        "ARK Retro Forge — universal ROM management: identify, verify, dedupe, curate, rename, and organize.");
    root.Add(MedicalBayCommand.Build(AnsiConsole.Console, BuildMedicalBayReport));
    root.Add(ConfigCommand.Build(AnsiConsole.Console, settingsStore, executor));
    root.Add(DatCommand.Build(AnsiConsole.Console, catalog, importer, syncService, LoadManifestSources(), EnsureProvisioned));
    root.Add(ParseCommand.Build(AnsiConsole.Console, tokenizer, formatter, vocabulary));
    root.Add(ScanCommand.Build(AnsiConsole.Console, ScanRoot));
    root.Add(VerifyCommand.Build(AnsiConsole.Console, VerifyRoot));

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

// Identification is scoped per ROM-set directory to the one DAT that directory resolves to, and
// each DAT's index is built on first use. A directory that resolves to no DAT identifies nothing,
// which is the honest answer rather than a catalog-wide search that finds the wrong release.
ScanReport ScanRoot(string root) =>
    scanService.Scan(root, new DatScopeResolver(catalog, systems, tokenizer, vocabulary));

// Verification scans first — it needs the units and the per-directory DAT scope — then hashes.
// The cache is committed as it goes, so a cancelled run keeps everything it computed.
VerificationReport VerifyRoot(string root, IProgress<VerificationProgress>? progress, CancellationToken cancellationToken)
{
    try
    {
        return verificationService.Verify(ScanRoot(root), progress, cancellationToken);
    }
    finally
    {
        hashCache.Close();
    }
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
