using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// Interactive PSX orchestrator command
/// </summary>
public static class PsxCommand
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        var recursive = HasFlag(args, "--recursive");
        var apply = HasFlag(args, "--apply");
        
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
            AnsiConsole.MarkupLine("[bold cyan]PSX Interactive Helper[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold]Root:[/] {root}");
            AnsiConsole.MarkupLine($"[bold]Recursive:[/] {(recursive ? "Yes" : "No")}");
            AnsiConsole.MarkupLine($"[bold]Mode:[/] {(apply ? "[red]Apply[/]" : "[yellow]Dry-run[/]")}");
            AnsiConsole.WriteLine();
            
            // Prompt 1: Operation type
            var operation = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What operation would you like to perform?")
                    .AddChoices(new[] {
                        "Rename only",
                        "Convert only (BIN/CUE → CHD)",
                        "Rename then Convert"
                    }));
            
            // Prompt 2: Multi-disc handling
            var multiDiscChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("How should multi-disc titles be handled?")
                    .AddChoices(new[] {
                        "Keep per-game folders",
                        "Flatten multi-disc to parent directory"
                    }));
            
            var flattenMultidisc = multiDiscChoice.Contains("Flatten");
            
            // Prompt 3: Delete sources (only if convert is involved)
            var deleteSource = false;
            if (operation.Contains("Convert"))
            {
                deleteSource = AnsiConsole.Confirm(
                    "Delete source files after successful conversion?", 
                    defaultValue: false);
            }
            
            // Build command arguments
            var baseArgs = new List<string> { "--root", root };
            if (recursive)
            {
                baseArgs.Add("--recursive");
            }
            if (flattenMultidisc)
            {
                baseArgs.Add("--flatten-multidisc");
            }
            if (!apply)
            {
                // Dry-run mode - don't add --apply
            }
            else
            {
                baseArgs.Add("--apply");
                baseArgs.Add("--force"); // Skip confirmation since we're doing it here
            }
            
            // Show final confirmation in apply mode
            if (apply)
            {
                AnsiConsole.WriteLine();
                var opsDescription = operation switch
                {
                    var s when s.Contains("Rename then Convert") => "rename and convert",
                    var s when s.Contains("Rename") => "rename",
                    var s when s.Contains("Convert") => "convert",
                    _ => "process"
                };
                
                var warning = deleteSource ? " [red](sources will be deleted)[/]" : "";
                if (!AnsiConsole.Confirm($"Proceed with [bold]{opsDescription}[/] operation{warning}?", false))
                {
                    AnsiConsole.MarkupLine("[yellow]Operation cancelled by user[/]");
                    return 0;
                }
            }
            
            AnsiConsole.WriteLine();
            
            // Execute based on operation choice
            if (operation == "Rename only")
            {
                return PsxRenameCommand.Execute(baseArgs.ToArray());
            }
            else if (operation == "Convert only (BIN/CUE → CHD)")
            {
                var convertArgs = new List<string>(baseArgs);
                if (deleteSource)
                {
                    convertArgs.Add("--delete-source");
                }
                return await PsxConvertCommand.ExecuteAsync(convertArgs.ToArray());
            }
            else // Rename then Convert
            {
                // First rename
                AnsiConsole.MarkupLine("[bold cyan]Step 1: Rename[/]");
                AnsiConsole.WriteLine();
                var renameResult = PsxRenameCommand.Execute(baseArgs.ToArray());
                
                if (renameResult != 0)
                {
                    AnsiConsole.MarkupLine("[red]Rename failed, skipping convert[/]");
                    return renameResult;
                }
                
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold cyan]Step 2: Convert[/]");
                AnsiConsole.WriteLine();
                
                // Then convert
                var convertArgs = new List<string>(baseArgs);
                if (deleteSource)
                {
                    convertArgs.Add("--delete-source");
                }
                return await PsxConvertCommand.ExecuteAsync(convertArgs.ToArray());
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
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
