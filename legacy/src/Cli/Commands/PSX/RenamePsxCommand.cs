using ARK.Cli.Infrastructure;
using ARK.Core.Database;
using ARK.Core.Systems.PSX;
using Spectre.Console;

namespace ARK.Cli.Commands.PSX;

/// <summary>
/// Handles the 'rename psx' command
/// </summary>
public static class RenamePsxCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrEmpty(root))
        {
            AnsiConsole.MarkupLine("[red]☄️ [[IMPACT]] | Component: rename psx | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red]☄️ [[IMPACT]] | Component: rename psx | Context: Directory not found: {root} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var verbose = args.Contains("--verbose");
        var debug = args.Contains("--debug");
        var keepLanguageTags = args.Contains("--keep-language-tags");
        var includeVersion = args.Contains("--include-version");
        var noMultiDisc = args.Contains("--no-multi-disc");
        var noMultiTrack = args.Contains("--no-multi-track");
        
        // Parse --playlists flag (create|update|off, default: create)
        var playlistMode = GetArgValue(args, "--playlists") ?? "create";
        var restoreArticles = args.Contains("--restore-articles");
        var createPlaylists = playlistMode.Equals("create", StringComparison.OrdinalIgnoreCase) || 
                             playlistMode.Equals("update", StringComparison.OrdinalIgnoreCase);
        var updatePlaylists = playlistMode.Equals("update", StringComparison.OrdinalIgnoreCase);

        DatUsageHelper.WarnIfCatalogMissing("psx", "PSX rename");

        var playlistLabel = createPlaylists ? playlistMode : "off";
        var languageTagLabel = keepLanguageTags ? "Keep" : "Strip";
        ConsoleDecorations.RenderOperationHeader(
            "PSX Rename",
            new ConsoleDecorations.HeaderMetadata("Root", root),
            new ConsoleDecorations.HeaderMetadata("Scope", recursive ? "Recursive" : "Top-level"),
            new ConsoleDecorations.HeaderMetadata("Mode", apply ? "[green]APPLY[/]" : "[yellow]DRY-RUN[/]", IsMarkup: true),
            new ConsoleDecorations.HeaderMetadata("Playlists", Markup.Escape(playlistLabel)),
            new ConsoleDecorations.HeaderMetadata("Articles", restoreArticles ? "Restore" : "Default"),
            new ConsoleDecorations.HeaderMetadata("Lang tags", Markup.Escape(languageTagLabel)),
            new ConsoleDecorations.HeaderMetadata("Multi-Disc", noMultiDisc ? "Off" : "On"),
            new ConsoleDecorations.HeaderMetadata("Multi-Track", noMultiTrack ? "Off" : "On"));

        // Initialize DB for cache lookups
        var dbPath = Path.Combine(InstancePathResolver.GetInstanceRoot(), "db");
        await using var dbManager = new DatabaseManager(dbPath);
        await dbManager.InitializeAsync();
        var romRepository = new RomRepository(dbManager.GetConnection());

        var planner = new PsxRenamePlanner();
        var playlistPlanner = new PsxPlaylistPlanner();
        List<PsxRenameOperation> operations = new();
        List<PsxPlaylistOperation> playlistOperations = new();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots12)
            .StartAsync("Scanning PSX library...", async ctx =>
            {
                operations = await planner.PlanRenamesAsync(
                    root, 
                    recursive, 
                    restoreArticles, 
                    stripLanguageTags: !keepLanguageTags, 
                    includeVersion: includeVersion,
                    handleMultiDisc: !noMultiDisc,
                    handleMultiTrack: !noMultiTrack,
                    romRepository: romRepository);

                ctx.Status("Planning playlists...");
                if (createPlaylists)
                {
                    // Use the rename operations to plan playlists so we see the future state
                    playlistOperations = playlistPlanner.PlanPlaylistsFromRenames(
                        operations,
                        createNew: playlistMode.Equals("create", StringComparison.OrdinalIgnoreCase),
                        updateExisting: updatePlaylists);
                }
                else
                {
                    playlistOperations = new List<PsxPlaylistOperation>();
                }
            });

        // Statistics
        var alreadyNamed = operations.Count(o => o.IsAlreadyNamed);
        var toRename = operations.Count(o => !o.IsAlreadyNamed);
        var warnings = operations.Count(o => o.Warning != null);
        var cheatDiscs = operations.Count(o => o.DiscInfo.ContentType == PsxContentType.Cheat);
        var educationalDiscs = operations.Count(o => o.DiscInfo.ContentType == PsxContentType.Educational);
        var missingSerials = operations.Count(o => !o.DiscInfo.HasSerial);
        var multiDisc = operations.Count(o => o.DiscInfo.IsMultiDisc);
        var playlistCount = playlistOperations.Count;

        const int PreviewRowLimit = 150;

        // Display table for files to rename
        if (toRename > 0)
        {
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Current[/]");
            table.AddColumn("[green]New[/]");
            table.AddColumn("[yellow]Disc[/]");
            table.AddColumn("[red]Warning[/]");

            var previewRows = operations.Where(o => !o.IsAlreadyNamed);
            var rowLimit = verbose || debug ? int.MaxValue : PreviewRowLimit;

            foreach (var op in previewRows.Take(rowLimit))
            {
                var current = verbose || debug
                    ? op.SourcePath
                    : Path.GetFileName(op.SourcePath);

                var newName = verbose || debug
                    ? op.DestinationPath
                    : Path.GetFileName(op.DestinationPath);

                var disc = op.DiscInfo.DiscNumber.HasValue
                    ? $"Disc {op.DiscInfo.DiscNumber}"
                    : "-";

                var warning = op.Warning != null ? $"⚠️  {op.Warning}" : "";

                table.AddRow(
                    current.EscapeMarkup(),
                    newName.EscapeMarkup(),
                    disc,
                    warning.EscapeMarkup()
                );
            }

            AnsiConsole.Write(table);

            if (toRename > rowLimit && !verbose && !debug)
            {
                AnsiConsole.MarkupLine($"[dim]  ... and {toRename - rowLimit} more (use --verbose to see all)[/]");
            }

            AnsiConsole.WriteLine();
        }

        // Display summary
        RenderRenameSummary(
            alreadyNamed,
            toRename,
            warnings,
            cheatDiscs,
            educationalDiscs,
            missingSerials,
            multiDisc,
            playlistCount);

        // Display playlist operations
        if (playlistOperations.Any())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold cyan]🎵 [[PSX PLAYLIST PLAN]][/]");
            
            var playlistTable = new Table();
            playlistTable.Border(TableBorder.Rounded);
            playlistTable.AddColumn("[cyan]Title[/]");
            playlistTable.AddColumn("[yellow]Region[/]");
            playlistTable.AddColumn("[green]Operation[/]");
            playlistTable.AddColumn("[magenta]Discs[/]");
            
            foreach (var plOp in playlistOperations.Take(verbose || debug ? int.MaxValue : PreviewRowLimit))
            {
                var operation = plOp.OperationType == PlaylistOperationType.Create ? "CREATE" : "UPDATE";
                var discCount = plOp.DiscFilenames.Count;
                
                playlistTable.AddRow(
                    plOp.Title.EscapeMarkup(),
                    plOp.Region.EscapeMarkup(),
                    operation,
                    discCount.ToString()
                );
            }
            
            AnsiConsole.Write(playlistTable);
            AnsiConsole.WriteLine();

            if (playlistOperations.Count > PreviewRowLimit && !verbose && !debug)
            {
                AnsiConsole.MarkupLine($"[dim]  ... and {playlistOperations.Count - PreviewRowLimit} more playlist operations.[/]");
            }
        }

        // Show some warnings if present
        if (warnings > 0 && (verbose || debug))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]⚠️  Warnings:[/]");
            foreach (var op in operations.Where(o => o.Warning != null).Take(10))
            {
                var filename = Path.GetFileName(op.SourcePath);
                AnsiConsole.MarkupLine($"  [dim]{filename.EscapeMarkup()}[/]: {op.Warning.EscapeMarkup()}");
            }
        }

        var ambiguousPreview = operations
            .Where(o => !o.DiscInfo.HasSerial && o.DiscInfo.SerialCandidates.Count > 0)
            .Take(5)
            .ToList();
        if (ambiguousPreview.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Serial suggestions for ambiguous titles:[/]");
            foreach (var op in ambiguousPreview)
            {
                var filename = Path.GetFileName(op.SourcePath).EscapeMarkup();
                var suggestions = string.Join(
                    ", ",
                    op.DiscInfo.SerialCandidates
                        .Where(c => !string.IsNullOrWhiteSpace(c.Serial))
                        .Select(c => $"{c.Serial} ({c.Title})")
                        .DefaultIfEmpty("No DAT hits"));
                AnsiConsole.MarkupLine($"  [dim]{filename}[/]: {suggestions.EscapeMarkup()}");
            }
        }

        if (apply && (toRename > 0 || playlistOperations.Any()))
        {
            await ApplyRenamesAsync(operations, playlistOperations, playlistPlanner, restoreArticles, includeVersion);
        }
        else if (!apply && (toRename > 0 || playlistOperations.Any()))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]💡 Next step: Add --apply to execute renames[/]");
        }

        return (int)ExitCode.OK;
    }

    private static void RenderRenameSummary(
        int alreadyNamed,
        int toRename,
        int warnings,
        int cheatDiscs,
        int educationalDiscs,
        int missingSerials,
        int multiDisc,
        int playlistOps)
    {
        var summary = new Table().Border(TableBorder.Rounded);
        summary.AddColumn("[cyan]Metric[/]");
        summary.AddColumn("[green]Value[/]");
        summary.AddRow("Already named", alreadyNamed.ToString("N0"));
        summary.AddRow("Needs rename", toRename.ToString("N0"));
        summary.AddRow("Warnings", warnings.ToString("N0"));
        summary.AddRow("Missing serials", missingSerials.ToString("N0"));
        summary.AddRow("Multi-disc", multiDisc.ToString("N0"));
        summary.AddRow("Cheat discs", cheatDiscs.ToString("N0"));
        summary.AddRow("Educational discs", educationalDiscs.ToString("N0"));
        summary.AddRow("Playlist ops", playlistOps.ToString("N0"));
        AnsiConsole.Write(summary);
        AnsiConsole.WriteLine();
    }

    private static async Task ApplyRenamesAsync(
        IReadOnlyList<PsxRenameOperation> operations,
        IReadOnlyList<PsxPlaylistOperation> playlistOperations,
        PsxPlaylistPlanner playlistPlanner,
        bool restoreArticles,
        bool includeVersion)
    {
        // Include operations that need renaming OR have new content (e.g. updated CUE)
        var renameTargets = operations.Where(o => !o.IsAlreadyNamed || o.NewContent != null).ToList();
        var playlistTargets = playlistOperations.ToList();
        var renamed = 0;
        var skipped = 0;
        var playlistsApplied = 0;
        var failures = new List<string>();

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
                if (renameTargets.Count > 0)
                {
                    var renameTask = ctx.AddTask("Renaming files", maxValue: renameTargets.Count);
                    foreach (var op in renameTargets)
                    {
                        OperationContextScope.ThrowIfCancellationRequested();
                        renameTask.Description = $"Renaming {FormatTaskLabel(op.SourcePath)}";

                        if (!TryResolveAmbiguousOperation(op, out var resolvedInfo))
                        {
                            skipped++;
                            renameTask.Increment(1);
                            continue;
                        }

                        var destinationDir = Path.GetDirectoryName(op.DestinationPath) ?? Path.GetDirectoryName(op.SourcePath) ?? string.Empty;
                        // We need to pass includeVersion here too, but ApplyRenamesAsync signature doesn't have it.
                        // Actually, op.DestinationPath is already calculated with includeVersion in PlanRenamesAsync.
                        // But here we are recalculating finalDestination using PsxNameFormatter.Format(resolvedInfo, restoreArticles).
                        // We should pass includeVersion here too.
                        // Wait, ApplyRenamesAsync needs to know includeVersion.
                        // Or we can just trust op.DestinationPath if resolvedInfo hasn't changed.
                        // But if resolvedInfo changed (ambiguous resolution), we re-format.
                        
                        // Let's update ApplyRenamesAsync signature to accept includeVersion.
                        var finalDestination = Path.Combine(destinationDir, PsxNameFormatter.Format(resolvedInfo, restoreArticles, includeVersion));
                        
                        // Debug logging
                        // AnsiConsole.WriteLine($"DEBUG: Source={op.SourcePath}, Dest={finalDestination}, NewContent={(op.NewContent != null)}");

                        try
                        {
                            if (op.NewContent != null)
                            {
                                // Write updated content (e.g. CUE file with new BIN references)
                                Directory.CreateDirectory(Path.GetDirectoryName(finalDestination)!);
                                await File.WriteAllTextAsync(finalDestination, op.NewContent);
                                
                                // Debug check
                                // if (!File.Exists(finalDestination)) AnsiConsole.WriteLine($"ERROR: File not found after write: {finalDestination}");

                                // If we wrote to a new location, delete the old one
                                var sourceFull = Path.GetFullPath(op.SourcePath);
                                var destFull = Path.GetFullPath(finalDestination);
                                if (!string.Equals(sourceFull, destFull, StringComparison.OrdinalIgnoreCase))
                                {
                                    File.Delete(op.SourcePath);
                                }
                                renamed++;
                            }
                            else if (!string.Equals(Path.GetFullPath(op.SourcePath), Path.GetFullPath(finalDestination), StringComparison.OrdinalIgnoreCase))
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(finalDestination)!);
                                File.Move(op.SourcePath, finalDestination);
                                renamed++;
                            }
                            else
                            {
                                // Already named correctly
                            }
                        }
                        catch (Exception ex)
                        {
                            failures.Add($"{Path.GetFileName(op.SourcePath)}: {ex.Message}");
                        }

                        renameTask.Increment(1);
                    }
                }

                if (playlistTargets.Count > 0)
                {
                    var playlistTask = ctx.AddTask("Updating playlists", maxValue: playlistTargets.Count);
                    foreach (var plOp in playlistTargets)
                    {
                        OperationContextScope.ThrowIfCancellationRequested();
                        playlistTask.Description = $"Playlist {FormatTaskLabel(plOp.Title, treatAsPath: false)}";
                        try
                        {
                            playlistPlanner.ApplyOperation(plOp, createBackup: true);
                            playlistsApplied++;
                        }
                        catch (Exception ex)
                        {
                            failures.Add($"{Path.GetFileName(plOp.PlaylistPath)}: {ex.Message}");
                        }

                        playlistTask.Increment(1);
                    }
                }
            });        AnsiConsole.MarkupLine($"[green]✨ [[DOCKED]] Renamed {renamed} file(s)[/]");
        if (skipped > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Skipped {skipped} file(s) at user request.[/]");
        }
        if (playlistsApplied > 0)
        {
            AnsiConsole.MarkupLine($"[green]🎵 Applied {playlistsApplied} playlist changes[/]");
        }
        if (failures.Count > 0)
        {
            var panel = new Panel(new Markup(string.Join(Environment.NewLine, failures.Select(f => $"- {f.EscapeMarkup()}"))))
            {
                Header = new PanelHeader($"[red]Failures ({failures.Count})[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };
            AnsiConsole.WriteLine();
            AnsiConsole.Write(panel);
        }
    }

    private static bool TryResolveAmbiguousOperation(PsxRenameOperation operation, out PsxDiscInfo resolvedInfo)
    {
        resolvedInfo = operation.DiscInfo;
        if (resolvedInfo.HasSerial || resolvedInfo.SerialCandidates.Count == 0)
        {
            return true;
        }

        AnsiConsole.WriteLine();
        var fileName = Path.GetFileName(operation.SourcePath).EscapeMarkup();
        AnsiConsole.MarkupLine($"[yellow]Serial not resolved for {fileName}[/]");

        var suggestionTable = new Table().Border(TableBorder.Rounded);
        suggestionTable.AddColumn("Option");
        suggestionTable.AddColumn("Title");
        suggestionTable.AddColumn("Region");
        suggestionTable.AddColumn("Serial");
        suggestionTable.AddColumn("Discs");
        for (var i = 0; i < resolvedInfo.SerialCandidates.Count; i++)
        {
            var candidate = resolvedInfo.SerialCandidates[i];
            suggestionTable.AddRow(
                (i + 1).ToString(),
                candidate.Title.EscapeMarkup(),
                (candidate.Region ?? "-").EscapeMarkup(),
                (candidate.Serial ?? "n/a").EscapeMarkup(),
                candidate.DiscCount?.ToString() ?? "-");
        }
        AnsiConsole.Write(suggestionTable);

        var prompt = new SelectionPrompt<string>()
            .Title("[white]Choose how to handle this file[/]")
            .AddChoices("Rename anyway", "Skip this file");

        var candidateMap = new Dictionary<string, PsxSerialCandidate>();
        foreach (var candidate in resolvedInfo.SerialCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Serial))
            {
                continue;
            }
            var label = $"Use {candidate.Serial} - {candidate.Title}";
            prompt.AddChoice(label);
            candidateMap[label] = candidate;
        }

        var selection = AnsiConsole.Prompt(prompt);
        if (selection.Equals("Skip this file", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (candidateMap.TryGetValue(selection, out var chosen))
        {
            resolvedInfo = resolvedInfo with
            {
                Title = chosen.Title,
                Region = chosen.Region ?? resolvedInfo.Region,
                Serial = chosen.Serial,
                DiscCount = chosen.DiscCount ?? resolvedInfo.DiscCount
            };
        }

        return true;
    }

    private static string FormatTaskLabel(string value, bool treatAsPath = true, int maxLength = 48)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "PSX";
        }

        var label = treatAsPath ? Path.GetFileName(value) ?? value : value;
        if (label.Length <= maxLength)
        {
            return label.EscapeMarkup();
        }

        return $"{label[..Math.Max(1, maxLength - 3)].EscapeMarkup()}...";
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
}
