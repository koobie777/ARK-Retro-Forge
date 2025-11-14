using ARK.Cli.Infrastructure;
using ARK.Core.Psx;
using Spectre.Console;

namespace ARK.Cli.Commands.Psx;

/// <summary>
/// Command for converting PSX files between BIN/CUE and CHD formats.
/// </summary>
public class PsxConvertCommand
{
    public static async Task<int> ExecuteAsync(
        string root,
        bool recursive,
        bool flattenMultidisc,
        string targetFormat,
        bool fromChdToBinCue,
        bool deleteSource,
        bool apply,
        bool force)
    {
        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Directory not found: {0}", Markup.Escape(root));
            return (int)ExitCode.InvalidArgs;
        }

        // Validate options
        if (fromChdToBinCue && !string.IsNullOrEmpty(targetFormat) && targetFormat != "chd")
        {
            AnsiConsole.MarkupLine("[red]Error:[/] --from-chd-to-bincue and --target-format are mutually exclusive");
            return (int)ExitCode.InvalidArgs;
        }

        var direction = fromChdToBinCue
            ? ConversionDirection.ChdToBinCue
            : ConversionDirection.BinCueToChd;

        AnsiConsole.MarkupLine("[cyan]PSX Convert[/]");
        AnsiConsole.MarkupLine("Root: {0}", Markup.Escape(root));
        AnsiConsole.MarkupLine("Recursive: {0}", recursive);
        AnsiConsole.MarkupLine("Direction: {0}", direction == ConversionDirection.BinCueToChd ? "BIN/CUE → CHD" : "CHD → BIN/CUE");
        AnsiConsole.MarkupLine("Delete Source: {0}", deleteSource);
        AnsiConsole.MarkupLine("Mode: {0}", apply ? "[green]APPLY[/]" : "[yellow]DRY RUN[/]");
        AnsiConsole.WriteLine();

        try
        {
            // Initialize CHD tool
            var chdTool = new ChdmanTool();

            // Group PSX titles
            var grouper = new PsxTitleGrouper();
            var groups = await Task.Run(() => grouper.GroupTitles(root, recursive));

            if (groups.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No PSX titles found.[/]");
                return (int)ExitCode.OK;
            }

            AnsiConsole.MarkupLine("[green]Found {0} PSX title(s)[/]", groups.Count);
            AnsiConsole.WriteLine();

            // Plan conversions
            var planner = new PsxConvertPlanner();
            var operations = planner.PlanConversions(groups, direction, deleteSource);

            var plannedOps = operations.Where(o => o.Status == ConversionStatus.Planned).ToList();
            var skippedOps = operations.Where(o => o.Status == ConversionStatus.Skipped).ToList();

            if (plannedOps.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No conversions needed.[/]");
                if (skippedOps.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]Skipped {0} title(s):[/]", skippedOps.Count);
                    foreach (var op in skippedOps.Take(10))
                    {
                        AnsiConsole.MarkupLine("  • {0}: {1}",
                            Markup.Escape(op.TitleName),
                            Markup.Escape(op.SkipReason ?? "Unknown"));
                    }
                }
                return (int)ExitCode.OK;
            }

            // Display plan
            AnsiConsole.MarkupLine("[cyan]Conversion Plan:[/]");
            var table = new Table();
            table.AddColumn("Title");
            table.AddColumn("Disc");
            table.AddColumn("Status");
            table.AddColumn("Details");

            foreach (var op in operations)
            {
                var status = op.Status == ConversionStatus.Planned ? "[green]Planned[/]" : "[yellow]Skipped[/]";
                var details = op.Status == ConversionStatus.Skipped
                    ? Markup.Escape(op.SkipReason ?? "")
                    : Markup.Escape(Path.GetFileName(op.TargetPath ?? ""));

                table.AddRow(
                    Markup.Escape(op.TitleName),
                    op.DiscNumber.ToString(),
                    status,
                    details);
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine("[cyan]Summary:[/]");
            AnsiConsole.MarkupLine("  To Convert: [green]{0}[/]", plannedOps.Count);
            AnsiConsole.MarkupLine("  To Skip: [yellow]{0}[/]", skippedOps.Count);
            if (deleteSource && plannedOps.Any())
            {
                var filesToDelete = plannedOps.Sum(o => o.FilesToDelete.Count);
                AnsiConsole.MarkupLine("  Files to Delete: [red]{0}[/]", filesToDelete);
            }
            AnsiConsole.WriteLine();

            // Execute
            var executor = new PsxConvertExecutor(chdTool);
            var result = await executor.ExecuteAsync(operations, !apply);

            // Display results
            if (!apply)
            {
                AnsiConsole.MarkupLine("[yellow]DRY RUN complete. Use --apply to make changes.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[cyan]Results:[/]");
                AnsiConsole.MarkupLine("  Converted: [green]{0}[/]", result.ConvertedCount);
                AnsiConsole.MarkupLine("  Skipped: [yellow]{0}[/]", result.SkippedCount);
                AnsiConsole.MarkupLine("  Failed: [red]{0}[/]", result.FailedCount);

                if (result.Errors.Any())
                {
                    AnsiConsole.MarkupLine("[red]Errors:[/]");
                    foreach (var error in result.Errors)
                    {
                        AnsiConsole.MarkupLine("  [red]•[/] {0}", Markup.Escape(error));
                    }
                }

                if (result.Success)
                {
                    AnsiConsole.MarkupLine("[green]Conversion complete![/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Conversion completed with errors.[/]");
                    return (int)ExitCode.GeneralError;
                }
            }

            return (int)ExitCode.OK;
        }
        catch (FileNotFoundException ex)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] {0}", Markup.Escape(ex.Message));
            AnsiConsole.MarkupLine("[yellow]Hint:[/] Make sure chdman.exe is in the tools directory.");
            return (int)ExitCode.ToolMissing;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] {0}", Markup.Escape(ex.Message));
            return (int)ExitCode.GeneralError;
        }
    }
}
