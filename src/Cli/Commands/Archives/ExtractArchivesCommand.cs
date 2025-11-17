using ARK.Cli.Infrastructure;
using ARK.Cli.Infrastructure.Progress;
using ARK.Core.Archives;
using Spectre.Console;

namespace ARK.Cli.Commands.Archives;

public static class ExtractArchivesCommand
{
    private static readonly string[] SupportedExtensions = { ".zip", ".7z", ".7zip", ".rar" };

    public static async Task<int> RunAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrWhiteSpace(root))
        {
            AnsiConsole.MarkupLine("[red][[IMPACT]] | Component: extract archives | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red][[IMPACT]] | Component: extract archives | Context: Directory not found: {root} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var output = GetArgValue(args, "--output") ?? root;
        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var deleteSource = args.Contains("--delete-source");

        if (deleteSource && !apply)
        {
            AnsiConsole.MarkupLine("[red][[IMPACT]] | Component: extract archives | Context: --delete-source requires --apply | Fix: Add --apply[/]");
            return (int)ExitCode.InvalidArgs;
        }

        AnsiConsole.MarkupLine("[cyan][[ARCHIVE EXTRACT]] Root:[/] {0}", root.EscapeMarkup());
        AnsiConsole.MarkupLine($"[dim]  Output: {output.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"[dim]  Mode: {(recursive ? "Recursive" : "Top-level only")}[/]");
        AnsiConsole.MarkupLine(apply ? "[yellow]  Apply mode enabled[/]" : "[yellow]  DRY RUN (use --apply to extract)[/]");
        AnsiConsole.WriteLine();

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        List<string> archivePaths = new();
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("Scanning for archives...", _ =>
            {
                archivePaths = Directory.GetFiles(root, "*.*", searchOption)
                    .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(path => path)
                    .ToList();
            });

        if (archivePaths.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No ZIP/7Z/RAR archives found[/]");
            return (int)ExitCode.OK;
        }

        var plans = archivePaths.Select(path => CreatePlan(path, output)).ToList();
        var totalBytes = RenderArchiveQueueSummary(plans, deleteSource);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[cyan]Archive[/]");
        table.AddColumn("[yellow]Destination[/]");

        foreach (var plan in plans)
        {
            table.AddRow(
                Path.GetFileName(plan.ArchivePath).EscapeMarkup(),
                plan.DestinationPath.EscapeMarkup());
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[cyan]Ready to process:[/] {archivePaths.Count} archive(s)");
        AnsiConsole.MarkupLine("[dim]Press ESC, B, or Ctrl+C to cancel the active archive (completed items remain).[/]");
        AnsiConsole.WriteLine();

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]Next step: Add --apply to extract[/]");
            return (int)ExitCode.OK;
        }

        AnsiConsole.Clear();
        AnsiConsole.Write(CreateExtractionHeader(root, output, recursive, deleteSource, plans.Count, totalBytes));
        AnsiConsole.MarkupLine("[dim]Press ESC, B, or Ctrl+C to cancel the active archive (completed items remain).[/]");
        AnsiConsole.WriteLine();

        var extracted = 0;
        var cancelled = false;
        string? cancelledArchive = null;
        var failures = new List<string>();

        using var extractionCts = CancellationTokenSource.CreateLinkedTokenSource(OperationContextScope.CurrentToken);
        var extractionToken = extractionCts.Token;
        var keyMonitor = MonitorCancellationKeyAsync(extractionCts);

        var progressColumns = new ProgressColumn[]
        {
            new TaskDescriptionColumn(),
            new ProgressBarColumn
            {
                Width = 48,
                CompletedStyle = new Style(Color.SpringGreen1),
                RemainingStyle = new Style(Color.Grey35)
            },
            new PercentageColumn(),
            new RemainingTimeColumn(),
            new ArchiveStatsColumn(),
            new SpinnerColumn()
        };

        AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(progressColumns)
            .Start(ctx =>
            {
                var progressTask = ctx.AddTask("Extracting archives", maxValue: plans.Count);
                var progressState = new ArchiveProgressState(plans.Count);
                SyncProgressState(progressTask, progressState);

                for (var index = 0; index < plans.Count; index++)
                {
                    if (extractionToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    var plan = plans[index];
                    var destination = plan.DestinationPath;
                    var archiveName = Path.GetFileName(plan.ArchivePath);

                    progressState.CurrentArchive = archiveName;
                    progressState.CurrentIndex = index + 1;
                    SyncProgressState(progressTask, progressState);
                    progressTask.Description = BuildTaskDescription(progressState);

                    var prepFailed = false;
                    if (plan.RequiresSubdirectory && Directory.Exists(destination))
                    {
                        try
                        {
                            Directory.Delete(destination, recursive: true);
                            AnsiConsole.MarkupLine($"[dim]  Overwriting existing directory: {destination.EscapeMarkup()}[/]");
                        }
                        catch (Exception ex)
                        {
                            prepFailed = true;
                            var error = ex.Message.EscapeMarkup();
                            failures.Add($"{archiveName}: failed to clean destination ({ex.Message})");
                            AnsiConsole.MarkupLine($"[red]  Failed to clean destination {destination.EscapeMarkup()}: {error}[/]");
                        }
                    }

                    if (prepFailed)
                    {
                        progressState.Failed = failures.Count;
                        SyncProgressState(progressTask, progressState);
                        progressTask.Increment(1);
                        continue;
                    }

                    try
                    {
                        var result = ArchiveExtractor.Extract(plan.ArchivePath, destination, extractionToken);
                        if (result.Success)
                        {
                            extracted++;
                            progressState.Completed = extracted;
                            SyncProgressState(progressTask, progressState);
                            if (deleteSource)
                            {
                                extractionToken.ThrowIfCancellationRequested();
                                File.Delete(plan.ArchivePath);
                            }
                        }
                        else
                        {
                            var error = string.IsNullOrWhiteSpace(result.Error) ? "Unknown error" : result.Error!;
                            failures.Add($"{archiveName}: {error}");
                            progressState.Failed = failures.Count;
                            SyncProgressState(progressTask, progressState);
                            AnsiConsole.MarkupLine($"[red]  Error extracting {archiveName.EscapeMarkup()}: {error.EscapeMarkup()}[/]");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        progressState.CancelRequested = true;
                        SyncProgressState(progressTask, progressState);
                        cancelledArchive = plan.ArchivePath;
                        SafeDeleteDirectory(destination);
                        break;
                    }

                    progressTask.Increment(1);
                }
            });

        extractionCts.Cancel();
        await keyMonitor;

        if (cancelled)
        {
            var rolledBack = cancelledArchive != null ? Path.GetFileName(cancelledArchive) : "current archive";
            AnsiConsole.MarkupLine($"[yellow]Extraction cancelled. Rolled back: {rolledBack.EscapeMarkup()}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]✔ Extracted {extracted} archive(s)[/]");
            if (deleteSource)
            {
                AnsiConsole.MarkupLine("[dim]  Source archives deleted after extraction[/]");
            }
        }

        if (failures.Count > 0)
        {
            RenderFailureSummary(failures);
        }

        await Task.CompletedTask;
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

    private static ArchivePlan CreatePlan(string archivePath, string outputRoot)
    {
        var requiresSubdirectory = ArchiveExtractor.RequiresSubdirectory(archivePath);
        var destination = requiresSubdirectory
            ? Path.Combine(outputRoot, SanitizeName(Path.GetFileNameWithoutExtension(archivePath)))
            : outputRoot;
        var size = 0L;
        try
        {
            var info = new FileInfo(archivePath);
            size = info.Length;
        }
        catch
        {
            // Ignore IO exceptions when sizing preview data.
        }
        return new ArchivePlan(archivePath, destination, requiresSubdirectory, size);
    }

    private static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "archive";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            builder[i] = invalid.Contains(ch) ? '_' : ch;
        }

        return new string(builder).Trim();
    }

    private static long RenderArchiveQueueSummary(IReadOnlyCollection<ArchivePlan> plans, bool deleteSource)
    {
        var totalBytes = plans.Sum(plan => plan.ArchiveSize);

        var summary = new Table().Border(TableBorder.Rounded);
        summary.AddColumn("[cyan]Metric[/]");
        summary.AddColumn("[green]Value[/]");
        summary.AddRow("Archives", plans.Count.ToString("N0"));
        summary.AddRow("Total size", FormatSize(totalBytes));
        summary.AddRow("Delete source", deleteSource ? "Yes" : "No");
        AnsiConsole.Write(summary);
        AnsiConsole.WriteLine();

        var extensionSummary = plans
            .GroupBy(plan => Path.GetExtension(plan.ArchivePath).ToLowerInvariant())
            .Select(group => new
            {
                Extension = string.IsNullOrWhiteSpace(group.Key) ? "(none)" : group.Key,
                Count = group.Count(),
                Bytes = group.Sum(p => p.ArchiveSize)
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.Bytes)
            .Take(6)
            .ToList();

        if (extensionSummary.Count > 0)
        {
            var extTable = new Table { Title = new TableTitle("[cyan]By extension[/]") };
            extTable.Border(TableBorder.Rounded);
            extTable.AddColumn("Ext");
            extTable.AddColumn("Count");
            extTable.AddColumn("Size");
            foreach (var stat in extensionSummary)
            {
                extTable.AddRow(stat.Extension, stat.Count.ToString("N0"), FormatSize(stat.Bytes));
            }

            AnsiConsole.Write(extTable);
            AnsiConsole.WriteLine();
        }

        return totalBytes;
    }

    private static async Task MonitorCancellationKeyAsync(CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    var cancelRequested = key.Key == ConsoleKey.Escape ||
                        key.Key == ConsoleKey.B ||
                        (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.C);
                    if (cancelRequested)
                    {
                        var reason = key.Key == ConsoleKey.B ? "B" : "ESC/Ctrl+C";
                        AnsiConsole.MarkupLine($"\n[yellow]Cancellation requested ({reason}). Rolling back current archive...[/]");
                        cts.Cancel();
                        return;
                    }
                }

                await Task.Delay(75, cts.Token);
            }
        }
        catch (TaskCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
            // Input redirected (CI). Ignore hotkeys.
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var order = (int)Math.Floor(Math.Log(bytes, 1024));
        order = Math.Clamp(order, 0, units.Length - 1);
        var value = bytes / Math.Pow(1024, order);
        return $"{value:F2} {units[order]}";
    }

    private static void SafeDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]  Failed to rollback directory {path.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
        }
    }

    private static string BuildTaskDescription(ArchiveProgressState state)
    {
        var prefix = $"[steelblue]{state.CurrentIndex}/{state.Total}[/]";
        var label = TruncateArchiveName(state.CurrentArchive);
        return $"{prefix} {label}";
    }

    private static string TruncateArchiveName(string name, int maxLength = 60)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "archive";
        }

        if (name.Length <= maxLength)
        {
            return name.EscapeMarkup();
        }

        var head = Math.Max(maxLength - 3, 1);
        return $"{name[..head].EscapeMarkup()}...";
    }

    private static Panel CreateExtractionHeader(string root, string output, bool recursive, bool deleteSource, int total, long totalBytes)
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow(
            new Markup($"[grey50]Root[/]\n{root.EscapeMarkup()}"),
            new Markup($"[grey50]Output[/]\n{output.EscapeMarkup()}"));
        grid.AddRow(
            new Markup($"[grey50]Scope[/]\n{(recursive ? "Recursive" : "Top-level only")}"),
            new Markup($"[grey50]Delete source[/]\n{(deleteSource ? "Yes" : "No")}"));
        grid.AddRow(
            new Markup($"[grey50]Queue[/]\n{total} archive(s)"),
            new Markup($"[grey50]Total size[/]\n{FormatSize(totalBytes)}"));
        grid.AddRow(
            new Markup($"[grey50]Started[/]\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}"),
            new Markup($"[grey50]Cancel[/]\nESC/B/Ctrl+C"));

        return new Panel(grid)
        {
            Border = BoxBorder.Double,
            Header = new PanelHeader("[cyan]ARCHIVE EXTRACTION[/]"),
            Padding = new Padding(1, 1)
        };
    }

    private static void RenderFailureSummary(List<string> failures)
    {
        var content = string.Join(
            Environment.NewLine,
            failures.Select(f => $"- {f.EscapeMarkup()}"));

        var panel = new Panel(new Markup(content))
        {
            Header = new PanelHeader($"[red]Failed ({failures.Count})[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0)
        };

        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);
    }

    private static void SyncProgressState(ProgressTask progressTask, ArchiveProgressState state)
    {
        progressTask.State.Update<ArchiveProgressState>(ArchiveProgressState.StateKey, _ => state);
    }

    private sealed record ArchivePlan(string ArchivePath, string DestinationPath, bool RequiresSubdirectory, long ArchiveSize);
}
