using ARK.Cli.Infrastructure;
using ARK.Core.Psx;
using Spectre.Console;

namespace ARK.Cli.Commands.Psx;

/// <summary>
/// Command for renaming PSX files to a standardized format.
/// </summary>
public class PsxRenameCommand
{
    public static async Task<int> ExecuteAsync(
        string root,
        bool recursive,
        bool flattenMultidisc,
        bool apply)
    {
        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Directory not found: {0}", Markup.Escape(root));
            return (int)ExitCode.InvalidArgs;
        }

        AnsiConsole.MarkupLine("[cyan]PSX Rename[/]");
        AnsiConsole.MarkupLine("Root: {0}", Markup.Escape(root));
        AnsiConsole.MarkupLine("Recursive: {0}", recursive);
        AnsiConsole.MarkupLine("Flatten Multi-Disc: {0}", flattenMultidisc);
        AnsiConsole.MarkupLine("Mode: {0}", apply ? "[green]APPLY[/]" : "[yellow]DRY RUN[/]");
        AnsiConsole.WriteLine();

        try
        {
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

            // Plan renames
            var planner = new PsxRenamePlanner();
            var operations = planner.PlanRenames(groups, flattenMultidisc);

            if (operations.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No rename operations needed. All files already have correct names.[/]");
                return (int)ExitCode.OK;
            }

            // Display plan
            AnsiConsole.MarkupLine("[cyan]Rename Plan:[/]");
            var table = new Table();
            table.AddColumn("Operation");
            table.AddColumn("Description");

            foreach (var op in operations)
            {
                // IMPORTANT: Escape dynamic text to prevent Spectre.Console markup errors
                var escapedDesc = Markup.Escape(op.Description);
                table.AddRow(op.Type.ToString(), escapedDesc);
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            // Execute
            var executor = new PsxRenameExecutor();
            var result = executor.Execute(operations, !apply);

            // Display results
            if (!apply)
            {
                AnsiConsole.MarkupLine("[yellow]DRY RUN complete. Use --apply to make changes.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[cyan]Results:[/]");
                AnsiConsole.MarkupLine("  Renamed: [green]{0}[/]", result.RenamedFiles);
                AnsiConsole.MarkupLine("  Moved: [green]{0}[/]", result.MovedFiles);
                AnsiConsole.MarkupLine("  Folders Deleted: [green]{0}[/]", result.DeletedFolders);
                AnsiConsole.MarkupLine("  Skipped: [yellow]{0}[/]", result.SkippedOperations);

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
                    AnsiConsole.MarkupLine("[green]Rename complete![/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Rename completed with errors.[/]");
                    return (int)ExitCode.GeneralError;
                }
            }

            return (int)ExitCode.OK;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] {0}", Markup.Escape(ex.Message));
            return (int)ExitCode.GeneralError;
        }
    }
}
