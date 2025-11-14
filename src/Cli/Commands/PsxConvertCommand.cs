using ARK.Core.Psx;
using ARK.Core.Tools;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// PSX convert command handler
/// </summary>
public static class PsxConvertCommand
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        var recursive = HasFlag(args, "--recursive");
        var flattenMultidisc = HasFlag(args, "--flatten-multidisc");
        var targetFormatArg = GetArgValue(args, "--target-format") ?? "chd";
        var fromChdToBinCue = HasFlag(args, "--from-chd-to-bincue");
        var deleteSource = HasFlag(args, "--delete-source");
        var apply = HasFlag(args, "--apply");
        var force = HasFlag(args, "--force");
        
        if (string.IsNullOrEmpty(root))
        {
            AnsiConsole.MarkupLine("[red]Error: --root is required[/]");
            return 1;
        }
        
        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red]Error: Directory not found: {root}[/]");
            return 1;
        }
        
        // Determine conversion direction
        var direction = fromChdToBinCue ? ConversionDirection.ChdToBinCue : ConversionDirection.BinCueToChd;
        var targetFormat = fromChdToBinCue ? "bincue" : targetFormatArg;
        
        if (!fromChdToBinCue && !targetFormat.Equals("chd", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[red]Error: Only 'chd' target format is supported for PSX[/]");
            return 1;
        }
        
        try
        {
            var planner = new PsxConvertPlanner();
            var plan = planner.CreatePlan(root, recursive, flattenMultidisc, targetFormat, direction, deleteSource);
            
            if (!apply)
            {
                // Dry-run mode
                PrintDryRunReport(plan);
                return 0;
            }
            
            // Apply mode - show confirmation unless --force
            if (!force)
            {
                var totalOps = plan.TotalConversions + plan.TotalPlaylistWrites;
                var deleteWarning = deleteSource ? " (sources will be deleted)" : "";
                if (!AnsiConsole.Confirm($"Apply {totalOps} operations{deleteWarning}?", false))
                {
                    AnsiConsole.MarkupLine("[yellow]Operation cancelled by user[/]");
                    return 0;
                }
            }
            
            // Execute the plan
            var toolManager = new ToolManager();
            var chdTool = new ChdmanTool(toolManager);
            var executor = new PsxConvertExecutor(chdTool, maxParallelism: 4);
            
            AnsiConsole.MarkupLine("[bold cyan]Converting...[/]");
            var result = await AnsiConsole.Status()
                .StartAsync("Processing conversions...", async ctx =>
                {
                    return await executor.ExecuteAsync(plan, dryRun: false);
                });
            
            PrintExecutionResult(result);
            
            return result.ConversionsFailed > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
    
    private static void PrintDryRunReport(ConvertPlan plan)
    {
        AnsiConsole.MarkupLine("[bold cyan]PSX Convert Plan (Dry Run)[/]");
        AnsiConsole.WriteLine();
        
        // Global header
        var directionStr = plan.Direction == ConversionDirection.BinCueToChd 
            ? "BIN/CUE → CHD" 
            : "CHD → BIN/CUE";
        AnsiConsole.MarkupLine($"[bold]Direction:[/] {directionStr}");
        AnsiConsole.MarkupLine($"[bold]Target Format:[/] {plan.TargetFormat}");
        AnsiConsole.MarkupLine($"[bold]Delete Sources:[/] {(plan.DeleteSource ? "[red]Yes[/]" : "[green]No[/]")}");
        AnsiConsole.WriteLine();
        
        if (plan.TitlePlans.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No PSX titles found[/]");
            return;
        }
        
        foreach (var titlePlan in plan.TitlePlans)
        {
            var title = titlePlan.TitleGroup;
            
            // Title header
            var titleText = $"[bold]{title.Title}[/]";
            if (!string.IsNullOrEmpty(title.Region))
            {
                titleText += $" [dim]({title.Region})[/]";
            }
            if (!string.IsNullOrEmpty(title.Version))
            {
                titleText += $" [dim][{title.Version}][/]";
            }
            
            AnsiConsole.MarkupLine(titleText);
            
            if (!string.IsNullOrEmpty(titlePlan.SkipReason))
            {
                AnsiConsole.MarkupLine($"  [yellow]Skipped:[/] {titlePlan.SkipReason}");
                AnsiConsole.WriteLine();
                continue;
            }
            
            // Conversion operations
            foreach (var op in titlePlan.ConversionOperations)
            {
                var sourceName = Path.GetFileName(op.SourcePath);
                var destName = plan.Direction == ConversionDirection.BinCueToChd
                    ? Path.GetFileName(op.DestinationPath)
                    : "(CUE + BIN files)";
                
                AnsiConsole.MarkupLine($"  [green]Convert:[/] {sourceName}");
                AnsiConsole.MarkupLine($"       [dim]→[/] {destName}");
                
                if (op.DeleteSourceAfterSuccess)
                {
                    AnsiConsole.MarkupLine($"       [red]Delete source after success[/]");
                    if (op.AssociatedFilesToDelete.Count > 0)
                    {
                        AnsiConsole.MarkupLine($"       [red]+ {op.AssociatedFilesToDelete.Count} associated files[/]");
                    }
                }
            }
            
            // Playlist operation
            if (titlePlan.PlaylistOperation != null)
            {
                var playlistName = Path.GetFileName(titlePlan.PlaylistOperation.PlaylistPath);
                AnsiConsole.MarkupLine($"  [blue]Create playlist:[/] {playlistName}");
                foreach (var discFile in titlePlan.PlaylistOperation.DiscFiles)
                {
                    AnsiConsole.MarkupLine($"       [dim]• {discFile}[/]");
                }
            }
            
            AnsiConsole.WriteLine();
        }
        
        // Summary
        var table = new Table();
        table.AddColumn("Operation");
        table.AddColumn("Count");
        table.AddRow("Conversions", plan.TotalConversions.ToString());
        table.AddRow("Playlists", plan.TotalPlaylistWrites.ToString());
        table.AddRow("Skipped", plan.TotalSkipped.ToString());
        
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Run with --apply to execute these operations[/]");
    }
    
    private static void PrintExecutionResult(PsxConvertExecutor.ExecutionResult result)
    {
        AnsiConsole.MarkupLine("[bold green]Execution Complete[/]");
        AnsiConsole.WriteLine();
        
        var table = new Table();
        table.AddColumn("Operation");
        table.AddColumn("Count");
        
        if (result.ConversionsSucceeded > 0)
        {
            table.AddRow("Conversions Succeeded", $"[green]{result.ConversionsSucceeded}[/]");
        }
        if (result.PlaylistsWritten > 0)
        {
            table.AddRow("Playlists Written", $"[green]{result.PlaylistsWritten}[/]");
        }
        if (result.SourcesDeleted > 0)
        {
            table.AddRow("Sources Deleted", $"[yellow]{result.SourcesDeleted}[/]");
        }
        if (result.Skipped > 0)
        {
            table.AddRow("Skipped", $"[dim]{result.Skipped}[/]");
        }
        if (result.ConversionsFailed > 0)
        {
            table.AddRow("Conversions Failed", $"[red]{result.ConversionsFailed}[/]");
        }
        
        AnsiConsole.Write(table);
        
        if (result.Errors.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold red]Errors:[/]");
            foreach (var error in result.Errors.Take(10))
            {
                AnsiConsole.MarkupLine($"  [red]• {error}[/]");
            }
            if (result.Errors.Count > 10)
            {
                AnsiConsole.MarkupLine($"  [dim]... and {result.Errors.Count - 10} more[/]");
            }
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
    
    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
    }
}
