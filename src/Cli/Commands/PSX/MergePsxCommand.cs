using ARK.Cli.Infrastructure;
using ARK.Core.Systems.PSX;
using Spectre.Console;

namespace ARK.Cli.Commands.PSX;

public static class MergePsxCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrWhiteSpace(root))
        {
            AnsiConsole.MarkupLine("[red]�~,�,? [IMPACT] | Component: merge psx | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red]�~,�,? [IMPACT] | Component: merge psx | Context: Directory not found: {root} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var deleteFlag = args.Contains("--delete-source");

        if (deleteFlag && !apply)
        {
            AnsiConsole.MarkupLine("[red]�~,�,? [IMPACT] | Component: merge psx | Context: --delete-source requires --apply | Fix: Add --apply[/]");
            return (int)ExitCode.InvalidArgs;
        }

        AnsiConsole.MarkupLine("[cyan]dY>��,? [[PSX BIN MERGE]][/] Root: {0}", root.EscapeMarkup());
        if (recursive)
        {
            AnsiConsole.MarkupLine("[dim]  Mode: Recursive[/]");
        }
        if (apply)
        {
            AnsiConsole.MarkupLine("[yellow]  Apply mode enabled[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]  DRY RUN (use --apply to execute)[/]");
        }
        AnsiConsole.WriteLine();

        var planner = new PsxBinMergePlanner();
        var operations = planner.PlanMerges(root, recursive);

        var eligibleOperations = operations.Where(o => !o.IsBlocked).ToList();
        var skippedOperations = operations.Count - eligibleOperations.Count;

        if (operations.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]�s��,?  No multi-track BIN layouts found[/]");
            return (int)ExitCode.OK;
        }

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("[cyan]Title[/]");
        table.AddColumn("[yellow]Tracks[/]");
        table.AddColumn("[green]Status[/]");
        table.AddColumn("[red]Notes[/]");

        foreach (var op in operations)
        {
            var status = op.IsBlocked
                ? "[red]SKIP[/]"
                : op.AlreadyMerged
                    ? "[green]READY[/]"
                    : "[yellow]MERGE[/]";

            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(op.BlockReason))
            {
                notes.Add(op.BlockReason);
            }
            if (op.Notes.Count > 0)
            {
                notes.AddRange(op.Notes);
            }

            table.AddRow(
                (op.Title ?? "Unknown").EscapeMarkup(),
                op.TrackSources.Count.ToString(),
                status,
                string.Join("; ", notes).EscapeMarkup()
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[cyan]Summary:[/]");
        AnsiConsole.MarkupLine($"  Planned merges: [yellow]{operations.Count}[/]");
        AnsiConsole.MarkupLine($"  Ready to merge: [green]{eligibleOperations.Count}[/]");
        if (skippedOperations > 0)
        {
            AnsiConsole.MarkupLine($"  Skipped (blocked): [red]{skippedOperations}[/]");
        }
        AnsiConsole.WriteLine();

        if (!apply || !eligibleOperations.Any())
        {
            AnsiConsole.MarkupLine("[yellow]dY'� Next step: Add --apply to execute merges[/]");
            return (int)ExitCode.OK;
        }

        var deleteSources = deleteFlag || AnsiConsole.Confirm("[bold]Delete original BIN/CUE files after merge?[/]", false);
        var service = new PsxBinMergeService();
        var merged = 0;

        foreach (var op in eligibleOperations)
        {
            try
            {
                await service.MergeAsync(op, deleteSources);
                merged++;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]  Error merging {Path.GetFileName(op.CuePath)}: {ex.Message}[/]");
            }
        }

        AnsiConsole.MarkupLine($"[green]�o\" [DOCKED] Merged {merged} multi-track sets[/]");
        if (deleteSources)
        {
            AnsiConsole.MarkupLine("[dim]  Source BIN/CUE files deleted after merge[/]");
        }

        return (int)ExitCode.OK;
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
