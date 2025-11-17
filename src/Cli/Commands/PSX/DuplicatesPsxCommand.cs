using System.Diagnostics.CodeAnalysis;
using ARK.Cli.Infrastructure;
using ARK.Core.Systems.PSX;
using Spectre.Console;
using System.Text.Json;

namespace ARK.Cli.Commands.PSX;

/// <summary>
/// Handles the 'duplicates psx' command
/// </summary>
public static class DuplicatesPsxCommand
{
    [RequiresUnreferencedCode("JSON output relies on System.Text.Json without source generation when trimming.")]
    public static async Task<int> RunAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrEmpty(root))
        {
            AnsiConsole.MarkupLine("[red]☄️ [[]IMPACT[]] | Component: duplicates psx | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red]☄️ [[]IMPACT[]] | Component: duplicates psx | Context: Directory not found: {root} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = args.Contains("--recursive");
        var json = args.Contains("--json");
        var hashAlgorithm = GetArgValue(args, "--hash") ?? "SHA1";

        AnsiConsole.MarkupLine("[cyan]🛰️ [[PSX DUPLICATES]][/] Root: {0}", root.EscapeMarkup());
        if (recursive)
        {
            AnsiConsole.MarkupLine("[dim]  Mode: Recursive[/]");
        }
        AnsiConsole.MarkupLine($"[dim]  Hash: {hashAlgorithm}[/]");
        AnsiConsole.WriteLine();

        var detector = new PsxDuplicateDetector();
        
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("[yellow]Scanning and hashing files...[/]", ctx =>
            {
                // This will run synchronously within the status context
            });

        var duplicateGroups = detector.ScanForDuplicates(root, recursive, hashAlgorithm);

        if (duplicateGroups.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]✨ [DOCKED] No duplicates found[/]");
            await Task.CompletedTask;
            return (int)ExitCode.OK;
        }

        // Display results
        if (json)
        {
            // JSON output
            var jsonOutput = JsonSerializer.Serialize(duplicateGroups, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            // Write to logs directory
            var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            Directory.CreateDirectory(logsDir);
            var jsonPath = Path.Combine(logsDir, $"psx-duplicates-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(jsonPath, jsonOutput);
            
            AnsiConsole.MarkupLine($"[green]✨ [DOCKED] Duplicate report written to: {jsonPath.EscapeMarkup()}[/]");
        }
        else
        {
            // Table output
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Title[/]");
            table.AddColumn("[yellow]Serial[/]");
            table.AddColumn("[magenta]Disc[/]");
            table.AddColumn("[red]Hash[/]");
            table.AddColumn("[green]Count[/]");

            foreach (var group in duplicateGroups)
            {
                var title = group.Title ?? "Unknown";
                var serial = group.Serial ?? "N/A";
                var disc = group.DiscNumber.HasValue ? $"Disc {group.DiscNumber}" : "-";
                var hash = group.Hash.Length > 16 ? group.Hash[..16] + "..." : group.Hash;
                var count = group.Files.Count;

                table.AddRow(
                    title.EscapeMarkup(),
                    serial.EscapeMarkup(),
                    disc,
                    hash,
                    count.ToString()
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            // Show details for first few groups
            var showDetailCount = Math.Min(5, duplicateGroups.Count);
            AnsiConsole.MarkupLine($"[yellow]⚠️  Showing details for first {showDetailCount} duplicate groups:[/]");
            
            foreach (var group in duplicateGroups.Take(showDetailCount))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[bold cyan]{group.Title ?? "Unknown"}[/] - Hash: {group.Hash[..12]}...");
                
                foreach (var file in group.Files)
                {
                    var relativePath = Path.GetRelativePath(root, file.FilePath);
                    var sizeKB = file.FileSize / 1024.0;
                    AnsiConsole.MarkupLine($"  [dim]{relativePath.EscapeMarkup()} ({sizeKB:F2} KB)[/]");
                }
            }

            if (duplicateGroups.Count > showDetailCount)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[dim]... and {duplicateGroups.Count - showDetailCount} more duplicate groups (use --json for full report)[/]");
            }
        }

        // Display summary
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════[/]");
        AnsiConsole.MarkupLine($"[cyan]Summary:[/]");
        AnsiConsole.MarkupLine($"  Duplicate groups: [red]{duplicateGroups.Count}[/]");
        
        var totalDuplicateFiles = duplicateGroups.Sum(g => g.Files.Count);
        var wastedFiles = totalDuplicateFiles - duplicateGroups.Count; // Each group should only have 1 file
        AnsiConsole.MarkupLine($"  Total duplicate files: [red]{totalDuplicateFiles}[/]");
        AnsiConsole.MarkupLine($"  Files that could be removed: [yellow]{wastedFiles}[/]");
        
        var wastedBytes = duplicateGroups.Sum(g => g.Files.Skip(1).Sum(f => f.FileSize));
        var wastedMB = wastedBytes / 1024.0 / 1024.0;
        AnsiConsole.MarkupLine($"  Wasted space: [yellow]{wastedMB:F2} MB[/]");
        AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════════[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]💡 Review duplicates carefully before deleting any files[/]");
        if (!json)
        {
            AnsiConsole.MarkupLine("[yellow]💡 Use --json to generate a detailed report in logs/ directory[/]");
        }

        await Task.CompletedTask;
        return (int)ExitCode.OK;
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
