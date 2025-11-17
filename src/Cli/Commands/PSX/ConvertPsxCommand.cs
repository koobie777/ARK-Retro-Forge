using System.Diagnostics;
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
            AnsiConsole.MarkupLine("[red]☄️ [[IMPACT]] | Component: convert psx | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red]☄️ [[IMPACT]] | Component: convert psx | Context: Directory not found: {root} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var deleteSource = args.Contains("--delete-source");
        var rebuild = args.Contains("--rebuild");
        var targetArg = GetArgValue(args, "--to") ?? "chd";

        if (deleteSource && !apply)
        {
            AnsiConsole.MarkupLine("[red]☄️ [[IMPACT]] | Component: convert psx | Context: --delete-source requires --apply | Fix: Add --apply flag[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!TryParseTarget(targetArg, out var target))
        {
            AnsiConsole.MarkupLine($"[red]☄️ Invalid --to value '{targetArg}'. Use chd, bin, or iso.[/]");
            return (int)ExitCode.InvalidArgs;
        }

        DatUsageHelper.WarnIfCatalogMissing("psx", "PSX convert");

        ConsoleDecorations.RenderOperationHeader(
            "PSX Convert",
            new ConsoleDecorations.HeaderMetadata("Root", root),
            new ConsoleDecorations.HeaderMetadata("Scope", recursive ? "Recursive" : "Top-level"),
            new ConsoleDecorations.HeaderMetadata("Target", target.ToString().ToUpperInvariant()),
            new ConsoleDecorations.HeaderMetadata("Mode", apply ? "[green]APPLY[/]" : "[yellow]DRY-RUN[/]", IsMarkup: true),
            new ConsoleDecorations.HeaderMetadata("Rebuild", rebuild ? "Yes" : "No"),
            new ConsoleDecorations.HeaderMetadata("Delete source", deleteSource ? "[red]Yes[/]" : "No", IsMarkup: deleteSource));
        AnsiConsole.WriteLine();

        var planner = new PsxConvertPlanner();
        var operations = planner.PlanConversions(root, recursive, rebuild, target);

        if (operations.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No files found for conversion with the requested options.[/]");
            return (int)ExitCode.OK;
        }

        RenderQueueSummary(operations);
        RenderPlanTable(operations, target);

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]💡 Next step: Add --apply to execute conversions[/]");
            return (int)ExitCode.OK;
        }

        var chdmanPath = FindChdman();
        if (chdmanPath == null)
        {
            AnsiConsole.MarkupLine("[red]☄️ [[IMPACT]] | Component: convert psx | Context: chdman.exe not found | Fix: Place chdman.exe in .\\tools\\ directory[/]");
            AnsiConsole.MarkupLine("[yellow]💡 Run 'ark-retro-forge doctor' to check tool status[/]");
            return (int)ExitCode.ToolMissing;
        }

        var summary = await ExecuteConversionsAsync(operations, chdmanPath, target, deleteSource);
        RenderConversionSummary(summary);
        return summary.Failures.Count > 0 ? (int)ExitCode.GeneralError : (int)ExitCode.OK;
    }

    private static void RenderPlanTable(IEnumerable<PsxConvertOperation> operations, PsxConversionTarget target)
    {
        const int MaxRows = 150;
        var opList = operations.ToList();
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[cyan]Source[/]");
        table.AddColumn("[green]Destination[/]");
        table.AddColumn("[yellow]Media[/]");
        table.AddColumn("[magenta]Status[/]");
        table.AddColumn("[red]Warning[/]");

        foreach (var op in opList.Take(MaxRows))
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
        if (opList.Count > MaxRows)
        {
            AnsiConsole.MarkupLine($"[dim]Showing first {MaxRows} of {opList.Count:N0} planned conversions...[/]");
        }
        AnsiConsole.WriteLine();
    }

    private static async Task<ConversionSummary> ExecuteConversionsAsync(
        IEnumerable<PsxConvertOperation> operations,
        string chdmanPath,
        PsxConversionTarget target,
        bool deleteSource)
    {
        var opList = operations.ToList();
        var pending = opList.Where(o => !o.AlreadyConverted).ToList();
        var failures = new List<ConversionResult>();
        var converted = 0;
        var token = OperationContextScope.CurrentToken;

        if (pending.Count == 0)
        {
            return new ConversionSummary(converted, opList.Count, failures);
        }

        var progressColumns = new ProgressColumn[]
        {
            new TaskDescriptionColumn(),
            new ProgressBarColumn { Width = 50, CompletedStyle = new Style(Color.SpringGreen1), RemainingStyle = new Style(Color.Grey35) },
            new PercentageColumn(),
            new RemainingTimeColumn(),
            new SpinnerColumn()
        };

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(progressColumns)
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Converting PSX images", maxValue: pending.Count);
                foreach (var op in pending)
                {
                    OperationContextScope.ThrowIfCancellationRequested();
                    task.Description = $"Converting {FormatTaskLabel(op.SourcePath)}";

                    var result = await RunConversionAsync(op, chdmanPath, target, deleteSource, token);
                    if (result.Success)
                    {
                        converted++;
                    }
                    else
                    {
                        failures.Add(result);
                    }

                    task.Increment(1);
                }
            });

        return new ConversionSummary(converted, opList.Count - pending.Count, failures);
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
        OperationContextScope.ThrowIfCancellationRequested();
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

    private static void CleanupPartialConversion(PsxConvertOperation op, PsxConversionTarget target)
    {
        try
        {
            switch (target)
            {
                case PsxConversionTarget.Chd when !string.IsNullOrWhiteSpace(op.DestinationPath):
                    if (File.Exists(op.DestinationPath))
                    {
                        File.Delete(op.DestinationPath);
                    }
                    break;
                case PsxConversionTarget.BinCue:
                    if (!string.IsNullOrWhiteSpace(op.DestinationCuePath) && File.Exists(op.DestinationCuePath))
                    {
                        File.Delete(op.DestinationCuePath);
                    }
                    if (!string.IsNullOrWhiteSpace(op.DestinationBinPath) && File.Exists(op.DestinationBinPath))
                    {
                        File.Delete(op.DestinationBinPath);
                    }
                    break;
                case PsxConversionTarget.Iso when !string.IsNullOrWhiteSpace(op.DestinationPath):
                    if (File.Exists(op.DestinationPath))
                    {
                        File.Delete(op.DestinationPath);
                    }
                    break;
            }
        }
        catch
        {
            // best effort cleanup
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
            // ignore
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

    private static string FormatTaskLabel(string path, int maxLength = 48)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name))
        {
            return "PSX";
        }

        if (name.Length <= maxLength)
        {
            return name.EscapeMarkup();
        }

        return $"{name[..Math.Max(1, maxLength - 3)].EscapeMarkup()}...";
    }

    private static string? ExtractErrorMessage(string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return null;
        }

        foreach (var raw in stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = raw.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }
        }

        return null;
    }

    private static void RenderQueueSummary(IReadOnlyList<PsxConvertOperation> operations)
    {
        if (operations.Count == 0)
        {
            return;
        }

        var ready = operations.Count(op => op.AlreadyConverted);
        var pending = operations.Count - ready;
        var multiDisc = operations.Count(op => (op.DiscInfo.DiscCount ?? 1) > 1);
        var warnings = operations.Count(op => !string.IsNullOrWhiteSpace(op.Warning));

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[cyan]Metric[/]");
        table.AddColumn("[green]Value[/]");
        table.AddRow("Total images", operations.Count.ToString("N0"));
        table.AddRow("Pending", pending.ToString("N0"));
        table.AddRow("Ready/skipped", ready.ToString("N0"));
        table.AddRow("Multi-disc", multiDisc.ToString("N0"));
        table.AddRow("Warnings", warnings.ToString("N0"));
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static void RenderConversionSummary(ConversionSummary summary)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[cyan]Metric[/]");
        table.AddColumn("[green]Value[/]");
        table.AddRow("Converted", summary.Converted.ToString("N0"));
        table.AddRow("Skipped", summary.Skipped.ToString("N0"));
        table.AddRow("Failures", summary.Failures.Count.ToString("N0"));
        AnsiConsole.Write(table);

        if (summary.Failures.Count > 0)
        {
            var list = string.Join(
                Environment.NewLine,
                summary.Failures.Select(f =>
                {
                    var name = Path.GetFileName(f.Operation.SourcePath).EscapeMarkup();
                    var error = string.IsNullOrWhiteSpace(f.ErrorMessage)
                        ? $"Exit {f.ExitCode}"
                        : f.ErrorMessage.EscapeMarkup();
                    return $"- {name}: {error}";
                }));

            var panel = new Panel(new Markup(list))
            {
                Header = new PanelHeader($"[red]Failed ({summary.Failures.Count})[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };
            AnsiConsole.WriteLine();
            AnsiConsole.Write(panel);
        }
    }

    private sealed record ConversionResult(
        PsxConvertOperation Operation,
        bool Success,
        int ExitCode,
        string? ErrorMessage,
        string? Details)
    {
        public static ConversionResult Successful(PsxConvertOperation operation)
            => new(operation, true, 0, null, null);

        public static ConversionResult Failed(PsxConvertOperation operation, int exitCode, string? message, string? details = null)
            => new(operation, false, exitCode, message, details);
    }

    private sealed record ConversionSummary(int Converted, int Skipped, IReadOnlyList<ConversionResult> Failures);

    private static async Task<ConversionResult> RunConversionAsync(
        PsxConvertOperation operation,
        string chdmanPath,
        PsxConversionTarget target,
        bool deleteSource,
        CancellationToken token)
    {
        var arguments = BuildChdmanArguments(operation, target);
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return ConversionResult.Failed(operation, -1, "Unsupported media type/target combination");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = chdmanPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return ConversionResult.Failed(operation, -1, "Failed to start chdman");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var registration = token.Register(() => TryKillProcess(process));
            try
            {
                await process.WaitForExitAsync(token);
            }
            catch (OperationCanceledException)
            {
                TryKillProcess(process);
                CleanupPartialConversion(operation, target);
                throw;
            }
            finally
            {
                registration.Dispose();
            }

            var stderr = await stderrTask;
            await stdoutTask;

            if (process.ExitCode == 0)
            {
                if (deleteSource)
                {
                    DeleteOriginals(operation, target);
                }

                return ConversionResult.Successful(operation);
            }

            CleanupPartialConversion(operation, target);
            var error = ExtractErrorMessage(stderr) ?? $"chdman exited with code {process.ExitCode}";
            return ConversionResult.Failed(operation, process.ExitCode, error, stderr);
        }
        catch (OperationCanceledException)
        {
            CleanupPartialConversion(operation, target);
            throw;
        }
        catch (Exception ex)
        {
            CleanupPartialConversion(operation, target);
            return ConversionResult.Failed(operation, -1, ex.Message);
        }
    }
}
