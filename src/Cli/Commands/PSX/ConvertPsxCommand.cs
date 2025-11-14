using ARK.Cli.Infrastructure;
using ARK.Core.Systems.PSX;
using Spectre.Console;

namespace ARK.Cli.Commands.PSX;

/// <summary>
/// Handles the 'convert psx' command
/// </summary>
public static class ConvertPsxCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrEmpty(root))
        {
            AnsiConsole.MarkupLine("[red]☄️ [IMPACT] | Component: convert psx | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red]☄️ [IMPACT] | Component: convert psx | Context: Directory not found: {root} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var deleteSource = args.Contains("--delete-source");

        if (deleteSource && !apply)
        {
            AnsiConsole.MarkupLine("[red]☄️ [IMPACT] | Component: convert psx | Context: --delete-source requires --apply | Fix: Add --apply flag[/]");
            return (int)ExitCode.InvalidArgs;
        }

        AnsiConsole.MarkupLine("[cyan]🛰️ [[PSX CONVERT]][/] Root: {0}", root.EscapeMarkup());
        if (recursive)
        {
            AnsiConsole.MarkupLine("[dim]  Mode: Recursive[/]");
        }
        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]  DRY RUN (use --apply to execute)[/]");
        }
        if (deleteSource)
        {
            AnsiConsole.MarkupLine("[red]  ⚠️  Delete source files after conversion[/]");
        }
        AnsiConsole.WriteLine();

        var planner = new PsxConvertPlanner();
        var operations = planner.PlanConversions(root, recursive);

        // Statistics
        var alreadyConverted = operations.Count(o => o.AlreadyConverted);
        var toConvert = operations.Count(o => !o.AlreadyConverted);

        // Display table for files to convert
        if (operations.Any())
        {
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Title[/]");
            table.AddColumn("[yellow]Disc[/]");
            table.AddColumn("[green]Status[/]");
            table.AddColumn("[red]Warning[/]");

            foreach (var op in operations)
            {
                var title = op.DiscInfo.Title ?? "Unknown";
                
                // Display disc number properly (never show "-")
                var disc = op.DiscInfo.DiscNumber.HasValue
                    ? $"Disc {op.DiscInfo.DiscNumber}"
                    : "Unknown";

                var status = op.AlreadyConverted
                    ? "[green]EXISTS[/]"
                    : "[yellow]CONVERT[/]";

                var warning = op.Warning != null ? $"⚠️  {op.Warning}" : "";

                table.AddRow(
                    title.EscapeMarkup(),
                    disc,
                    status,
                    warning.EscapeMarkup()
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        // Display summary
        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════[/]");
        AnsiConsole.MarkupLine($"[cyan]Summary:[/]");
        AnsiConsole.MarkupLine($"  Total CUE files: [cyan]{operations.Count}[/]");
        AnsiConsole.MarkupLine($"  Already converted: [green]{alreadyConverted}[/]");
        AnsiConsole.MarkupLine($"  To convert: [yellow]{toConvert}[/]");
        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════[/]");

        // Check for chdman tool
        if (apply && toConvert > 0)
        {
            var chdmanPath = FindChdman();
            if (chdmanPath == null)
            {
                AnsiConsole.MarkupLine("[red]☄️ [IMPACT] | Component: convert psx | Context: chdman.exe not found | Fix: Place chdman.exe in .\\tools\\ directory[/]");
                AnsiConsole.MarkupLine("[yellow]💡 Run 'ark-retro-forge doctor' to check tool status[/]");
                return (int)ExitCode.ToolMissing;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]🔥 [BURN] Converting CUE files to CHD...[/]");

            var converted = 0;
            foreach (var op in operations.Where(o => !o.AlreadyConverted))
            {
                try
                {
                    AnsiConsole.MarkupLine($"[dim]  Converting: {Path.GetFileName(op.SourcePath).EscapeMarkup()}[/]");

                    // Run chdman createcd
                    var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = chdmanPath,
                        Arguments = $"createcd -i \"{op.SourcePath}\" -o \"{op.DestinationPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });

                    if (process != null)
                    {
                        await process.WaitForExitAsync();

                        if (process.ExitCode == 0)
                        {
                            converted++;

                            // Delete source files if requested
                            if (deleteSource)
                            {
                                File.Delete(op.SourcePath);

                                // Also delete associated BIN files
                                var cueDir = Path.GetDirectoryName(op.SourcePath);
                                var cueContent = File.ReadAllText(op.SourcePath);
                                var binFiles = System.Text.RegularExpressions.Regex.Matches(cueContent, @"FILE ""([^""]+)"" BINARY");
                                foreach (System.Text.RegularExpressions.Match match in binFiles)
                                {
                                    var binFile = Path.Combine(cueDir ?? "", match.Groups[1].Value);
                                    if (File.Exists(binFile))
                                    {
                                        File.Delete(binFile);
                                    }
                                }
                            }
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[red]  Error: chdman exited with code {process.ExitCode}[/]");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]  Error converting {Path.GetFileName(op.SourcePath)}: {ex.Message}[/]");
                }
            }

            AnsiConsole.MarkupLine($"[green]✨ [DOCKED] Converted {converted} files[/]");
        }
        else if (!apply && toConvert > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]💡 Next step: Add --apply to execute conversions[/]");
        }

        await Task.CompletedTask;
        return (int)ExitCode.OK;
    }

    private static string? FindChdman()
    {
        var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
        var chdmanPath = Path.Combine(toolsDir, "chdman.exe");

        if (File.Exists(chdmanPath))
        {
            return chdmanPath;
        }

        // Also check in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var path in pathEnv.Split(Path.PathSeparator))
            {
                var fullPath = Path.Combine(path, "chdman.exe");
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
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
