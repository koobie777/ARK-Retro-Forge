using ARK.Cli.Infrastructure;
using ARK.Core.Systems.PSX;
using Spectre.Console;

namespace ARK.Cli.Commands.PSX;

/// <summary>
/// Handles the 'playlist psx' command
/// </summary>
public static class PlaylistPsxCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrEmpty(root))
        {
            AnsiConsole.MarkupLine("[red]☄️ [[IMPACT]] | Component: playlist psx | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red]☄️ [[IMPACT]] | Component: playlist psx | Context: Directory not found: {root} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var verbose = args.Contains("--verbose");
        var debug = args.Contains("--debug");
        
        // Default to creating new and updating existing unless flags say otherwise
        // Or maybe we want explicit flags?
        // Let's stick to simple flags: --no-create, --no-update
        var createNew = !args.Contains("--no-create");
        var updateExisting = !args.Contains("--no-update");

        ConsoleDecorations.RenderOperationHeader(
            "PSX Playlist",
            new ConsoleDecorations.HeaderMetadata("Root", root),
            new ConsoleDecorations.HeaderMetadata("Scope", recursive ? "Recursive" : "Top-level"),
            new ConsoleDecorations.HeaderMetadata("Mode", apply ? "[green]APPLY[/]" : "[yellow]DRY-RUN[/]", IsMarkup: true),
            new ConsoleDecorations.HeaderMetadata("Create New", createNew ? "Yes" : "No"),
            new ConsoleDecorations.HeaderMetadata("Update Existing", updateExisting ? "Yes" : "No"));

        var planner = new PsxPlaylistPlanner();
        List<PsxPlaylistOperation> operations = new();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots12)
            .StartAsync("Scanning for multi-disc sets...", async ctx =>
            {
                await Task.Run(() => 
                {
                    operations = planner.PlanPlaylists(
                        root, 
                        recursive, 
                        preferredExtension: null, // Could add arg for this later
                        createNew: createNew,
                        updateExisting: updateExisting);
                });
            });

        const int PreviewRowLimit = 150;

        if (operations.Any())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold cyan]🎵 [[PSX PLAYLIST PLAN]][/]");
            
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Title[/]");
            table.AddColumn("[yellow]Region[/]");
            table.AddColumn("[green]Operation[/]");
            table.AddColumn("[magenta]Discs[/]");
            table.AddColumn("[blue]Path[/]");
            
            var previewRows = operations.Take(verbose || debug ? int.MaxValue : PreviewRowLimit);
            
            foreach (var op in previewRows)
            {
                var opType = op.OperationType == PlaylistOperationType.Create ? "CREATE" : "UPDATE";
                var discCount = op.DiscFilenames.Count;
                var path = verbose || debug ? op.PlaylistPath : Path.GetFileName(op.PlaylistPath);
                
                table.AddRow(
                    op.Title.EscapeMarkup(),
                    op.Region.EscapeMarkup(),
                    opType,
                    discCount.ToString(),
                    path.EscapeMarkup()
                );
            }
            
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            if (operations.Count > PreviewRowLimit && !verbose && !debug)
            {
                AnsiConsole.MarkupLine($"[dim]  ... and {operations.Count - PreviewRowLimit} more (use --verbose to see all)[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]No playlist operations needed.[/]");
        }

        if (apply && operations.Any())
        {
            await ApplyPlaylistsAsync(operations, planner);
        }
        else if (!apply && operations.Any())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]💡 Next step: Add --apply to execute playlist operations[/]");
        }

        return (int)ExitCode.OK;
    }

    private static async Task ApplyPlaylistsAsync(
        List<PsxPlaylistOperation> operations,
        PsxPlaylistPlanner planner)
    {
        var applied = 0;
        var failures = new List<string>();

        await AnsiConsole.Progress()
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Applying playlists", maxValue: operations.Count);
                
                foreach (var op in operations)
                {
                    task.Description = $"Processing {Path.GetFileName(op.PlaylistPath).EscapeMarkup()}";
                    
                    try
                    {
                        await Task.Run(() => planner.ApplyOperation(op, createBackup: true));
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{Path.GetFileName(op.PlaylistPath)}: {ex.Message}");
                    }
                    
                    task.Increment(1);
                }
            });

        AnsiConsole.MarkupLine($"[green]✨ [[DOCKED]] Processed {applied} playlist(s)[/]");
        
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
