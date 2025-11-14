using ARK.Cli.Infrastructure;
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
            AnsiConsole.MarkupLine("[red]☄️ [IMPACT] | Component: rename psx | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red]☄️ [IMPACT] | Component: rename psx | Context: Directory not found: {root} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var verbose = args.Contains("--verbose");
        var debug = args.Contains("--debug");
        
        // Parse --playlists flag (create|update|off, default: create)
        var playlistMode = GetArgValue(args, "--playlists") ?? "create";
        var createPlaylists = playlistMode.Equals("create", StringComparison.OrdinalIgnoreCase) || 
                             playlistMode.Equals("update", StringComparison.OrdinalIgnoreCase);
        var updatePlaylists = playlistMode.Equals("update", StringComparison.OrdinalIgnoreCase);

        AnsiConsole.MarkupLine("[cyan]🛰️ [[PSX RENAME]][/] Root: {0}", root.EscapeMarkup());
        if (recursive)
        {
            AnsiConsole.MarkupLine("[dim]  Mode: Recursive[/]");
        }
        if (createPlaylists)
        {
            AnsiConsole.MarkupLine($"[dim]  Playlists: {playlistMode}[/]");
        }
        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]  DRY RUN (use --apply to execute)[/]");
        }
        AnsiConsole.WriteLine();

        var planner = new PsxRenamePlanner();
        var operations = planner.PlanRenames(root, recursive);
        
        // Plan playlist operations
        var playlistPlanner = new PsxPlaylistPlanner();
        var playlistOperations = createPlaylists 
            ? playlistPlanner.PlanPlaylists(root, recursive, preferredExtension: ".cue", 
                createNew: playlistMode.Equals("create", StringComparison.OrdinalIgnoreCase),
                updateExisting: updatePlaylists)
            : new List<PsxPlaylistOperation>();

        // Statistics
        var alreadyNamed = operations.Count(o => o.IsAlreadyNamed);
        var toRename = operations.Count(o => !o.IsAlreadyNamed);
        var warnings = operations.Count(o => o.Warning != null);
        var cheatDiscs = operations.Count(o => o.DiscInfo.ContentType == PsxContentType.Cheat);
        var educationalDiscs = operations.Count(o => o.DiscInfo.ContentType == PsxContentType.Educational);
        var missingSerials = operations.Count(o => !o.DiscInfo.HasSerial);

        // Display table for files to rename
        if (toRename > 0)
        {
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Current[/]");
            table.AddColumn("[green]New[/]");
            table.AddColumn("[yellow]Disc[/]");
            table.AddColumn("[red]Warning[/]");

            foreach (var op in operations.Where(o => !o.IsAlreadyNamed).Take(verbose || debug ? int.MaxValue : 20))
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

            if (toRename > 20 && !verbose && !debug)
            {
                AnsiConsole.MarkupLine($"[dim]  ... and {toRename - 20} more (use --verbose to see all)[/]");
            }

            AnsiConsole.WriteLine();
        }

        // Display summary
        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════[/]");
        AnsiConsole.MarkupLine($"[cyan]Summary:[/]");
        AnsiConsole.MarkupLine($"  Already named: [green]{alreadyNamed}[/]");
        AnsiConsole.MarkupLine($"  To rename: [yellow]{toRename}[/]");
        AnsiConsole.MarkupLine($"  Warnings: [red]{warnings}[/]");
        AnsiConsole.MarkupLine($"  Cheat discs: [magenta]{cheatDiscs}[/]");
        AnsiConsole.MarkupLine($"  Educational discs: [blue]{educationalDiscs}[/]");
        AnsiConsole.MarkupLine($"  Missing serials: [red]{missingSerials}[/]");
        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════[/]");

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
            
            foreach (var plOp in playlistOperations)
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

        // Apply renames if requested
        if (apply && toRename > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]🔥 [BURN] Applying renames...[/]");

            var renamed = 0;
            foreach (var op in operations.Where(o => !o.IsAlreadyNamed))
            {
                try
                {
                    File.Move(op.SourcePath, op.DestinationPath);
                    renamed++;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]  Error renaming {Path.GetFileName(op.SourcePath)}: {ex.Message}[/]");
                }
            }

            AnsiConsole.MarkupLine($"[green]✨ [DOCKED] Renamed {renamed} files[/]");
            
            // Apply playlist operations
            if (playlistOperations.Any())
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]🔥 [BURN] Applying playlist operations...[/]");
                
                var playlistsApplied = 0;
                foreach (var plOp in playlistOperations)
                {
                    try
                    {
                        playlistPlanner.ApplyOperation(plOp, createBackup: true);
                        playlistsApplied++;
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]  Error with playlist {Path.GetFileName(plOp.PlaylistPath)}: {ex.Message}[/]");
                    }
                }
                
                AnsiConsole.MarkupLine($"[green]✨ [DOCKED] Applied {playlistsApplied} playlists[/]");
            }
        }
        else if (!apply && (toRename > 0 || playlistOperations.Any()))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]💡 Next step: Add --apply to execute renames[/]");
        }

        await Task.CompletedTask;
        return (int)ExitCode.OK;
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
