using ARK.Core.Psx;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// PSX rename command handler
/// </summary>
public static class PsxRenameCommand
{
    public static int Execute(string[] args)
    {
        var root = GetArgValue(args, "--root");
        var recursive = HasFlag(args, "--recursive");
        var flattenMultidisc = HasFlag(args, "--flatten-multidisc");
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
        
        try
        {
            var planner = new PsxRenamePlanner();
            var plan = planner.CreatePlan(root, recursive, flattenMultidisc);
            
            if (!apply)
            {
                // Dry-run mode
                PrintDryRunReport(plan);
                return 0;
            }
            
            // Apply mode - show confirmation unless --force
            if (!force)
            {
                var totalOps = plan.TotalRenames + plan.TotalMoves + plan.TotalFolderDeletions;
                if (!AnsiConsole.Confirm($"Apply {totalOps} operations?", false))
                {
                    AnsiConsole.MarkupLine("[yellow]Operation cancelled by user[/]");
                    return 0;
                }
            }
            
            // Execute the plan
            var executor = new PsxRenameExecutor();
            var result = executor.Execute(plan, dryRun: false);
            
            PrintExecutionResult(result);
            
            return result.RenamesFailed + result.MovesFailed > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
    
    private static void PrintDryRunReport(RenamePlan plan)
    {
        AnsiConsole.MarkupLine("[bold cyan]PSX Rename Plan (Dry Run)[/]");
        AnsiConsole.WriteLine();
        
        if (plan.TitlePlans.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No PSX titles found[/]");
            return;
        }
        
        foreach (var titlePlan in plan.TitlePlans)
        {
            var title = titlePlan.TitleGroup;
            var hasOps = titlePlan.RenameOperations.Count > 0 || 
                         titlePlan.MoveOperations.Count > 0 || 
                         titlePlan.FolderDeletions.Count > 0;
            
            if (!hasOps)
            {
                continue;
            }
            
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
            
            if (title.Serials.Any())
            {
                AnsiConsole.MarkupLine($"  [dim]Serials: {string.Join(", ", title.Serials)}[/]");
            }
            
            AnsiConsole.MarkupLine($"  [dim]Discs: {title.Discs.Count}[/]");
            AnsiConsole.WriteLine();
            
            // Rename operations
            foreach (var op in titlePlan.RenameOperations)
            {
                var sourceName = Path.GetFileName(op.SourcePath);
                var destName = Path.GetFileName(op.DestinationPath);
                AnsiConsole.MarkupLine($"  [green]Rename:[/] {sourceName}");
                AnsiConsole.MarkupLine($"       [dim]→[/] {destName}");
            }
            
            // Move operations
            foreach (var op in titlePlan.MoveOperations)
            {
                var sourceName = Path.GetFileName(op.SourcePath);
                var destName = Path.GetFileName(op.DestinationPath);
                var destDir = Path.GetDirectoryName(op.DestinationPath);
                AnsiConsole.MarkupLine($"  [blue]Move:[/] {sourceName}");
                AnsiConsole.MarkupLine($"     [dim]→ {destDir}/{destName}[/]");
            }
            
            // Folder deletions
            foreach (var op in titlePlan.FolderDeletions)
            {
                AnsiConsole.MarkupLine($"  [red]Delete folder:[/] [dim]{op.FolderPath}[/]");
            }
            
            AnsiConsole.WriteLine();
        }
        
        // Summary
        var table = new Table();
        table.AddColumn("Operation");
        table.AddColumn("Count");
        table.AddRow("Renames", plan.TotalRenames.ToString());
        table.AddRow("Moves", plan.TotalMoves.ToString());
        table.AddRow("Folder Deletions", plan.TotalFolderDeletions.ToString());
        
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Run with --apply to execute these operations[/]");
    }
    
    private static void PrintExecutionResult(PsxRenameExecutor.ExecutionResult result)
    {
        AnsiConsole.MarkupLine("[bold green]Execution Complete[/]");
        AnsiConsole.WriteLine();
        
        var table = new Table();
        table.AddColumn("Operation");
        table.AddColumn("Count");
        
        if (result.RenamesSucceeded > 0)
        {
            table.AddRow("Renames Succeeded", $"[green]{result.RenamesSucceeded}[/]");
        }
        if (result.MovesSucceeded > 0)
        {
            table.AddRow("Moves Succeeded", $"[green]{result.MovesSucceeded}[/]");
        }
        if (result.FoldersDeleted > 0)
        {
            table.AddRow("Folders Deleted", $"[green]{result.FoldersDeleted}[/]");
        }
        if (result.Conflicts > 0)
        {
            table.AddRow("Conflicts", $"[yellow]{result.Conflicts}[/]");
        }
        if (result.RenamesFailed > 0)
        {
            table.AddRow("Renames Failed", $"[red]{result.RenamesFailed}[/]");
        }
        if (result.MovesFailed > 0)
        {
            table.AddRow("Moves Failed", $"[red]{result.MovesFailed}[/]");
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
