using ARK.Cli.Infrastructure;
using ARK.Core.Systems.PSX;
using Spectre.Console;

namespace ARK.Cli.Commands.PSX;

public static class CuePsxCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrWhiteSpace(root))
        {
            AnsiConsole.MarkupLine("[red]Error: --root argument is required.[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red]Error: Root directory not found: {root}[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var force = args.Contains("--force");
        
        // Modes
        var create = args.Contains("--create");
        var update = args.Contains("--update");
        var scrape = args.Contains("--scrape");

        // Default to create+update if nothing specified
        if (!create && !update && !scrape)
        {
            create = true;
            update = true;
        }

        ConsoleDecorations.RenderOperationHeader("PSX CUE Tool",
            new ConsoleDecorations.HeaderMetadata("Root", root),
            new ConsoleDecorations.HeaderMetadata("Recursive", recursive.ToString()),
            new ConsoleDecorations.HeaderMetadata("Modes", $"{(create ? "Create " : "")}{(update ? "Update " : "")}{(scrape ? "Scrape" : "")}".Trim()),
            new ConsoleDecorations.HeaderMetadata("Mode", apply ? "APPLY" : "DRY-RUN"));

        var planner = new PsxCuePlanner(root, recursive);
        var operations = new List<PsxCueOperation>();

        await AnsiConsole.Status().StartAsync("Scanning for CUE issues...", async ctx =>
        {
            if (create)
            {
                ctx.Status("Planning CUE creations...");
                operations.AddRange(await planner.PlanCueCreationsAsync(force));
            }
            
            if (update)
            {
                ctx.Status("Planning CUE updates...");
                operations.AddRange(await planner.PlanCueUpdatesAsync());
            }

            if (scrape)
            {
                ctx.Status("Planning CUE scraping...");
                // operations.AddRange(await planner.PlanCueScrapingAsync());
                // TODO: Implement scraping
            }
        });

        if (operations.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No CUE issues found.[/]");
            return (int)ExitCode.OK;
        }

        // Render Plan
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]CUE Operations Plan[/]");
        table.AddColumn("Type");
        table.AddColumn("File");
        table.AddColumn("Details");

        foreach (var op in operations)
        {
            var color = op.Type switch
            {
                PsxCueOperationType.Create => "green",
                PsxCueOperationType.Update => "yellow",
                PsxCueOperationType.Scrape => "blue",
                _ => "white"
            };
            
            table.AddRow(
                $"[{color}]{op.Type}[/]", 
                Markup.Escape(Path.GetFileName(op.TargetCuePath)), 
                Markup.Escape(op.Details));
        }

        AnsiConsole.Write(table);

        if (!apply)
        {
            AnsiConsole.MarkupLine("\n[yellow]Dry-run mode. Use --apply to execute changes.[/]");
            return (int)ExitCode.OK;
        }

        if (!AnsiConsole.Confirm($"Proceed with {operations.Count} operations?", false))
        {
            AnsiConsole.MarkupLine("[yellow]Operation cancelled.[/]");
            return (int)ExitCode.UserCancelled;
        }

        // Execute
        await AnsiConsole.Progress().StartAsync(async ctx =>
        {
            var task = ctx.AddTask("Processing CUEs", maxValue: operations.Count);
            foreach (var op in operations)
            {
                task.Description = $"{op.Type}: {Path.GetFileName(op.TargetCuePath)}";
                
                try
                {
                    switch (op.Type)
                    {
                        case PsxCueOperationType.Create:
                        case PsxCueOperationType.Update:
                            await File.WriteAllTextAsync(op.TargetCuePath, op.NewContent);
                            break;
                        case PsxCueOperationType.Scrape:
                            // TODO: Implement scraping download
                            break;
                    }
                }
                catch (Exception ex)
                {
                    CliLogger.LogError($"Failed to process CUE: {op.TargetCuePath}", ex);
                    AnsiConsole.MarkupLine($"[red]Failed: {Path.GetFileName(op.TargetCuePath)} - {ex.Message}[/]");
                }
                
                task.Increment(1);
            }
        });

        AnsiConsole.MarkupLine("[green]CUE operations complete.[/]");
        return (int)ExitCode.OK;
    }

    private static string? GetArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
