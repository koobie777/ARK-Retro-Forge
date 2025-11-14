using ARK.Cli.Infrastructure;
using Spectre.Console;

namespace ARK.Cli.Commands.Psx;

/// <summary>
/// Interactive helper command for PSX operations.
/// </summary>
public class PsxInteractiveCommand
{
    public static async Task<int> ExecuteAsync(string root, bool recursive, bool apply)
    {
        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Directory not found: {0}", Markup.Escape(root));
            return (int)ExitCode.InvalidArgs;
        }

        AnsiConsole.MarkupLine("[cyan]PSX Interactive Helper[/]");
        AnsiConsole.MarkupLine("Root: {0}", Markup.Escape(root));
        AnsiConsole.MarkupLine("Recursive: {0}", recursive);
        AnsiConsole.WriteLine();

        // Prompt for operation
        var operation = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices(new[]
                {
                    "Rename only",
                    "Convert only",
                    "Rename then Convert"
                }));

        // Prompt for multi-disc handling
        var flattenMultidisc = AnsiConsole.Confirm("Flatten multi-disc titles (move files out of game folders)?", false);

        bool deleteSource = false;
        if (operation.Contains("Convert"))
        {
            // Prompt for source deletion
            deleteSource = AnsiConsole.Confirm("Delete source files after successful conversion?", false);
        }

        // Confirm if apply mode
        if (apply)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]WARNING: This will make actual changes to your files![/]");
            var confirm = AnsiConsole.Confirm("Are you sure you want to apply these changes?", false);
            if (!confirm)
            {
                AnsiConsole.MarkupLine("[yellow]Operation cancelled.[/]");
                return (int)ExitCode.OK;
            }
        }

        AnsiConsole.WriteLine();

        // Execute operations
        int exitCode = 0;

        if (operation == "Rename only" || operation == "Rename then Convert")
        {
            AnsiConsole.MarkupLine("[cyan]═══ Running Rename ═══[/]");
            AnsiConsole.WriteLine();
            exitCode = await PsxRenameCommand.ExecuteAsync(root, recursive, flattenMultidisc, apply);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        if (operation == "Convert only" || operation == "Rename then Convert")
        {
            if (operation == "Rename then Convert")
            {
                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine();
            }

            AnsiConsole.MarkupLine("[cyan]═══ Running Convert ═══[/]");
            AnsiConsole.WriteLine();
            exitCode = await PsxConvertCommand.ExecuteAsync(
                root,
                recursive,
                flattenMultidisc,
                targetFormat: "chd",
                fromChdToBinCue: false,
                deleteSource: deleteSource,
                apply: apply,
                force: false);
        }

        return exitCode;
    }
}
