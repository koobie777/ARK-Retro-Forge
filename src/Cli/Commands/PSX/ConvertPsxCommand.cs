using System.Text.RegularExpressions;
using ARK.Cli.Infrastructure;
using ARK.Core.Systems.PSX;
using Spectre.Console;

namespace ARK.Cli.Commands.PSX;

/// <summary>
/// Handles the 'convert psx' command
/// </summary>
public static class ConvertPsxCommand
{
    private static readonly Regex CueFileRegex = new(@"FILE ""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static async Task<int> RunAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrEmpty(root))
        {
            AnsiConsole.MarkupLine("[red]☄️ [[]IMPACT[]] | Component: convert psx | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red]☄️ [[]IMPACT[]] | Component: convert psx | Context: Directory not found: {root} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var deleteSource = args.Contains("--delete-source");
        var rebuild = args.Contains("--rebuild");
        var targetArg = GetArgValue(args, "--to") ?? "chd";

        if (deleteSource && !apply)
        {
            AnsiConsole.MarkupLine("[red]☄️ [[]IMPACT[]] | Component: convert psx | Context: --delete-source requires --apply | Fix: Add --apply flag[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!TryParseTarget(targetArg, out var target))
        {
            AnsiConsole.MarkupLine($"[red]☄️ Invalid --to value '{targetArg}'. Use chd, bin, or iso.[/]");
            return (int)ExitCode.InvalidArgs;
        }

        AnsiConsole.MarkupLine("[cyan]🛰️ [[PSX CONVERT]][/] Root: {0}", root.EscapeMarkup());
        AnsiConsole.MarkupLine($"[dim]  Mode: {(recursive ? "Recursive" : "Top-level only")}[/]");
        AnsiConsole.MarkupLine($"[dim]  Target: {target.ToString().ToUpperInvariant()}[/]");
        if (rebuild)
        {
            AnsiConsole.MarkupLine("[yellow]  Rebuild: Force reconversion even if destination exists[/]");
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
        var operations = planner.PlanConversions(root, recursive, rebuild, target);

        if (operations.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No files found for conversion with the requested options.[/]");
            return (int)ExitCode.OK;
        }

        RenderPlanTable(operations, target);

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]💡 Next step: Add --apply to execute conversions[/]");
            return (int)ExitCode.OK;
        }

        var chdmanPath = FindChdman();
        if (chdmanPath == null)
        {
            AnsiConsole.MarkupLine("[red]☄️ [[]IMPACT[]] | Component: convert psx | Context: chdman.exe not found | Fix: Place chdman.exe in .\\tools\\ directory[/]");
            AnsiConsole.MarkupLine("[yellow]💡 Run 'ark-retro-forge doctor' to check tool status[/]");
            return (int)ExitCode.ToolMissing;
        }

        var converted = await ExecuteConversionsAsync(operations, chdmanPath, target, deleteSource);
        AnsiConsole.MarkupLine($"[green]✨ [DOCKED] Converted {converted} file(s)[/]");
        return (int)ExitCode.OK;
    }

    private static void RenderPlanTable(IEnumerable<PsxConvertOperation> operations, PsxConversionTarget target)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[cyan]Source[/]");
        table.AddColumn("[green]Destination[/]");
        table.AddColumn("[yellow]Media[/]");
        table.AddColumn("[magenta]Status[/]");
        table.AddColumn("[red]Warning[/]");

        foreach (var op in operations)
        {
        var destinationDisplay = target switch
            {
                PsxConversionTarget.Chd => op.DestinationPath ?? "-",
                PsxConversionTarget.BinCue => op.DestinationCuePath ?? "-",
                PsxConversionTarget.Iso => op.DestinationPath ?? "-",
                _ => "-"
            };

            table.AddRow(
                Path.GetFileName(op.SourcePath).EscapeMarkup(),
                Path.GetFileName(destinationDisplay ?? "-")?.EscapeMarkup() ?? "-",
                op.MediaType.ToString(),
                op.AlreadyConverted ? "[green]Ready[/]" : "[yellow]Pending[/]",
                (op.Warning ?? string.Empty).EscapeMarkup());
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static async Task<int> ExecuteConversionsAsync(IEnumerable<PsxConvertOperation> operations, string chdmanPath, PsxConversionTarget target, bool deleteSource)
    {
        var converted = 0;
        foreach (var op in operations.Where(o => !o.AlreadyConverted))
        {
            try
            {
                var arguments = BuildChdmanArguments(op, target);
                if (string.IsNullOrWhiteSpace(arguments))
                {
                    AnsiConsole.MarkupLine($"[red]  Skipping {Path.GetFileName(op.SourcePath)}: unsupported media type/target[/]");
                    continue;
                }

                AnsiConsole.MarkupLine($"[dim]  Converting: {Path.GetFileName(op.SourcePath).EscapeMarkup()}[/]");
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = chdmanPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    AnsiConsole.MarkupLine($"[red]  Failed to start chdman for {Path.GetFileName(op.SourcePath)}[/]");
                    continue;
                }

                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    converted++;
                    if (deleteSource)
                    {
                        DeleteOriginals(op, target);
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]  chdman exited with code {process.ExitCode}[/]");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]  Error converting {Path.GetFileName(op.SourcePath)}: {ex.Message}[/]");
            }
        }

        return converted;
    }

    private static string BuildChdmanArguments(PsxConvertOperation op, PsxConversionTarget target)
    {
        return target switch
        {
            PsxConversionTarget.Chd when !string.IsNullOrWhiteSpace(op.DestinationPath) =>
                $"{ChdMediaTypeHelper.GetChdmanCommand(op.MediaType)} -i \"{op.SourcePath}\" -o \"{op.DestinationPath}\"",
            PsxConversionTarget.BinCue when !string.IsNullOrWhiteSpace(op.DestinationCuePath) && !string.IsNullOrWhiteSpace(op.DestinationBinPath) =>
                $"{ChdMediaTypeHelper.GetExtractCommand(op.MediaType)} -i \"{op.SourcePath}\" -o \"{op.DestinationCuePath}\" -ob \"{op.DestinationBinPath}\"",
            PsxConversionTarget.Iso when !string.IsNullOrWhiteSpace(op.DestinationPath) =>
                $"{ChdMediaTypeHelper.GetExtractCommand(op.MediaType)} -i \"{op.SourcePath}\" -o \"{op.DestinationPath}\"",
            _ => string.Empty
        };
    }

    private static void DeleteOriginals(PsxConvertOperation op, PsxConversionTarget target)
    {
        try
        {
            switch (target)
            {
                case PsxConversionTarget.Chd:
                    DeleteCueAndBins(op.SourcePath);
                    break;
                case PsxConversionTarget.BinCue:
                case PsxConversionTarget.Iso:
                    if (File.Exists(op.SourcePath))
                    {
                        File.Delete(op.SourcePath);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]  Failed to delete source: {ex.Message}[/]");
        }
    }

    private static void DeleteCueAndBins(string cuePath)
    {
        if (!File.Exists(cuePath))
        {
            return;
        }

        var content = File.ReadAllText(cuePath);
        File.Delete(cuePath);

        var matches = CueFileRegex.Matches(content);
        var cueDir = Path.GetDirectoryName(cuePath) ?? string.Empty;
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var binPath = Path.Combine(cueDir, match.Groups[1].Value);
            if (File.Exists(binPath))
            {
                File.Delete(binPath);
            }
        }
    }

    private static bool TryParseTarget(string value, out PsxConversionTarget target)
    {
        switch (value.ToLowerInvariant())
        {
            case "chd":
                target = PsxConversionTarget.Chd;
                return true;
            case "bin":
            case "cue":
            case "bin-cue":
                target = PsxConversionTarget.BinCue;
                return true;
            case "iso":
                target = PsxConversionTarget.Iso;
                return true;
            default:
                target = PsxConversionTarget.Chd;
                return false;
        }
    }

    private static string? FindChdman()
    {
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
        var chdmanPath = Path.Combine(toolsDir, "chdman.exe");
        if (File.Exists(chdmanPath))
        {
            return chdmanPath;
        }

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
