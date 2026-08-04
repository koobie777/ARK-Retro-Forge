using System.CommandLine;
using ARK.Cli.Commands;
using ARK.Core.Configuration;
using ARK.Core.Dat;
using ARK.Core.Dedup;
using ARK.Core.Diagnostics;
using ARK.Core.Execution;
using ARK.Core.Hashing;
using ARK.Core.Instances;
using ARK.Core.Naming;
using ARK.Core.Policy;
using ARK.Core.Renaming;
using ARK.Core.Reporting;
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
var journals = new JournalStore(paths);
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
var archiveInspector = new ArchiveInspector(fileSystem, archiveExtensions);

// Disc first. The cartridge resolver claims every file it is offered, so anything reaching it is
// what the disc resolver declined — which is the correct order, because a disc has positive
// evidence (a cue sheet, an ISO) and a cartridge is the residue.
var resolvers = new IGameUnitResolver[]
{
    new DiscUnitResolver(tokenizer, archiveInspector, fileSystem),
    new CartridgeUnitResolver(tokenizer, archiveInspector, scanRules.DiscDescriptorExtensions),
};
var scanService = new ScanService(
    fileSystem,
    new DirectoryProfiler(tokenizer, scanRules, systems.All.SelectMany(system => system.FormatQualifiers)),
    resolvers,
    scanRules);

var hashCache = new HashCache(paths);
var verificationService = new VerificationService(
    new RomHasher(fileSystem, archiveInspector),
    hashCache,
    new InProgressDetector(
        scanRules.IncompleteDownloadExtensions,
        settingsStore.Read().IncompleteDownloadDirectories,
        TimeSpan.FromMinutes(scanRules.RecentWriteWindowMinutes)),
    vocabulary);

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
    root.Add(DedupeCommand.Build(AnsiConsole.Console, AnalyzeDuplicates, QuarantineDuplicates));
    root.Add(CurateCommand.Build(
        AnsiConsole.Console, AnalyzeVariants, QuarantineVariants, SortVariants,
        PolicySettings.Resolve(settingsStore.Read())));
    root.Add(ReportCommand.Build(
        AnsiConsole.Console, BuildCollectionReport, PolicySettings.Resolve(settingsStore.Read())));
    root.Add(RenameCommand.BuildRename(AnsiConsole.Console, DecideRenames, ApplyRenames));
    root.Add(RenameCommand.BuildOrganize(AnsiConsole.Console, OrganizeRoot));
    root.Add(UndoCommand.BuildJournal(AnsiConsole.Console, journals));
    root.Add(UndoCommand.BuildUndo(AnsiConsole.Console, new UndoService(journals, executor)));

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

// Dedup rides on the same scan and verification the other verbs use: units come from the scan,
// eligibility from the verification state, and hashes from the shared cache.
DedupReport AnalyzeDuplicates(string root, KeepPolicy policy)
{
    try
    {
        var scan = ScanRoot(root);
        var verification = verificationService.Verify(scan);
        return new DedupService(new RomHasher(fileSystem, archiveInspector), hashCache, vocabulary)
            .Analyze(scan, verification, policy);
    }
    finally
    {
        hashCache.Close();
    }
}

// DRY-RUN builds the plan and stops. Only --apply hands it to the Executor, which journals every
// move as it completes so `ark undo` can put the set back exactly.
(QuarantinePlan Plan, ExecutionResult? Result) QuarantineDuplicates(DedupReport report, bool apply)
{
    var sessionId = $"dedup-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
    var active = report.ExcludedFor(DedupExclusion.InProgress)
        .Select(entry => Path.GetDirectoryName(entry.Candidate.Path))
        .Where(directory => directory is not null)
        .Distinct(StringComparer.OrdinalIgnoreCase)!;

    var plan = QuarantinePlanner.Build(report, sessionId, DateTimeOffset.UtcNow, active!, report.Sets);

    return plan.Plan.Actions.Count == 0 || !apply
        ? (plan, null)
        : (plan, executor.Execute(plan.Plan, apply: true));
}

// Curation reuses the same scan and verification: units from the scan, eligibility from the
// verification state. Unlike dedup it needs no hashing — every decision comes from the name.
CurationReport AnalyzeVariants(string root, VariantPolicy policy)
{
    try
    {
        var scan = ScanRoot(root);
        return new CurationService(tokenizer, vocabulary)
            .Analyze(scan, verificationService.Verify(scan), policy);
    }
    finally
    {
        hashCache.Close();
    }
}

// Removal. Full Phase 7 machinery — manifest, journal, same volume, undo.
(QuarantinePlan Plan, ExecutionResult? Result) QuarantineVariants(CurationReport report, bool apply)
{
    var sessionId = $"curate-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
    var setKeys = QuarantinePlanner.SetKeysByPath(report.Sets);
    var requests = report.Removable
        .Select(member => new QuarantineRequest(
            member.Path, member.FileName, null, null, member.Unit.TotalSize,
            KeptPathFor(report, member), $"Variant removed by policy '{report.Policy.Name}'",
            member.Unit.Files.Select(file => file.FullPath).ToArray(),
            setKeys.GetValueOrDefault(member.Path)))
        .ToArray();

    var plan = QuarantinePlanner.Build(
        report.Root, requests, sessionId, DateTimeOffset.UtcNow, report.Policy.Name,
        ActiveDirectories(report), report.Sets);

    return plan.Plan.Actions.Count == 0 || !apply
        ? (plan, null)
        : (plan, executor.Execute(plan.Plan, apply: true));
}

// Organization. Files move, nothing leaves the collection.
(SortPlan Plan, ExecutionResult? Result) SortVariants(CurationReport report, string? subfolder, bool apply)
{
    var sessionId = $"curate-sort-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
    var plan = SortPlanner.Build(report, sessionId, DateTimeOffset.UtcNow, subfolder, ActiveDirectories(report));

    return plan.Plan.Actions.Count == 0 || !apply
        ? (plan, null)
        : (plan, executor.Execute(plan.Plan, apply: true));
}

string KeptPathFor(CurationReport report, CurationCandidate member) =>
    report.Resolved.FirstOrDefault(group => group.Remove.Contains(member))?.Keep.FirstOrDefault()?.Path
    ?? string.Empty;

IEnumerable<string> ActiveDirectories(CurationReport report) => report
    .ExcludedFor(CurationExclusion.InProgress)
    .Select(candidate => Path.GetDirectoryName(candidate.Path))
    .Where(directory => directory is not null)
    .Distinct(StringComparer.OrdinalIgnoreCase)!;

// The join the pipeline was built to produce. Nothing new is computed: the catalog says what
// exists, the scan what you have, verification whether it is correct, and the policy what you want.
// Only the DATs the scanned directories actually resolved to are tokenized — running the policy
// over all 1.5M catalog entries would be both slow and meaningless.
CollectionReport BuildCollectionReport(string root, VariantPolicy policy)
{
    try
    {
        var scan = ScanRoot(root);
        var verification = verificationService.Verify(scan);

        var scoped = scan.RomSetDirectories
            .Select(directory => directory.Scope?.DatName)
            .Where(name => name is { Length: > 0 })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(name => catalog.EntriesForDat(name!))
            .ToArray();

        var cache = new CatalogNameCache(tokenizer);
        var target = new TargetSetBuilder(cache, vocabulary).Build(scoped, policy);

        return new CollectionReportService(vocabulary).Build(scan, verification, target, cache);
    }
    finally
    {
        hashCache.Close();
    }
}

// Renaming needs the scan (for the DAT match) and verification (for the state that authorizes it).
// A unit is canonicalized only when its hash confirmed the entry whose name it will take.
RenameReport DecideRenames(string root, RenameMode mode)
{
    try
    {
        var scan = ScanRoot(root);
        return new RenameService(formatter).Decide(scan, verificationService.Verify(scan), mode);
    }
    finally
    {
        hashCache.Close();
    }
}

(RenamePlan Plan, ExecutionResult? Result) ApplyRenames(RenameReport report, bool apply)
{
    var sessionId = $"rename-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
    var plan = RenamePlanner.Build(report, sessionId, DateTimeOffset.UtcNow, ActiveRenameDirectories(report));

    return plan.Plan.Actions.Count == 0 || !apply
        ? (plan, null)
        : (plan, executor.Execute(plan.Plan, apply: true));
}

(OrganizePlan Plan, ExecutionResult? Result) OrganizeRoot(string root, bool apply)
{
    OrganizePlan plan;
    try
    {
        var scan = ScanRoot(root);
        var verification = verificationService.Verify(scan);
        plan = OrganizePlanner.Build(
            scan, verification, $"organize-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}", DateTimeOffset.UtcNow,
            ActiveDirectoriesFrom(verification));
    }
    finally
    {
        hashCache.Close();
    }

    return plan.Plan.Actions.Count == 0 || !apply
        ? (plan, null)
        : (plan, executor.Execute(plan.Plan, apply: true));
}

// In-flight units are already refused by RenameService before they reach a plan, in either mode.
// This passes their directories through as well, so a unit that merely sits beside one is caught
// even when its own timestamp looks settled.
IEnumerable<string> ActiveRenameDirectories(RenameReport report) => report
    .RefusedFor(RenameRefusal.ActiveDownload)
    .Select(decision => Path.GetDirectoryName(decision.Path))
    .Where(directory => directory is not null)
    .Distinct(StringComparer.OrdinalIgnoreCase)!;

IEnumerable<string> ActiveDirectoriesFrom(VerificationReport verification) => verification
    .InState(VerificationState.InProgress)
    .Select(unit => Path.GetDirectoryName(unit.Path))
    .Where(directory => directory is not null)
    .Distinct(StringComparer.OrdinalIgnoreCase)!;

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
