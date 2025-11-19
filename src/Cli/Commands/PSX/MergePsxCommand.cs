using ARK.Cli.Infrastructure;
using ARK.Core.Database;
using ARK.Core.Systems.PSX;
using Spectre.Console;
using HeaderMetadata = ARK.Cli.Infrastructure.ConsoleDecorations.HeaderMetadata;

namespace ARK.Cli.Commands.PSX;

public static class MergePsxCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrWhiteSpace(root))
        {
            AnsiConsole.MarkupLine("[red][[IMPACT]] | Component: merge psx | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red][[IMPACT]] | Component: merge psx | Context: Directory not found: {root} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var deleteFlag = args.Contains("--delete-source");
        var flatten = args.Contains("--flatten");

        if (deleteFlag && !apply)
        {
            AnsiConsole.MarkupLine("[red][[IMPACT]] | Component: merge psx | Context: --delete-source requires --apply | Fix: Add --apply[/]");
            return (int)ExitCode.InvalidArgs;
        }

        ConsoleDecorations.RenderOperationHeader(
            "PSX Merge",
            new HeaderMetadata("Mode", apply ? "[green]APPLY[/]" : "[yellow]DRY-RUN[/]", IsMarkup: true),
            new HeaderMetadata("Root", root),
            new HeaderMetadata("Scope", recursive ? "Recursive" : "Top-level"),
            new HeaderMetadata("Flatten", flatten ? "Yes" : "No"),
            new HeaderMetadata("Delete source", deleteFlag ? "[red]Yes[/]" : "No", IsMarkup: deleteFlag));
        DatUsageHelper.WarnIfCatalogMissing("psx", "PSX merge");

        var operations = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("yellow"))
            .StartAsync("Scanning for multi-track CUE files...", async ctx =>
            {
                await using var dbManager = new DatabaseManager(Path.Combine(InstancePathResolver.GetInstanceRoot(), "db"));
                await dbManager.InitializeAsync();
                var repo = new RomRepository(dbManager.GetConnection());

                var planner = new PsxBinMergePlanner();
                var ops = await planner.PlanMergesAsync(root, recursive, outputDirectory: flatten ? root : null, romRepository: repo, flatten: flatten);
                ctx.Status($"Found {ops.Count} multi-track layout(s)");
                await Task.Delay(500); // Brief pause to show result
                return ops;
            });

        var eligibleOperations = operations.Where(o => !o.IsBlocked).ToList();
        var skippedOperations = operations.Count - eligibleOperations.Count;

        if (operations.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No multi-track BIN layouts found[/]");
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
            AnsiConsole.MarkupLine("[yellow]Next step: Add --apply to execute merges[/]");
            return (int)ExitCode.OK;
        }

        OperationContextScope.ThrowIfCancellationRequested();
        var deleteSources = deleteFlag || PromptDeleteSources();
        var service = new PsxBinMergeService();
        var merged = 0;
        var failures = new List<string>();
        var token = OperationContextScope.CurrentToken;

        var progressColumns = new ProgressColumn[]
        {
            new TaskDescriptionColumn { Alignment = Justify.Left },
            new ProgressBarColumn { Width = 40, CompletedStyle = new Style(Color.SpringGreen1), RemainingStyle = new Style(Color.Grey35) },
            new PercentageColumn(),
            new SpinnerColumn()
        };

        await AnsiConsole.Progress()
            .AutoClear(true)
            .Columns(progressColumns)
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Merging multi-track sets", maxValue: eligibleOperations.Count);
                foreach (var op in eligibleOperations)
                {
                    token.ThrowIfCancellationRequested();
                    // Pad the description to a fixed width to prevent jitter
                    var label = TruncateLabel(op.Title ?? Path.GetFileNameWithoutExtension(op.CuePath), 35);
                    task.Description = $"Merging {label.PadRight(35)}";
                    
                    try
                    {
                        await service.MergeAsync(op, deleteSources, token);
                        merged++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{op.Title ?? Path.GetFileName(op.CuePath)}: {ex.Message}");
                        AnsiConsole.MarkupLine($"[red]  Error merging {Path.GetFileName(op.CuePath).EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
                    }

                    task.Increment(1);
                }
            });

        AnsiConsole.MarkupLine($"[green][[DOCKED]] Merged {merged} multi-track set(s)[/]");
        if (deleteSources)
        {
            AnsiConsole.MarkupLine("[dim]  Source BIN/CUE files deleted after merge[/]");
        }

        if (failures.Count > 0)
        {
            var content = string.Join(Environment.NewLine, failures.Select(f => $"- {f.EscapeMarkup()}"));
            var panel = new Panel(new Markup(content))
            {
                Header = new PanelHeader($"[red]Failed ({failures.Count})[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };
            AnsiConsole.WriteLine();
            AnsiConsole.Write(panel);
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
    private static bool PromptDeleteSources()
    {
        var prompt = new SelectionPrompt<string>()
            .Title("[white]Delete original BIN/CUE files after merge?[/]")
            .AddChoices("No", "Yes");

        var choice = AnsiConsole.Prompt(prompt);
        return choice.Equals("Yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string TruncateLabel(string? value, int maxLength = 48)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "PSX";
        }

        if (value.Length <= maxLength)
        {
            return value.EscapeMarkup();
        }

        var head = Math.Max(maxLength - 3, 1);
        return $"{value[..head].EscapeMarkup()}...";
    }
}
