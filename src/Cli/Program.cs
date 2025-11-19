using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ARK.Cli.Infrastructure;
using ARK.Cli.Commands.PSX;
using ARK.Cli.Commands.Archives;
using ARK.Cli.Commands.Dat;
using ARK.Core.Tools;
using ARK.Core.Hashing;
using ARK.Core.Database;
using ARK.Core.Systems.PSX;
using Spectre.Console;
using HeaderMetadata = ARK.Cli.Infrastructure.ConsoleDecorations.HeaderMetadata;

namespace ARK.Cli;

public class Program
{
    private static bool _menuDryRun = true;
    private static string? _rememberedRomRoot;
    private static SystemProfile _currentSystem = SystemProfiles.Default;
    private static bool _sessionPrimed;
    private static bool _preventSleep;
    private static readonly HashSet<string> ScanExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bin", ".cue", ".iso", ".chd", ".cso", ".pbp",
        ".z64", ".n64", ".v64",
        ".gb", ".gbc", ".gba",
        ".nes", ".smc", ".sfc",
        ".gcm", ".wbfs", ".rvz", ".wux",
        ".xci", ".nsp", ".nsz",
        ".gdi", ".cdi"
    };

    private static readonly HashSet<string> VerifyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bin", ".iso", ".chd", ".cso", ".pbp"
    };
    public static async Task<int> Main(string[] args)
    {
        CliLogger.Initialize();
        ParseGlobalArgs(ref args);
        TryApplyConsoleDefaults();
        PrintBanner();

        var session = SessionStateManager.State;
        _menuDryRun = session.MenuDryRun;
        _rememberedRomRoot = session.RomRoot;
        _currentSystem = SystemProfiles.Resolve(session.SystemCode);
        _preventSleep = session.PreventSleep;
        ApplySleepInhibition();
        CliLogger.LogInfo($"Session restored | Root: {_rememberedRomRoot ?? "<unset>"} | System: {_currentSystem.Code} | Mode: {(_menuDryRun ? "DRY" : "APPLY")}");

        if (args.Length == 0)
        {
            if (!HasInteractiveConsole())
            {
                Console.WriteLine("Interactive menu requires a terminal. Run `ark-retro-forge --help` for usage.");
                return (int)ExitCode.InvalidArgs;
            }

            var menuExit = await ShowMenuAsync();
            SessionStateManager.Save();
            return menuExit;
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "doctor" or "medical" or "medical-bay" => await RunWithOperationScope("medical bay", () => RunMedicalBayAsync(args), watchForEscape: true),
                "scan" => await RunWithOperationScope("scan", () => RunScanAsync(args), watchForEscape: true),
                "verify" => await RunWithOperationScope("verify", () => RunVerifyAsync(args), watchForEscape: true),
                "rename" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await RunWithOperationScope("rename psx", () => RenamePsxCommand.RunAsync(args), watchForEscape: HasFlag(args, "--apply")),
                "convert" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await RunWithOperationScope("convert psx", () => ConvertPsxCommand.RunAsync(args), watchForEscape: HasFlag(args, "--apply")),
                "merge" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await RunWithOperationScope("merge psx", () => MergePsxCommand.RunAsync(args), watchForEscape: HasFlag(args, "--apply")),
                "clean" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await RunWithOperationScope("clean psx", () => CleanPsxCommand.RunAsync(args), watchForEscape: true),
                "playlist" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await RunWithOperationScope("playlist psx", () => PlaylistPsxCommand.RunAsync(args), watchForEscape: HasFlag(args, "--apply")),
                "cue" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await RunWithOperationScope("cue psx", () => CuePsxCommand.RunAsync(args), watchForEscape: HasFlag(args, "--apply")),
                "duplicates" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await RunWithOperationScope("duplicates psx", () => DuplicatesPsxCommand.RunAsync(args)),
                "dupes" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await RunWithOperationScope("duplicates psx", () => DuplicatesPsxCommand.RunAsync(args)),
                "extract" when args.Length > 1 && args[1].Equals("archives", StringComparison.OrdinalIgnoreCase) => await RunWithOperationScope("extract archives", () => ExtractArchivesCommand.RunAsync(args), watchForEscape: HasFlag(args, "--apply")),
                "dat" when args.Length > 1 && args[1].Equals("sync", StringComparison.OrdinalIgnoreCase) => await RunWithOperationScope("dat sync", () => DatSyncCommand.RunAsync(args)),
                "--help" or "-h" or "help" => ShowHelp(),
                "--version" or "-v" => ShowVersion(),
                _ => ShowUnknownCommand(command)
            };
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nOperation cancelled by user.");
            return (int)ExitCode.UserCancelled;
        }
        catch (Exception ex)
        {
            CliLogger.LogError($"Unhandled exception while running '{command}'", ex);
            var context = Markup.Escape(ex.Message);
            var component = Markup.Escape(command);
            AnsiConsole.MarkupLine($"\n[red][[IMPACT]] | Component: {component} | Context: {context}[/]");
            return (int)ExitCode.GeneralError;
        }
    }

    [RequiresUnreferencedCode("Medical Bay serializes tool results to JSON when requested.")]
    private static async Task<int> RunMedicalBayAsync(string[] args)
    {
        var json = args.Contains("--json");
        var toolManager = new ToolManager();
        var results = toolManager.CheckAllTools().ToList();

        if (json)
        {
            var jsonOutput = System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            Console.WriteLine(jsonOutput);
            return (int)ExitCode.OK;
        }

        var session = SessionStateManager.State;
        var systemProfile = SystemProfiles.Resolve(session.SystemCode);
        var datSummaries = DatStatusReporter.Inspect().ToList();

        var romRootMissing = string.IsNullOrWhiteSpace(session.RomRoot);
        var romRootValue = romRootMissing ? "[red]Not set[/]" : session.RomRoot!;

        ConsoleDecorations.RenderOperationHeader(
            "Medical Bay",
            new HeaderMetadata("Instance", InstancePathResolver.CurrentInstance),
            new HeaderMetadata("ROM Root", romRootValue, IsMarkup: romRootMissing),
            new HeaderMetadata("System", $"{systemProfile.Name} ({systemProfile.Code.ToUpperInvariant()})"),
            new HeaderMetadata(
                "DAT Catalogs",
                BuildDatHeaderSummary(datSummaries),
                IsMarkup: true));

        RenderToolTable(results);
        RenderDatSnapshot(datSummaries);

        var missingRequired = results.Where(r => !r.IsFound && !r.IsOptional).ToList();
        if (missingRequired.Count > 0)
        {
            var bulletList = string.Join("\n", missingRequired.Select(r => $"[red]- {r.Name}[/]: {(r.ErrorMessage ?? "Place executable in .\\tools").EscapeMarkup()}"));
            var panel = new Panel(new Markup(bulletList))
            {
                Header = new PanelHeader("Action Items"),
                Border = BoxBorder.Rounded,
                BorderStyle = Style.Parse("red")
            };
            AnsiConsole.Write(panel);
            AnsiConsole.MarkupLine("\n[bold yellow]Next step:[/] Download the missing tools and drop them into .\\tools then re-run Medical Bay.");
            return (int)ExitCode.ToolMissing;
        }

        var interactive = AnsiConsole.Profile.Capabilities.Interactive;
        if (interactive && datSummaries.Count > 0)
        {
            var suggestConsole = datSummaries.Any(s => !s.HasCatalog || s.IsStale);
            if (PromptYesNo("Open the DAT console for catalog actions?", suggestConsole))
            {
                if (await ShowDatConsoleAsync(datSummaries))
                {
                    datSummaries = DatStatusReporter.Inspect().ToList();
                    RenderDatSnapshot(datSummaries);
                }
            }
        }

        AnsiConsole.MarkupLine("\n[green]All required tools detected. Optional tools can be added later.[/]");
        return (int)ExitCode.OK;
    }

    private static void RenderToolTable(IEnumerable<ToolCheckResult> results)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Tooling Loadout[/]");
        table.AddColumn("Tool");
        table.AddColumn("Status");
        table.AddColumn("Version");
        table.AddColumn("Minimum");
        table.AddColumn("Location / Notes");

        foreach (var result in results)
        {
            var status = result.IsFound
                ? "[green]Ready[/]"
                : result.IsOptional
                    ? "[yellow]Optional missing[/]"
                    : "[red]Missing[/]";
            var notes = result.IsFound
                ? (result.Path ?? "Detected")
                : (result.ErrorMessage ?? "Place executable in .\\tools");

            table.AddRow(
                result.Name.EscapeMarkup(),
                status,
                (result.Version ?? "n/a").EscapeMarkup(),
                (result.MinimumVersion ?? "-").EscapeMarkup(),
                notes.EscapeMarkup());
        }

        AnsiConsole.Write(table);
    }

    private static void RenderDatSnapshot(IReadOnlyList<DatStatusSummary> summaries, bool showOverflowHint = true)
    {
        if (summaries.Count == 0)
        {
            AnsiConsole.MarkupLine("\n[yellow]DAT catalog offline.[/] Run `dat sync` to hydrate your instance cache.");
            return;
        }

        RenderDatSummaryPanel(summaries);

        var prioritized = summaries
            .OrderBy(s => GetDatStatusPriority(s))
            .ThenBy(s => s.System, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var snapshot = summaries
            .OrderByDescending(s => s.LocalFileCount)
            .ThenBy(s => GetDatStatusPriority(s))
            .ThenBy(s => s.System, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        RenderDatStatusTable(snapshot, "[bold]DAT Snapshot[/]");

        if (showOverflowHint && prioritized.Count > snapshot.Count)
        {
            AnsiConsole.MarkupLine($"[grey]Showing {snapshot.Count} of {prioritized.Count} systems. Open the DAT console for the full list.[/]");
        }
    }

    private static void RenderDatSummaryPanel(IReadOnlyList<DatStatusSummary> summaries)
    {
        var ready = summaries.Count(s => s.HasCatalog && !s.IsStale);
        var stale = summaries.Count(s => s.HasCatalog && s.IsStale);
        var missing = summaries.Count - ready - stale;
        var latest = summaries
            .Select(s => s.LastUpdatedUtc)
            .Where(d => d.HasValue)
            .OrderByDescending(d => d!.Value)
            .FirstOrDefault();

        var summaryTable = new Table().HideHeaders();
        summaryTable.AddColumn("Key");
        summaryTable.AddColumn("Value");
        summaryTable.AddRow("[green]Ready[/]", ready.ToString("N0"));
        summaryTable.AddRow("[yellow]Stale[/]", stale.ToString("N0"));
        summaryTable.AddRow("[red]Missing[/]", missing.ToString("N0"));
        summaryTable.AddRow("[dim]Latest sync[/]", FormatDatTimestamp(latest));

        var panel = new Panel(summaryTable)
        {
            Header = new PanelHeader("DAT Summary"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("silver")
        };

        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);
    }

    private static void RenderDatStatusTable(IEnumerable<DatStatusSummary> summaries, string? title = null, bool allowEmptyHint = false)
    {
        var rows = summaries.ToList();
        if (rows.Count == 0)
        {
            if (allowEmptyHint)
            {
                AnsiConsole.MarkupLine("[yellow]No DAT entries match the current filter.[/]");
            }
            return;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("System");
        table.AddColumn("Status");
        table.AddColumn("Sources");
        table.AddColumn("Files");
        table.AddColumn("Last Sync (UTC)");

        if (!string.IsNullOrWhiteSpace(title))
        {
            table.Title = new TableTitle(title);
        }

        foreach (var summary in rows)
        {
            table.AddRow(
                GetColoredSystem(summary),
                GetDatStatusMarkup(summary),
                summary.SourceCount.ToString("N0"),
                summary.LocalFileCount.ToString("N0"),
                FormatDatTimestamp(summary.LastUpdatedUtc));
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
    }

    private static string GetDatStatusMarkup(DatStatusSummary summary)
        => summary.Status switch
        {
            "Ready" => "[green]Ready[/]",
            "Stale" => "[yellow]Stale[/]",
            _ => "[red]Missing[/]"
        };

    private static string GetColoredSystem(DatStatusSummary summary)
    {
        var code = summary.System.ToUpperInvariant();
        return summary.Status switch
        {
            "Ready" => $"[green]{code}[/]",
            "Stale" => $"[yellow]{code}[/]",
            _ => $"[red]{code}[/]"
        };
    }

    private static int GetDatStatusPriority(DatStatusSummary summary)
    {
        if (!summary.HasCatalog)
        {
            return 0;
        }

        return summary.IsStale ? 1 : 2;
    }

    private static string FormatDatTimestamp(DateTime? timestamp)
        => timestamp?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "n/a";

    private static string BuildDatHeaderSummary(IReadOnlyList<DatStatusSummary> summaries)
    {
        if (summaries.Count == 0)
        {
            return "[yellow]Catalog offline[/]";
        }

        var ready = summaries.Count(s => s.HasCatalog && !s.IsStale);
        var stale = summaries.Count(s => s.HasCatalog && s.IsStale);
        var missing = summaries.Count - ready - stale;
        return $"[green]{ready} ready[/], [yellow]{stale} stale[/], [red]{missing} missing[/]";
    }

    private static async Task<bool> PromptDatSyncAsync(IReadOnlyList<DatStatusSummary> pool, bool preselectAll, string title, bool forceSelected = false)
    {
        if (pool.Count == 0)
        {
            return false;
        }

        var prompt = new MultiSelectionPrompt<DatStatusSummary>()
            .Title(title)
            .InstructionsText("[grey](SPACE = toggle, ENTER = sync, ESC = cancel)[/]")
            .MoreChoicesText("[grey](Type to filter system codes.)[/]")
            .NotRequired()
            .PageSize(Math.Clamp(pool.Count, 1, 15))
            .UseConverter(FormatDatChoice)
            .AddChoices(pool);

        if (preselectAll)
        {
            foreach (var item in pool)
            {
                prompt.Select(item);
            }
        }

        List<DatStatusSummary> selection;
        try
        {
            selection = PromptWithCancel(() => AnsiConsole.Prompt(prompt), "DAT sync selection", offerRetryPrompt: false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (selection.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No catalogs selected.[/]");
            return false;
        }

        await SyncDatSystemsAsync(selection, forceSelected);
        return true;
    }

    private static async Task<bool> ShowDatConsoleAsync(IReadOnlyList<DatStatusSummary> initialSummaries)
    {
        var datSummaries = initialSummaries.ToList();
        var updated = false;

        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]DAT Console[/]"));
            RenderDatSnapshot(datSummaries, showOverflowHint: false);

            string choice;
            try
            {
                choice = PromptWithCancel(
                    () => AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[bold cyan]Select a DAT action[/]")
                            .PageSize(8)
                            .AddChoices(
                                "View catalog",
                                "Sync missing/stale",
                                "Sync specific systems",
                                "Sync ready systems (force)",
                                $"Sync {_currentSystem.Code.ToUpperInvariant()}",
                                "Show DAT folder",
                                "Refresh",
                                "Back")),
                    "DAT console",
                    offerRetryPrompt: false);
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]DAT console exit requested.[/]");
                return updated;
            }

            switch (choice)
            {
                case "View catalog":
                    ShowDatCatalogBrowser(datSummaries);
                    break;
                case "Sync missing/stale":
                {
                    var targets = datSummaries.Where(s => !s.HasCatalog || s.IsStale).ToList();
                    if (targets.Count == 0)
                    {
                        AnsiConsole.MarkupLine("[green]All catalogs are already ready.[/]");
                        Pause();
                        break;
                    }

                    if (await PromptDatSyncAsync(targets, preselectAll: true, "[bold yellow]Sync missing / stale catalogs[/]\n[grey]Type to filter; SPACE toggles; ENTER to sync; ESC cancels.[/]"))
                    {
                        datSummaries = DatStatusReporter.Inspect().ToList();
                        updated = true;
                    }
                    break;
                }
                case "Sync specific systems":
                    if (await PromptDatSyncAsync(datSummaries, preselectAll: false, "[bold cyan]Select DAT catalogs to sync[/]\n[grey]Type to filter; SPACE toggles; ENTER to sync; ESC cancels.[/]"))
                    {
                        datSummaries = DatStatusReporter.Inspect().ToList();
                        updated = true;
                    }
                    break;
                case "Sync ready systems (force)":
                {
                    var readyTargets = datSummaries.Where(s => s.HasCatalog && !s.IsStale).ToList();
                    if (readyTargets.Count == 0)
                    {
                        AnsiConsole.MarkupLine("[yellow]No ready catalogs available to refresh.[/]");
                        Pause();
                        break;
                    }

                    if (await PromptDatSyncAsync(readyTargets, preselectAll: false, "[bold cyan]Force refresh ready catalogs[/]\n[grey]Type to filter; SPACE toggles; ENTER to sync; ESC cancels.[/]", forceSelected: true))
                    {
                        datSummaries = DatStatusReporter.Inspect().ToList();
                        updated = true;
                    }
                    break;
                }
                case string active when active.StartsWith("Sync ", StringComparison.OrdinalIgnoreCase):
                {
                    var currentSummary = datSummaries.FirstOrDefault(s => s.System.Equals(_currentSystem.Code, StringComparison.OrdinalIgnoreCase));
                    if (currentSummary == null)
                    {
                        AnsiConsole.MarkupLine($"[yellow]No DAT catalog entry found for {_currentSystem.Code.ToUpperInvariant()}.[/]");
                        Pause();
                        break;
                    }

                    bool force;
                    try
                    {
                        force = PromptYesNo("Force re-download even if ready?", currentSummary.IsStale);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    await SyncDatSystemsAsync(new[] { currentSummary }, force);
                    datSummaries = DatStatusReporter.Inspect().ToList();
                    updated = true;
                    break;
                }
                case "Show DAT folder":
                {
                    var datRoot = Path.Combine(InstancePathResolver.GetInstanceRoot(), "dat");
                    AnsiConsole.MarkupLine($"Instance DAT directory: [dim]{datRoot.EscapeMarkup()}[/]");
                    Pause();
                    break;
                }
                case "Refresh":
                    datSummaries = DatStatusReporter.Inspect().ToList();
                    break;
                case "Back":
                    AnsiConsole.Clear();
                    return updated;
            }
        }
    }

    private static void ShowDatCatalogBrowser(IReadOnlyList<DatStatusSummary> summaries)
    {
        if (summaries.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]DAT catalog offline.[/]");
            Pause();
            return;
        }

        var filter = DatStatusFilter.All;
        var search = string.Empty;

        while (true)
        {
            AnsiConsole.Clear();
            var filtered = summaries
                .Where(summary => filter switch
                {
                    DatStatusFilter.Ready => summary.HasCatalog && !summary.IsStale,
                    DatStatusFilter.Stale => summary.HasCatalog && summary.IsStale,
                    DatStatusFilter.Missing => !summary.HasCatalog,
                    _ => true
                })
                .Where(summary => string.IsNullOrWhiteSpace(search) || summary.System.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(summary => summary.System, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var title = $"[bold]DAT Catalog[/] ({filtered.Count}/{summaries.Count})";
            if (filter != DatStatusFilter.All)
            {
                title += $" [grey]| Filter: {filter}[/]";
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                title += $" [grey]| Search: {search.ToUpperInvariant()}[/]";
            }

            RenderDatStatusTable(filtered, title, allowEmptyHint: true);

            string action;
            try
            {
                action = PromptWithCancel(
                    () => AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[bold cyan]Catalog Browser[/]")
                            .PageSize(4)
                            .AddChoices("Filter status", "Search", "Reset filters", "Back")),
                    "DAT catalog browser",
                    offerRetryPrompt: false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            switch (action)
            {
                case "Filter status":
                {
                    string filterChoice;
                    try
                    {
                        filterChoice = PromptWithCancel(
                            () => AnsiConsole.Prompt(
                                new SelectionPrompt<string>()
                                    .Title("Show which status?")
                                    .AddChoices("All", "Ready", "Stale", "Missing")),
                            "Filter status",
                            offerRetryPrompt: false);
                    }
                    catch (OperationCanceledException)
                    {
                        continue;
                    }
                    filter = filterChoice switch
                    {
                        "Ready" => DatStatusFilter.Ready,
                        "Stale" => DatStatusFilter.Stale,
                        "Missing" => DatStatusFilter.Missing,
                        _ => DatStatusFilter.All
                    };
                    break;
                }
                case "Search":
                {
                    string searchInput;
                    try
                    {
                        searchInput = PromptWithCancel(
                            () => AnsiConsole.Prompt(
                                new TextPrompt<string>("Enter system code filter (leave blank to keep current):")
                                    .AllowEmpty()),
                            "Catalog search",
                            offerRetryPrompt: false);
                    }
                    catch (OperationCanceledException)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(searchInput))
                    {
                        search = searchInput.Trim();
                    }
                    break;
                }
                case "Reset filters":
                    filter = DatStatusFilter.All;
                    search = string.Empty;
                    break;
                case "Back":
                    AnsiConsole.Clear();
                    return;
            }
        }
    }

    private enum DatStatusFilter
    {
        All,
        Ready,
        Stale,
        Missing
    }

    private static string FormatDatChoice(DatStatusSummary summary)
    {
        var status = summary.Status;
        var lastSync = FormatDatTimestamp(summary.LastUpdatedUtc);
        return $"{summary.System.ToUpperInvariant()} · {status} · Files: {summary.LocalFileCount:N0} · Last: {lastSync}";
    }

    private static async Task SyncDatSystemsAsync(IEnumerable<DatStatusSummary> summaries, bool force = false)
    {
        foreach (var summary in summaries)
        {
            var syncArgs = new List<string> { "dat", "sync", "--system", summary.System };
            if (force || summary.IsStale)
            {
                syncArgs.Add("--force");
            }

            await DatSyncCommand.RunAsync(syncArgs.ToArray());
        }
    }
    internal static async Task<int> RunScanAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrWhiteSpace(root))
        {
            AnsiConsole.MarkupLine("[red][[IMPACT]] | Component: scan | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red][[IMPACT]] | Component: scan | Context: Directory not found: {root.EscapeMarkup()} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = HasFlag(args, "--recursive");

        ConsoleDecorations.RenderOperationHeader(
            "ROM Scan",
            new HeaderMetadata("Root", root),
            new HeaderMetadata("Scope", recursive ? "Recursive" : "Top-level"),
            new HeaderMetadata("Instance", InstancePathResolver.CurrentInstance),
            new HeaderMetadata("Indexed Extensions", ScanExtensions.Count.ToString("N0")));

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(root, "*.*", searchOption)
            .Where(file => ScanExtensions.Contains(Path.GetExtension(file)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No ROM files found with supported extensions.[/]");
            return (int)ExitCode.OK;
        }

        var dbPath = Path.Combine(InstancePathResolver.GetInstanceRoot(), "db");
        await using var dbManager = new DatabaseManager(dbPath);
        await dbManager.InitializeAsync();
        var romRepository = new RomRepository(dbManager.GetConnection());
        var scanTimestamp = DateTime.UtcNow;
        var extensionStats = new Dictionary<string, (int Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        var startTime = DateTime.UtcNow;

        // Initialize PSX parser if we are in PSX mode
        PsxNameParser? psxParser = null;
        if (_currentSystem.Code.Equals("psx", StringComparison.OrdinalIgnoreCase))
        {
            psxParser = new PsxNameParser();
        }

        var progressColumns = new ProgressColumn[]
        {
            new TaskDescriptionColumn(),
            new ProgressBarColumn { Width = 50, CompletedStyle = new Style(Color.SpringGreen1), RemainingStyle = new Style(Color.Grey35) },
            new PercentageColumn(),
            new RemainingTimeColumn(),
            new SpinnerColumn()
        };

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(progressColumns)
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Indexing ROMs", maxValue: files.Count);
                foreach (var file in files)
                {
                    OperationContextScope.ThrowIfCancellationRequested();
                    var info = new FileInfo(file);
                    totalBytes += info.Length;

                    var ext = info.Extension.ToLowerInvariant();
                    extensionStats.TryGetValue(ext, out var stats);
                    stats.Count++;
                    stats.Bytes += info.Length;
                    extensionStats[ext] = stats;

                    var romRecord = BuildRomRecord(file, scanTimestamp);

                    // Enhanced detection for PSX
                    if (psxParser != null && (romRecord.SystemId == "PSX" || IsPsxExtension(file)))
                    {
                        try 
                        {
                            var discInfo = psxParser.Parse(file);
                            romRecord = romRecord with 
                            { 
                                Title = discInfo.Title ?? romRecord.Title,
                                Region = discInfo.Region ?? romRecord.Region,
                                Serial = discInfo.Serial,
                                DiscNumber = discInfo.DiscNumber,
                                DiscCount = discInfo.DiscCount
                            };
                        }
                        catch
                        {
                            // Fallback to basic record if parsing fails
                        }
                    }

                    await romRepository.UpsertRomAsync(romRecord);

                    task.Description = $"Indexing {TruncateLabel(Path.GetFileName(file))}";
                    task.Increment(1);
                }
            });

        var duration = DateTime.UtcNow - startTime;

        var summary = new Table().Border(TableBorder.Rounded);
        summary.AddColumn("[cyan]Metric[/]");
        summary.AddColumn("[green]Value[/]");
        summary.AddRow("ROM files", files.Count.ToString("N0"));
        summary.AddRow("Scope", recursive ? "Recursive" : "Top-level");
        summary.AddRow("Total size", FormatSize(totalBytes));
        summary.AddRow("Duration", $"{duration.TotalSeconds:F1}s");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Scan complete. ROM cache updated.[/]");
        AnsiConsole.Write(summary);

        if (extensionStats.Count > 0)
        {
            var topExtensions = extensionStats
                .OrderByDescending(kv => kv.Value.Count)
                .ThenByDescending(kv => kv.Value.Bytes)
                .Take(6)
                .ToList();

            var extTable = new Table { Title = new TableTitle("[cyan]Top Extensions[/]") };
            extTable.AddColumn("Ext");
            extTable.AddColumn("Count");
            extTable.AddColumn("Size");

            foreach (var (ext, stats) in topExtensions)
            {
                extTable.AddRow(ext, stats.Count.ToString("N0"), FormatSize(stats.Bytes));
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(extTable);
        }

        AnsiConsole.MarkupLine($"\n[dim]Next: run [bold]verify --root \"{root}\"[/] to hash files.[/]");

        return (int)ExitCode.OK;
    }

    private static bool IsPsxExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return new[] { ".bin", ".cue", ".iso", ".pbp", ".chd", ".cso" }
            .Contains(ext, StringComparer.OrdinalIgnoreCase);
    }


    private static async Task<int> RunVerifyAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrWhiteSpace(root))
        {
            AnsiConsole.MarkupLine("[red][[IMPACT]] | Component: verify | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red][[IMPACT]] | Component: verify | Context: Directory not found: {root.EscapeMarkup()} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = HasFlag(args, "--recursive");

        ConsoleDecorations.RenderOperationHeader(
            "Verify ROMs",
            new HeaderMetadata("Root", root),
            new HeaderMetadata("Scope", recursive ? "Recursive" : "Top-level"),
            new HeaderMetadata("Instance", InstancePathResolver.CurrentInstance),
            new HeaderMetadata("Hashes", "CRC32 / MD5 / SHA1"));

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(root, "*.*", searchOption)
            .Where(f => VerifyExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No supported ROM files found to verify.[/]");
            return (int)ExitCode.OK;
        }

        var dbPath = Path.Combine(InstancePathResolver.GetInstanceRoot(), "db");
        await using var dbManager = new DatabaseManager(dbPath);
        await dbManager.InitializeAsync();
        var romRepository = new RomRepository(dbManager.GetConnection());

        var hasher = new FileHasher();
        var startTime = DateTime.UtcNow;
        var totalBytes = 0L;
        var processed = 0;

        var progressColumns = new ProgressColumn[]
        {
            new TaskDescriptionColumn(),
            new ProgressBarColumn { Width = 50, CompletedStyle = new Style(Color.SpringGreen1), RemainingStyle = new Style(Color.Grey35) },
            new PercentageColumn(),
            new RemainingTimeColumn(),
            new SpinnerColumn()
        };

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(progressColumns)
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Hashing ROMs", maxValue: files.Count);
                foreach (var file in files)
                {
                    OperationContextScope.ThrowIfCancellationRequested();
                    task.Description = $"Hashing {TruncateLabel(Path.GetFileName(file))}";

                    var metadataRecord = BuildRomRecord(file, DateTime.UtcNow);
                    await romRepository.UpsertRomAsync(metadataRecord);

                    var result = await hasher.ComputeHashesAsync(file);
                    totalBytes += result.FileSize;
                    await romRepository.UpdateHashesAsync(new RomHashUpdate(
                        result.FilePath,
                        result.Crc32,
                        result.Md5,
                        result.Sha1,
                        result.FileSize,
                        DateTime.UtcNow));

                    processed++;
                    task.Increment(1);
                }
            });

        var duration = DateTime.UtcNow - startTime;
        var throughput = totalBytes / 1024.0 / 1024.0 / Math.Max(duration.TotalSeconds, 0.001);

        var summary = new Table().Border(TableBorder.Rounded);
        summary.AddColumn("[cyan]Metric[/]");
        summary.AddColumn("[green]Value[/]");
        summary.AddRow("Files hashed", processed.ToString("N0"));
        summary.AddRow("Scope", recursive ? "Recursive" : "Top-level");
        summary.AddRow("Total size", FormatSize(totalBytes));
        summary.AddRow("Duration", $"{duration.TotalSeconds:F1}s");
        summary.AddRow("Throughput", $"{throughput:F2} MB/s");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Verification complete. Hashes stored in ROM cache.[/]");
        AnsiConsole.Write(summary);

        return (int)ExitCode.OK;
    }
    private static async Task<int> ShowMenuAsync()
    {
        EnsureSessionPrimed();

        while (true)
        {
            AnsiConsole.Clear();
            RenderMenuHeader();

            var psxLabel = $"{_currentSystem.Name} Operations";
            string choice;
            try
            {
                choice = PromptWithCancel(
                    () => AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[bold cyan]Main Operations[/] [grey](arrow keys + Enter | ESC to cancel)[/]")
                            .PageSize(10)
                            .AddChoices(
                                "Medical Bay",
                                "ROM Scan & Verify",
                                psxLabel,
                                "Archive Extract",
                                "DAT Sync",
                                "DAT Console",
                                "Settings",
                                _menuDryRun ? "Switch to APPLY mode" : "Switch to DRY-RUN mode",
                                "Exit")),
                    "Main menu",
                    offerRetryPrompt: false);
            }
            catch (OperationCanceledException)
            {
                return (int)ExitCode.UserCancelled;
            }

            switch (choice)
            {
                case "Medical Bay":
                    await ExecuteMenuAction("Medical Bay", () => RunMedicalBayAsync(Array.Empty<string>()));
                    break;
                case "ROM Scan & Verify":
                    await ShowRomMaintenanceMenuAsync();
                    break;
                case string ops when ops == psxLabel:
                    await ShowPsxOperationsMenuAsync();
                    break;
                case "Archive Extract":
                    await ExecuteMenuAction("Archive Extract", RunMenuExtractArchivesAsync, watchForEscape: !_menuDryRun);
                    break;
                case "DAT Sync":
                    await ExecuteMenuAction("DAT Sync", RunMenuDatSyncAsync);
                    break;
                case "DAT Console":
                    await ExecuteMenuAction("DAT Console", RunMenuDatConsoleAsync);
                    break;
                case "Settings":
                    await ShowSettingsMenuAsync();
                    break;
                case "Switch to APPLY mode":
                case "Switch to DRY-RUN mode":
                    _menuDryRun = !_menuDryRun;
                    PersistSession();
                    var mode = _menuDryRun ? "[yellow]DRY-RUN[/]" : "[green]APPLY[/]";
                    AnsiConsole.MarkupLine($"\n[bold]Mode updated:[/] {mode}");
                    Pause();
                    break;
                case "Exit":
                    return (int)ExitCode.OK;
            }
        }
    }

    private static void EnsureSessionPrimed()
    {
        if (_sessionPrimed)
        {
            return;
        }

        _sessionPrimed = true;

        var intro = new Panel("[bold cyan]Welcome back to ARK-Retro-Forge[/]\n[grey]Medical Bay is your first stop. Set your ROM root and active system to reduce prompts later.[/]")
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("teal"),
            Padding = new Padding(1, 1),
            Expand = true
        };
        AnsiConsole.Write(intro);

        if (string.IsNullOrWhiteSpace(_rememberedRomRoot))
        {
            try
            {
                var root = PromptForOptional("Enter ROM root directory (leave blank to decide later)");
                if (!string.IsNullOrWhiteSpace(root))
                {
                    _rememberedRomRoot = root;
                }
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]ROM root prompt skipped.[/]");
            }
        }

        try
        {
            var keepSystem = PromptYesNo($"Continue working on {_currentSystem.Name}? (System code: {_currentSystem.Code.ToUpperInvariant()})", true);
            if (!keepSystem)
            {
                _currentSystem = PromptForSystemProfile("Select default system profile");
                _rememberedRomRoot = null;
            }
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]System selection skipped.[/]");
        }

        PersistSession();
    }

    private static async Task ShowRomMaintenanceMenuAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            RenderMenuHeader();
            string choice;
            try
            {
                choice = PromptWithCancel(
                    () => AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[bold cyan]ROM Cache & Hashing[/]")
                            .AddChoices(
                                "Scan directories",
                                "Verify hashes",
                                "Back")),
                    "ROM cache menu",
                    offerRetryPrompt: false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            switch (choice)
            {
                case "Scan directories":
                    await ExecuteMenuAction("Scan", RunMenuScanAsync);
                    break;
                case "Verify hashes":
                    await ExecuteMenuAction("Verify", RunMenuVerifyAsync);
                    break;
                case "Back":
                    return;
            }
        }
    }

    private static async Task ShowPsxOperationsMenuAsync()
    {
        if (!string.IsNullOrWhiteSpace(_rememberedRomRoot))
        {
            var args = new[] { "scan", "--root", _rememberedRomRoot, "--recursive" };
            await RunWithOperationScope("Auto-Scan", () => RunScanAsync(args), watchForEscape: true);
        }

        while (true)
        {
            AnsiConsole.Clear();
            RenderMenuHeader();
            string choice;
            try
            {
                choice = PromptWithCancel(
                    () => AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title($"[bold cyan]{_currentSystem.Name} Toolkit[/]")
                            .AddChoices(
                                "Clean library",
                                "Rename library",
                                "Convert images",
                                "Merge multi-track BINs",
                                "Manage Playlists",
                                "Manage CUE sheets",
                                "Find duplicates",
                                "Back")),
                    $"{_currentSystem.Name} toolkit",
                    offerRetryPrompt: false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            switch (choice)
            {
                case "Clean library":
                    await ExecuteMenuAction("Clean", RunMenuCleanPsxAsync);
                    break;
                case "Rename library":
                    await ExecuteMenuAction("Rename", RunMenuRenamePsxAsync, watchForEscape: !_menuDryRun);
                    break;
                case "Convert images":
                    await ExecuteMenuAction("Convert", RunMenuConvertPsxAsync, watchForEscape: !_menuDryRun);
                    break;
                case "Merge multi-track BINs":
                    await ExecuteMenuAction("Merge", RunMenuMergePsxAsync, watchForEscape: !_menuDryRun);
                    break;
                case "Manage Playlists":
                    await ExecuteMenuAction("Playlist", RunMenuPlaylistPsxAsync, watchForEscape: !_menuDryRun);
                    break;
                case "Manage CUE sheets":
                    await ExecuteMenuAction("CUE Tool", RunMenuCuePsxAsync, watchForEscape: !_menuDryRun);
                    break;
                case "Find duplicates":
                    await ExecuteMenuAction("Duplicates", RunMenuDuplicatesPsxAsync);
                    break;
                case "Back":
                    return;
            }
        }
    }

    private static Task ShowSettingsMenuAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            RenderMenuHeader();
            string choice;
            try
            {
                choice = PromptWithCancel(
                    () => AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[bold cyan]Settings[/]")
                            .AddChoices(
                                "Set ROM root",
                                "Set active system",
                                "Set instance profile",
                                "Toggle sleep prevention",
                                "View session info",
                                "Back")),
                    "Settings menu",
                    offerRetryPrompt: false);
            }
            catch (OperationCanceledException)
            {
                return Task.CompletedTask;
            }

            switch (choice)
            {
                case "Set ROM root":
                    SetRomRoot();
                    Pause();
                    break;
                case "Set active system":
                    SetSystemProfile();
                    Pause();
                    break;
                case "Set instance profile":
                    SetInstanceProfile();
                    Pause();
                    break;
                case "Toggle sleep prevention":
                    ToggleSleepInhibition();
                    Pause();
                    break;
                case "View session info":
                    RenderSessionInfo();
                    Pause();
                    break;
                case "Back":
                    return Task.CompletedTask;
            }
        }
    }

    private static void RenderSessionInfo()
    {
        var info = new Table().Border(TableBorder.Rounded);
        info.AddColumn("Key");
        info.AddColumn("Value");
        info.AddRow("Instance", InstancePathResolver.CurrentInstance.EscapeMarkup());
        info.AddRow("ROM root", string.IsNullOrWhiteSpace(_rememberedRomRoot) ? "[red]Not set[/]" : _rememberedRomRoot.EscapeMarkup());
        info.AddRow("System", $"{_currentSystem.Name} ({_currentSystem.Code.ToUpperInvariant()})");
        info.AddRow("Mode", _menuDryRun ? "DRY-RUN" : "APPLY");
        info.AddRow("Sleep prevention", _preventSleep ? "Enabled" : "Disabled");
        AnsiConsole.Write(info);
    }

    private static SystemProfile PromptForSystemProfile(string title)
        => PromptWithCancel(
            () => AnsiConsole.Prompt(
                new SelectionPrompt<SystemProfile>()
                    .Title($"[white]{title}[/]")
                    .PageSize(5)
                    .UseConverter(p => $"{p.Name} ({p.Code.ToUpperInvariant()}) - {p.Description}")
                    .AddChoices(SystemProfiles.All)),
            title);

    private static void SetSystemProfile()
    {
        _currentSystem = PromptForSystemProfile("Select default system profile");
        PersistSession();
        AnsiConsole.MarkupLine($"[green]System set to {_currentSystem.Name} ({_currentSystem.Code.ToUpperInvariant()})[/]");
    }

    private static void ToggleSleepInhibition()
    {
        _preventSleep = !_preventSleep;
        ApplySleepInhibition();
        SessionStateManager.Update(state => state with { PreventSleep = _preventSleep });
        var stateText = _preventSleep ? "[green]Sleep/hibernate prevention enabled[/]" : "[yellow]Sleep/hibernate prevention disabled[/]";
        AnsiConsole.MarkupLine(stateText);
    }

    private static void ApplySleepInhibition()
    {
        var state = EXECUTION_STATE.ES_CONTINUOUS;
        if (_preventSleep)
        {
            state |= EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_DISPLAY_REQUIRED;
        }

        try
        {
            SetThreadExecutionState(state);
        }
        catch
        {
            // Ignore environments that don't support this Win32 API.
        }
    }

    private static void TryApplyConsoleDefaults()
    {
        try
        {
            if (Console.IsOutputRedirected)
            {
                return;
            }

            var targetWidth = Math.Min(150, Console.LargestWindowWidth);
            var targetHeight = Math.Min(45, Console.LargestWindowHeight);
            if (targetWidth > Console.WindowWidth || targetHeight > Console.WindowHeight)
            {
                Console.SetWindowSize(Math.Max(Console.WindowWidth, targetWidth), Math.Max(Console.WindowHeight, targetHeight));
            }
        }
        catch
        {
            // Non-fatal: resizing console may not be supported in all hosts.
        }
    }


    private static async Task ExecuteMenuAction(string label, Func<Task<int>> action, bool watchForEscape = false)
    {
        while (true)
        {
        try
        {
            var exitCode = await RunWithOperationScope(label, action, watchForEscape);
                var status = exitCode == (int)ExitCode.OK
                    ? "[green]Completed[/]"
                    : $"[yellow]Exit code {(ExitCode)exitCode}[/]";
                var safeLabel = Markup.Escape(label);
                AnsiConsole.MarkupLine($"\n[bold]{safeLabel}[/]: {status}");
                Pause();
                break;
            }
            catch (OperationCanceledException)
            {
                // If cancelled, just break the loop to return to menu without pausing
                AnsiConsole.MarkupLine("\n[yellow]Operation cancelled.[/]");
                break;
            }
            catch (Exception ex)
            {
                CliLogger.LogError($"Menu action '{label}' failed", ex);
                var component = Markup.Escape(label.ToLowerInvariant());
                var context = Markup.Escape(ex.Message);
                AnsiConsole.MarkupLine($"\n[red][[IMPACT]] | Component: {component} | Context: {context}[/]");
                Pause(); // Only pause on error
                break;
            }
        }
    }

    private static async Task<int> RunWithOperationScope(string label, Func<Task<int>> action, bool watchForEscape = false)
    {
        using var scope = OperationContextScope.Begin(label, watchForEscape);
        return await action();
    }
    private static void RenderMenuHeader()
    {
        var modeText = _menuDryRun
            ? "[yellow]DRY-RUN (preview only)[/]"
            : "[green]APPLY (changes will be written)[/]";

        var rootText = string.IsNullOrWhiteSpace(_rememberedRomRoot)
            ? "[red]ROM root: Not set[/]"
            : $"[dim]ROM root:[/] {_rememberedRomRoot.EscapeMarkup()}";

        var systemText = $"[dim]System:[/] {_currentSystem.Name} ({_currentSystem.Code.ToUpperInvariant()})";
        var instanceText = $"[dim]Instance:[/] {InstancePathResolver.CurrentInstance.EscapeMarkup()}";
        var datSummary = DatStatusReporter.Inspect(_currentSystem.Code).FirstOrDefault();
        var datText = datSummary switch
        {
            null => "[yellow]DAT status: catalog offline[/]",
            _ when !datSummary.HasCatalog => "[red]DAT status: missing[/]",
            _ when datSummary.IsStale => "[yellow]DAT status: stale[/]",
            _ => "[green]DAT status: ready[/]"
        };
        var sleepText = _preventSleep
            ? "[green]Sleep prevention: Enabled[/]"
            : "[yellow]Sleep prevention: Disabled[/]";
        var tip = "[grey]Tip: Use arrow keys to navigate, Enter to run, and ESC to cancel any prompt. Toggle DRY-RUN/APPLY, set ROM root, or switch instance any time.[/]";
        var version = GetVersion();

        var header = new Panel(new Markup($"[bold silver]ARK-Retro-Forge v{version}[/]\n[dim]Interactive Operations Menu[/]\n\nMode: {modeText}\n{rootText}\n{systemText}\n{instanceText}\n{datText}\n{sleepText}\n\n{tip}"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("cyan"),
            Padding = new Padding(1, 1),
            Expand = true
        };

        AnsiConsole.Write(header);
        AnsiConsole.Write(new Rule("[dim]Operations[/]") { Style = Style.Parse("grey") });
        AnsiConsole.WriteLine();
    }

    private static async Task<int> RunMenuScanAsync()
    {
        var root = EnsureRomRoot("Enter root folder to scan");
        if (string.IsNullOrWhiteSpace(root))
        {
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = PromptYesNo("Scan recursively?", true);
        var args = new List<string> { "scan", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }

        return await RunWithOperationScope("Scan", () => RunScanAsync(args.ToArray()), watchForEscape: true);
    }

    private static async Task<int> RunMenuVerifyAsync()
    {
        var root = EnsureRomRoot("Enter root folder to verify");
        if (string.IsNullOrWhiteSpace(root))
        {
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = PromptYesNo("Scan recursively?", true);
        var args = new List<string> { "verify", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }

        return await RunWithOperationScope("Verify", () => RunVerifyAsync(args.ToArray()), watchForEscape: true);
    }

    private static async Task<int> RunMenuRenamePsxAsync()
    {
        var root = EnsureRomRoot("Enter PSX root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return (int)ExitCode.InvalidArgs;
        }

        var defaults = SessionStateManager.State.RenamePsx;
        
        var choiceMap = new Dictionary<string, string>
        {
            { "Recursive", "Recursive Scan" },
            { "Version", "Include Version/Revision in filename" },
            { "Articles", "Restore Articles (e.g. ', The' -> 'The ')" },
            { "Playlists", "Manage Playlists (.m3u)" },
            { "MultiDisc", "Detect Multi-Disc Sets (Assign Disc Numbers)" },
            { "MultiTrack", "Scan CUE files (Handle multi-track BINs)" }
        };

        var prompt = new MultiSelectionPrompt<string>()
            .Title("Select [green]rename options[/]:")
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
            .AddChoices(choiceMap.Values);
        
        if (defaults.Recursive)
        {
            prompt.Select(choiceMap["Recursive"]);
        }
        if (defaults.IncludeVersion)
        {
            prompt.Select(choiceMap["Version"]);
        }
        if (defaults.RestoreArticles)
        {
            prompt.Select(choiceMap["Articles"]);
        }
        if (defaults.PlaylistMode != "off")
        {
            prompt.Select(choiceMap["Playlists"]);
        }
        if (defaults.MultiDisc)
        {
            prompt.Select(choiceMap["MultiDisc"]);
        }
        if (defaults.MultiTrack)
        {
            prompt.Select(choiceMap["MultiTrack"]);
        }

        var selections = AnsiConsole.Prompt(prompt);
        
        var recursive = selections.Contains(choiceMap["Recursive"]);
        var includeVersion = selections.Contains(choiceMap["Version"]);
        var restoreArticles = selections.Contains(choiceMap["Articles"]);
        var managePlaylists = selections.Contains(choiceMap["Playlists"]);
        var multiDisc = selections.Contains(choiceMap["MultiDisc"]);
        var multiTrack = selections.Contains(choiceMap["MultiTrack"]);
        
        var playlistMode = "off";
        if (managePlaylists)
        {
             var modePrompt = new SelectionPrompt<string>()
                .Title("Playlist Mode")
                .AddChoices("Create (New only)", "Update (Create & Sync existing)");
             
             var modeSel = AnsiConsole.Prompt(modePrompt);
             playlistMode = modeSel.StartsWith("Update") ? "update" : "create";
        }

        SessionStateManager.Update(s => s with {
            RenamePsx = new RenamePsxOptions {
                Recursive = recursive,
                IncludeVersion = includeVersion,
                PlaylistMode = playlistMode,
                RestoreArticles = restoreArticles,
                MultiDisc = multiDisc,
                MultiTrack = multiTrack
            }
        });

        var apply = !_menuDryRun && PromptYesNo("Apply changes?", true);

        var args = new List<string> { "rename", "psx", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (apply)
        {
            args.Add("--apply");
        }
        if (includeVersion)
        {
            args.Add("--include-version");
        }
        if (restoreArticles)
        {
            args.Add("--restore-articles");
        }
        if (playlistMode != "off")
        {
            args.Add("--playlists");
            args.Add(playlistMode);
        }
        if (!multiDisc)
        {
            args.Add("--no-multi-disc");
        }
        if (!multiTrack)
        {
            args.Add("--no-multi-track");
        }

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]Global dry-run mode: rename will preview changes only.[/]");
        }

        return await RenamePsxCommand.RunAsync(args.ToArray());
    }

    private static async Task<int> RunMenuConvertPsxAsync()
    {
        var root = EnsureRomRoot("Enter PSX root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = PromptYesNo("Scan recursively?", true);
        var apply = !_menuDryRun;
        var target = PromptForConversionTarget();
        var rebuild = PromptYesNo("Force rebuild existing outputs?", false);
        var deleteSource = apply && PromptYesNo("Delete source files after conversion?", false);

        var args = new List<string> { "convert", "psx", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (apply)
        {
            args.Add("--apply");
        }
        if (rebuild)
        {
            args.Add("--rebuild");
        }
        if (deleteSource)
        {
            args.Add("--delete-source");
        }
        if (target != PsxConversionTarget.Chd)
        {
            args.Add("--to");
            args.Add(target switch
            {
                PsxConversionTarget.BinCue => "bin",
                PsxConversionTarget.Iso => "iso",
                _ => "chd"
            });
        }

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]Global dry-run mode: conversion plans will not create CHDs.[/]");
        }

        return await ConvertPsxCommand.RunAsync(args.ToArray());
    }

    private static async Task<int> RunMenuMergePsxAsync()
    {
        var root = EnsureRomRoot("Enter PSX root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = PromptYesNo("Scan recursively?", true);
        var apply = !_menuDryRun;
        var deleteSources = apply && PromptYesNo("Delete original BIN/CUE files after merge?", false);

        var args = new List<string> { "merge", "psx", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (apply)
        {
            args.Add("--apply");
        }
        if (deleteSources)
        {
            args.Add("--delete-source");
        }
        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]Global dry-run mode: merge will only show planned operations.[/]");
        }

        return await MergePsxCommand.RunAsync(args.ToArray());
    }

    private static async Task<int> RunMenuDuplicatesPsxAsync()
    {
        var root = EnsureRomRoot("Enter PSX root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = PromptYesNo("Scan recursively?", true);
        var exportJson = PromptYesNo("Write JSON report to logs/?", false);

        var args = new List<string> { "duplicates", "psx", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (exportJson)
        {
            args.Add("--json");
        }

        return await DuplicatesPsxCommand.RunAsync(args.ToArray());
    }

    private static async Task<int> RunMenuDatSyncAsync()
    {
        var defaultSystem = _currentSystem.Code;
        var prompt = $"Filter by system code (psx, ps2, gba, etc.) or leave blank [{defaultSystem}]";
        var systemInput = PromptForOptional(prompt);
        var system = string.IsNullOrWhiteSpace(systemInput) ? defaultSystem : systemInput;
        var force = PromptYesNo("Force download even if cached?", false);

        var args = new List<string> { "dat", "sync" };
        if (!string.IsNullOrWhiteSpace(system))
        {
            args.Add("--system");
            args.Add(system);
        }
        if (force)
        {
            args.Add("--force");
        }

        return await DatSyncCommand.RunAsync(args.ToArray());
    }

    private static async Task<int> RunMenuDatConsoleAsync()
    {
        var summaries = DatStatusReporter.Inspect().ToList();
        if (summaries.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]DAT catalog offline. Run `dat sync` to download sources first.[/]");
            return (int)ExitCode.OK;
        }

        await ShowDatConsoleAsync(summaries);
        return (int)ExitCode.OK;
    }

    private static async Task<int> RunMenuCleanPsxAsync()
    {
        var root = EnsureRomRoot("Enter PSX root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return (int)ExitCode.InvalidArgs;
        }

        var defaults = SessionStateManager.State.CleanPsx;
        
        // Descriptive choices map
        var choiceMap = new Dictionary<string, string>
        {
            { "Recursive", "Recursive Scan (Include subdirectories)" },
            { "MultiTrack", "Organize Multi-Track Sets (Merge .bin/.cue tracks into Title folders)" },
            { "MultiDisc", "Organize Multi-Disc Sets (Group Disc 1/2/etc. into Title folders)" },
            { "Cues", "Generate Missing CUEs (Create .cue files for .bin files)" },
            { "Flatten", "Flatten Single-Disc Folders (Move single-disc games to root)" },
            { "Ingest", "Import Staged ROMs (Process 'Ingest' folder)" }
        };

        var prompt = new MultiSelectionPrompt<string>()
            .Title("Select [green]cleaning operations[/] to perform:")
            .PageSize(10)
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
            .AddChoices(choiceMap.Values);

        if (defaults.Recursive)
        {
            prompt.Select(choiceMap["Recursive"]);
        }
        if (defaults.MultiTrack)
        {
            prompt.Select(choiceMap["MultiTrack"]);
        }
        if (defaults.MultiDisc)
        {
            prompt.Select(choiceMap["MultiDisc"]);
        }
        if (defaults.GenerateCues)
        {
            prompt.Select(choiceMap["Cues"]);
        }
        if (defaults.Flatten)
        {
            prompt.Select(choiceMap["Flatten"]);
        }
        if (defaults.Ingest)
        {
            prompt.Select(choiceMap["Ingest"]);
        }

        var selections = AnsiConsole.Prompt(prompt);
        
        var recursive = selections.Contains(choiceMap["Recursive"]);
        var moveMultiTrack = selections.Contains(choiceMap["MultiTrack"]);
        var moveMultiDisc = selections.Contains(choiceMap["MultiDisc"]);
        var generateCues = selections.Contains(choiceMap["Cues"]);
        var flattenSingles = selections.Contains(choiceMap["Flatten"]);
        var doIngest = selections.Contains(choiceMap["Ingest"]);

        // Save preferences
        SessionStateManager.Update(s => s with
        {
            CleanPsx = new CleanPsxOptions
            {
                Recursive = recursive,
                MultiTrack = moveMultiTrack,
                MultiDisc = moveMultiDisc,
                GenerateCues = generateCues,
                Flatten = flattenSingles,
                Ingest = doIngest
            }
        });

        string? multiTrackDirName = null;
        if (moveMultiTrack)
        {
            multiTrackDirName = PromptForOptional("Multi-track container folder (blank = organize into 'Title (Region)' folders in ROM root)");
        }

        string? ingestRoot = null;
        string? importDirName = null;
        var ingestMove = false;

        if (doIngest)
        {
            ingestRoot = PromptForOptional("Import directory path");
            if (!string.IsNullOrWhiteSpace(ingestRoot))
            {
                importDirName = PromptForOptional("Import folder name (blank for 'PSX Imports')");
                ingestMove = PromptYesNo("Move imported ROMs into the PSX root?", true);
            }
        }

        var apply = !_menuDryRun && PromptYesNo("Apply changes (moves/writes)?", true);

        var args = new List<string> { "clean", "psx", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (apply)
        {
            args.Add("--apply");
        }
        if (moveMultiTrack)
        {
            args.Add("--move-multitrack");
            if (!string.IsNullOrWhiteSpace(multiTrackDirName))
            {
                args.Add("--multitrack-dir");
                args.Add(multiTrackDirName);
            }
        }
        if (moveMultiDisc)
        {
            args.Add("--move-multidisc");
        }
        if (generateCues)
        {
            args.Add("--generate-cues");
        }
        if (flattenSingles)
        {
            args.Add("--flatten");
        }
        if (!string.IsNullOrWhiteSpace(ingestRoot))
        {
            args.Add("--ingest-root");
            args.Add(ingestRoot);
            if (ingestMove)
            {
                args.Add("--ingest-move");
            }
            if (!string.IsNullOrWhiteSpace(importDirName))
            {
                args.Add("--import-dir");
                args.Add(importDirName);
            }
        }

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]Global dry-run mode: cleaner will preview without moving files.[/]");
        }

        return await RunWithOperationScope("Clean PSX", () => CleanPsxCommand.RunAsync(args.ToArray()), watchForEscape: true);
    }

    private static async Task<int> RunMenuExtractArchivesAsync()
    {
        var root = EnsureRomRoot("Enter archive root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return (int)ExitCode.InvalidArgs;
        }

        var output = PromptForOptional("Enter output directory (blank for root)");
        var recursive = PromptYesNo("Scan recursively?", true);
        var apply = !_menuDryRun;
        var deleteSource = apply && PromptYesNo("Delete source archives after extraction?", false);

        var args = new List<string> { "extract", "archives", "--root", root };
        if (!string.IsNullOrWhiteSpace(output))
        {
            args.Add("--output");
            args.Add(output);
        }
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (apply)
        {
            args.Add("--apply");
        }
        if (deleteSource)
        {
            args.Add("--delete-source");
        }

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]Global dry-run mode: extraction will only preview work.[/]");
        }

        return await ExtractArchivesCommand.RunAsync(args.ToArray());
    }

    private enum PromptCancelAction
    {
        Retry,
        Return
    }

    private static T PromptWithCancel<T>(Func<T> promptFactory, string context, bool offerRetryPrompt = true)
    {
        while (true)
        {
            try
            {
                return promptFactory();
            }
            catch (OperationCanceledException)
            {
                if (!offerRetryPrompt)
                {
                    throw;
                }

                if (ShowPromptCancelledOptions(context) == PromptCancelAction.Return)
                {
                    throw;
                }
            }
        }
    }

    private static PromptCancelAction ShowPromptCancelledOptions(string context)
    {
        AnsiConsole.MarkupLine($"\n[yellow]{context} cancelled via ESC.[/]");
        AnsiConsole.MarkupLine("[grey]Press [bold]R[/] to retry or [bold]M[/] to return.[/]");
        while (true)
        {
            var key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.R:
                    AnsiConsole.MarkupLine("[grey]Retrying prompt...[/]");
                    return PromptCancelAction.Retry;
                case ConsoleKey.M:
                case ConsoleKey.Enter:
                case ConsoleKey.Escape:
                    AnsiConsole.MarkupLine("[grey]Returning to previous menu.[/]");
                    return PromptCancelAction.Return;
            }
        }
    }

    private static string PromptForPath(string prompt)
    {
        var response = PromptWithCancel(
            () => AnsiConsole.Prompt(
                new TextPrompt<string>($"[white]{Markup.Escape(prompt)}[/]\n[bold]>[/] ")
                    .AllowEmpty()
                    .PromptStyle("green")),
            prompt);
        return response.Trim();
    }

    private static string PromptForOptional(string prompt)
    {
        var response = PromptWithCancel(
            () => AnsiConsole.Prompt(
                new TextPrompt<string>($"[white]{Markup.Escape(prompt)}[/]\n[bold]>[/] ")
                    .AllowEmpty()
                    .PromptStyle("green")),
            prompt);
        return response.Trim();
    }

    private static bool PromptYesNo(string prompt, bool defaultValue)
    {
        var defaultLabel = defaultValue ? "Yes" : "No";
        var choices = defaultValue
            ? new[] { "Yes", "No" }
            : new[] { "No", "Yes" };

        var choice = PromptWithCancel(
            () => AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[white]{Markup.Escape(prompt)}[/] [grey](default: {defaultLabel})[/]")
                    .PageSize(3)
                    .AddChoices(choices)),
            prompt);

        return string.Equals(choice, "Yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string PromptForPlaylistMode()
    {
        var response = PromptWithCancel(
            () => AnsiConsole.Prompt(
                new TextPrompt<string>(Markup.Escape("Playlist mode (create/update/off) [create]:"))
                    .AllowEmpty()
                    .PromptStyle("green")),
            "Playlist mode").Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(response) ? "create" : response;
    }

    private static PsxConversionTarget PromptForConversionTarget()
    {
        var choices = new[]
        {
            ("CHD (cue/bin -> chd)", PsxConversionTarget.Chd),
            ("BIN/CUE (chd -> cue/bin)", PsxConversionTarget.BinCue),
            ("ISO (chd -> iso)", PsxConversionTarget.Iso)
        };

        var selection = PromptWithCancel(
            () => AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[white]Select conversion target[/]")
                    .AddChoices(choices.Select(c => c.Item1))),
            "Conversion target");

        return choices.First(c => c.Item1 == selection).Item2;
    }

    private static void Pause()
    {
        AnsiConsole.MarkupLine("\n[grey]Press ENTER to return to the menu...[/]");
        Console.ReadLine();
        AnsiConsole.Clear();
    }

    private static string? EnsureRomRoot(string prompt)
    {
        if (!string.IsNullOrWhiteSpace(_rememberedRomRoot))
        {
            AnsiConsole.MarkupLine($"[dim]Using saved ROM root:[/] {_rememberedRomRoot.EscapeMarkup()}");
            return _rememberedRomRoot;
        }

        var root = PromptForPath(prompt);
        if (string.IsNullOrWhiteSpace(root))
        {
            AnsiConsole.MarkupLine("[red]No root directory provided.[/]");
            return null;
        }

        _rememberedRomRoot = root;
        PersistSession();
        return root;
    }

    private static void SetRomRoot()
    {
        var newRoot = PromptForOptional("Enter ROM root directory (leave blank to clear)");
        if (string.IsNullOrWhiteSpace(newRoot))
        {
            _rememberedRomRoot = null;
            AnsiConsole.MarkupLine("[yellow]Cleared saved ROM root.[/]");
        }
        else
        {
            _rememberedRomRoot = newRoot;
            AnsiConsole.MarkupLine($"[green]ROM root set to {newRoot.EscapeMarkup()}[/]");
        }
        PersistSession();
    }

    private static void SetInstanceProfile()
    {
        var newInstance = PromptForOptional("Enter instance name (leave blank for 'default')");
        if (string.IsNullOrWhiteSpace(newInstance))
        {
            InstancePathResolver.SetInstanceName("default");
        }
        else
        {
            InstancePathResolver.SetInstanceName(newInstance);
        }

        SessionStateManager.Reload();
        var session = SessionStateManager.State;
        _rememberedRomRoot = session.RomRoot;
        _menuDryRun = session.MenuDryRun;
        _currentSystem = SystemProfiles.Resolve(session.SystemCode);
        _sessionPrimed = false;
        PersistSession();
        AnsiConsole.MarkupLine($"[green]Instance profile set to {InstancePathResolver.CurrentInstance.EscapeMarkup()}[/]");
    }

    private static void PersistSession()
    {
        SessionStateManager.Update(state => state with
        {
            RomRoot = _rememberedRomRoot,
            MenuDryRun = _menuDryRun,
            SystemCode = _currentSystem.Code,
            PreventSleep = _preventSleep
        });
    }

    private static bool HasFlag(string[] args, string flag)
        => args.Any(arg => arg.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string TruncateLabel(string? value, int maxLength = 48)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "...";
        }

        var text = value.Trim();
        if (text.Length <= maxLength)
        {
            return text.EscapeMarkup();
        }

        return $"{text[..Math.Max(1, maxLength - 3)].EscapeMarkup()}...";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var order = (int)Math.Floor(Math.Log(bytes, 1024));
        order = Math.Clamp(order, 0, units.Length - 1);
        var value = bytes / Math.Pow(1024, order);
        return $"{value:F2} {units[order]}";
    }

    [DllImport("kernel32.dll")]
    private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    [Flags]
    private enum EXECUTION_STATE : uint
    {
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_SYSTEM_REQUIRED = 0x00000001
    }

    private static readonly Regex TitleRegionPattern = new(@"^(?<title>.+?)\s*\((?<region>[^)]+)\)", RegexOptions.Compiled);

    private static RomRecord BuildRomRecord(string filePath, DateTime timestamp)
    {
        var info = new FileInfo(filePath);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        var match = TitleRegionPattern.Match(nameWithoutExt);

        string? title = null;
        string? region = null;
        if (match.Success)
        {
            title = match.Groups["title"].Value.Trim();
            region = match.Groups["region"].Value.Trim();
        }

        var systemId = GuessSystemId(info.Extension);

        return new RomRecord(
            filePath,
            info.Length,
            timestamp,
            systemId,
            title,
            region,
            Path.GetFileName(filePath));
    }

    private static string? GuessSystemId(string extension)
    {
        var ext = extension.ToLowerInvariant();
        if (new[] { ".bin", ".cue", ".chd", ".pbp", ".iso" }.Contains(ext))
        {
            return "PSX";
        }
        if (new[] { ".wbfs", ".rvz", ".wux", ".gcm" }.Contains(ext))
        {
            return "Nintendo";
        }
        if (new[] { ".xci", ".nsp", ".nsz" }.Contains(ext))
        {
            return "Switch";
        }
        if (new[] { ".nes", ".smc", ".sfc", ".gb", ".gbc", ".gba" }.Contains(ext))
        {
            return "Retro-Nintendo";
        }
        if (new[] { ".z64", ".n64", ".v64" }.Contains(ext))
        {
            return "N64";
        }
        return null;
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static int ShowHelp()
    {
        PrintHelp();
        return (int)ExitCode.OK;
    }

    private static int ShowVersion()
    {
        Console.WriteLine($"ARK-Retro-Forge v{GetVersion()}");
        return (int)ExitCode.OK;
    }

    private static int ShowUnknownCommand(string command)
    {
        Console.WriteLine($"Î“Ã¿Ã¤âˆ©â••Ã… [IMPACT] Unknown command: {command}");
        Console.WriteLine();
        PrintHelp();
        return (int)ExitCode.InvalidArgs;
    }

    private static void PrintBanner()
    {
        var border = new string('=', 59);
        Console.WriteLine(border);
        Console.WriteLine("  ARK-Retro-Forge v{0}", GetVersion());
        Console.WriteLine("  Spaceflight Toolchain - Portable - Deterministic");
        Console.WriteLine("  No ROMs/BIOS/Keys included - User-supplied tools");
        Console.WriteLine(border);
        Console.WriteLine();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: ark-retro-forge <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  doctor              Check for missing external tools and validate environment");
        Console.WriteLine("    --json            Output results in JSON format");
        Console.WriteLine();
        Console.WriteLine("  scan                Scan directories for ROM files");
        Console.WriteLine("    --root <path>     Root directory to scan (required)");
        Console.WriteLine("    --recursive       Scan subdirectories");
        Console.WriteLine();
        Console.WriteLine("  verify              Verify ROM integrity with hash checking");
        Console.WriteLine("    --root <path>     Root directory to verify (required)");
        Console.WriteLine("    --recursive       Scan subdirectories");
        Console.WriteLine();
        Console.WriteLine("  rename psx          Rename PSX files to standard format");
        Console.WriteLine("    --root <path>     Root directory (required)");
        Console.WriteLine("    --recursive       Scan subdirectories");
        Console.WriteLine("    --apply           Apply rename operations (default is dry-run)");
        Console.WriteLine("    --verbose         Show full paths");
        Console.WriteLine("    --debug           Show debug information");
        Console.WriteLine("    --playlists <mode> Playlist handling: create (default), update, or off");
        Console.WriteLine();
        Console.WriteLine("  convert psx         Convert PSX CUE files to CHD format");
        Console.WriteLine("    --root <path>     Root directory (required)");
        Console.WriteLine("    --recursive       Scan subdirectories");
        Console.WriteLine("    --apply           Apply conversions (default is dry-run)");
        Console.WriteLine("    --delete-source   Delete source files after conversion (requires --apply)");
        Console.WriteLine("    --rebuild         Force reconversion even if CHD exists");
        Console.WriteLine("    --playlist-mode <mode> Playlist handling: chd (default), bin, or off");
        Console.WriteLine();
        Console.WriteLine("  clean psx          Organize PSX ROM directories");
        Console.WriteLine("    --root <path>     Root directory (required)");
        Console.WriteLine("    --recursive       Scan subdirectories");
        Console.WriteLine("    --apply           Apply moves/writes (default is preview)");
        Console.WriteLine("    --multitrack-dir <name>  Destination folder for multi-track sets");
        Console.WriteLine("    --ingest-root <path>    Optional import directory to ingest");
        Console.WriteLine("    --import-dir <name>     Target folder for ingested ROMs");
        Console.WriteLine("    --move-multitrack  Move detected multi-track BIN/CUE sets");
        Console.WriteLine("    --generate-cues    Generate missing CUE files");
        Console.WriteLine("    --ingest-move      Move imported ROMs into the root");
        Console.WriteLine("    --flatten          Flatten single-disc folders back into the root");
        Console.WriteLine();
        Console.WriteLine("  dat sync            Download DAT catalogs from known sources");
        Console.WriteLine("    --system <id>     Filter to a specific system (psx, ps2, gba, etc.)");
        Console.WriteLine("    --force           Re-download even if cached locally");
        Console.WriteLine();
        Console.WriteLine("  duplicates psx      Detect duplicate PSX disc images");
        Console.WriteLine("  dupes psx           (alias for duplicates psx)");
        Console.WriteLine("    --root <path>     Root directory (required)");
        Console.WriteLine("    --recursive       Scan subdirectories");
        Console.WriteLine("    --hash <algo>     Hash algorithm: SHA1 (default), MD5");
        Console.WriteLine("    --json            Write detailed report to logs/ directory");
        Console.WriteLine();
        Console.WriteLine("  --help, -h          Show this help message");
        Console.WriteLine("  --version, -v       Show version information");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ark-retro-forge doctor");
        Console.WriteLine("  ark-retro-forge scan --root C:\\ROMs");
        Console.WriteLine("  ark-retro-forge verify --root C:\\ROMs");
        Console.WriteLine("  ark-retro-forge rename psx --root C:\\PSX --recursive");
        Console.WriteLine("  ark-retro-forge rename psx --root C:\\PSX --recursive --apply");
        Console.WriteLine("  ark-retro-forge convert psx --root C:\\PSX --recursive --apply");
        Console.WriteLine("  ark-retro-forge duplicates psx --root C:\\PSX --recursive --json");
        Console.WriteLine();
        Console.WriteLine("â‰¡Æ’Ã†Ã­ Run 'doctor' first to check your environment");
    }

    private static string GetVersion()
    {
        // Keep in sync with the next planned release tag
        return "1.0.3";
    }

    private static bool HasInteractiveConsole()
    {
        try
        {
            if (Console.IsOutputRedirected || Console.IsInputRedirected)
            {
                return false;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }

        return AnsiConsole.Profile.Capabilities.Interactive;
    }

    private static void ParseGlobalArgs(ref string[] args)
    {
        if (args.Length == 0)
        {
            return;
        }

        var filtered = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--instance", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    InstancePathResolver.SetInstanceName(args[++i]);
                }
                continue;
            }

            filtered.Add(args[i]);
        }

        args = filtered.ToArray();
    }

    private static async Task<int> RunMenuCuePsxAsync()
    {
        var root = EnsureRomRoot("Enter PSX root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = PromptYesNo("Scan recursively?", true);
        var apply = !_menuDryRun;
        
        var modes = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select CUE operations")
                .AddChoices("Create missing CUEs", "Update invalid CUEs", "Scrape from Redump (Not Implemented)")
        );

        var create = modes.Contains("Create missing CUEs");
        var update = modes.Contains("Update invalid CUEs");
        var scrape = modes.Contains("Scrape from Redump (Not Implemented)");
        var force = create && PromptYesNo("Force overwrite existing CUEs?", false);

        var args = new List<string> { "cue", "psx", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (apply)
        {
            args.Add("--apply");
        }
        if (create)
        {
            args.Add("--create");
        }
        if (update)
        {
            args.Add("--update");
        }
        if (scrape)
        {
            args.Add("--scrape");
        }
        if (force)
        {
            args.Add("--force");
        }

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]Global dry-run mode: CUE tool will preview changes only.[/]");
        }

        return await CuePsxCommand.RunAsync(args.ToArray());
    }

    private static async Task<int> RunMenuPlaylistPsxAsync()
    {
        var root = EnsureRomRoot("Enter PSX root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = PromptYesNo("Scan recursively?", true);
        var createNew = PromptYesNo("Create new playlists?", true);
        var updateExisting = PromptYesNo("Update existing playlists?", true);
        var apply = !_menuDryRun && PromptYesNo("Apply changes?", true);

        var args = new List<string> { "playlist", "psx", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (apply)
        {
            args.Add("--apply");
        }
        if (!createNew)
        {
            args.Add("--no-create");
        }
        if (!updateExisting)
        {
            args.Add("--no-update");
        }

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]Global dry-run mode: playlist tool will preview changes only.[/]");
        }

        return await PlaylistPsxCommand.RunAsync(args.ToArray());
    }
}












