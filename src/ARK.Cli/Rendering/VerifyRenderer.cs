using ARK.Core.Hashing;
using ARK.Core.Verification;
using Spectre.Console;

namespace ARK.Cli.Rendering;

/// <summary>Renders a <see cref="VerificationReport"/>. Rendering only — it decides nothing.</summary>
public static class VerifyRenderer
{
    private const int PreviewRows = 15;

    /// <summary>
    /// Runs a verification pass behind a live progress display. A full pass over a reference-sized
    /// drive takes hours, and a run indistinguishable from hung is a run users kill.
    /// </summary>
    public static VerificationReport WithProgress(
        IAnsiConsole console,
        Func<IProgress<VerificationProgress>, VerificationReport> run)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(run);

        VerificationReport? report = null;

        console.Progress()
            .AutoClear(true)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .Start(context =>
            {
                var task = context.AddTask("Hashing", maxValue: 1);
                var progress = new Progress<VerificationProgress>(update =>
                {
                    task.MaxValue = Math.Max(update.Total, 1);
                    task.Value = update.Completed;
                    task.Description = update.CurrentName.Length == 0
                        ? "Hashing"
                        : $"Hashing {Truncate(update.CurrentName, 48)}";
                });

                report = run(progress);
                task.Value = task.MaxValue;
            });

        return report!;
    }

    /// <summary>Writes the state counts, the pivots, and the findings.</summary>
    public static void Render(IAnsiConsole console, VerificationReport report, bool listAll)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(report);

        console.MarkupLineInterpolated($"[bold]Verify[/] {report.Root}");
        console.MarkupLineInterpolated(
            $"[grey]{report.Units.Count} unit(s) — read-only, nothing written. {report.Hashed} hashed, {report.CacheHits} from cache, {Bytes(report.BytesHashed)} read in {report.Elapsed:hh\\:mm\\:ss} ({Bytes((long)report.BytesPerSecond)}/s).[/]");

        if (report.Cancelled)
        {
            console.MarkupLine("[yellow]Run was cancelled. Every hash computed before the interruption is cached and will not be recomputed.[/]");
        }

        console.WriteLine();

        RenderStates(console, report);
        RenderPivots(console, report);
        RenderMismatched(console, report, listAll);
        RenderInProgress(console, report);
        RenderDiscSets(console, report, listAll);
        RenderFormatContradictions(console, report);
    }

    // A set is only as complete as its worst disc. Reporting Disc 1 Verified and Disc 2 Mismatched
    // as two unrelated rows tells the truth about the files and hides the fact that the game is
    // unplayable — and the disc that fails is usually the one reached hours in.
    private static void RenderDiscSets(IAnsiConsole console, VerificationReport report, bool listAll)
    {
        if (report.Sets.Count == 0)
        {
            return;
        }

        var incomplete = report.IncompleteSets;
        console.MarkupLineInterpolated(
            $"[bold]Multi-disc sets[/] — {report.Sets.Count - incomplete.Count} complete, {incomplete.Count} incomplete");

        if (incomplete.Count == 0)
        {
            console.MarkupLine("[grey]Every disc of every set verified.[/]");
            console.WriteLine();
            return;
        }

        foreach (var verdict in listAll ? incomplete : incomplete.Take(PreviewRows))
        {
            var colour = verdict.State == DiscSetState.Damaged ? "red" : "yellow";
            console.MarkupLineInterpolated($"  [{colour}]{verdict.Set.Title}[/] — {verdict.Detail}");
        }

        if (!listAll && incomplete.Count > PreviewRows)
        {
            console.MarkupLineInterpolated($"[grey]  … and {incomplete.Count - PreviewRows} more. --list-all to see them.[/]");
        }

        console.MarkupLine("[grey]A set is complete only when every disc in it verifies.[/]");
        console.WriteLine();
    }

    private static void RenderStates(IAnsiConsole console, VerificationReport report)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]States[/]");
        table.AddColumn("State");
        table.AddColumn(new TableColumn("Units").RightAligned());
        table.AddColumn("Meaning");

        foreach (var (state, count) in report.Counts)
        {
            table.AddRow($"[{ColourOf(state)}]{state}[/]", count.ToString(), MeaningOf(state));
        }

        console.Write(table);
        console.MarkupLineInterpolated(
            $"[grey]{report.RenameEligible.Count} unit(s) are rename-eligible. Only Verified units ever are.[/]");
        console.WriteLine();
    }

    private static void RenderPivots(IAnsiConsole console, VerificationReport report)
    {
        var bySet = report.Units
            .Where(unit => unit.State != VerificationState.Excluded)
            .GroupBy(unit => unit.DatName ?? unit.SetFolder)
            .OrderByDescending(group => group.Count())
            .Take(PreviewRows)
            .ToArray();

        if (bySet.Length == 0)
        {
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).Title("[bold]By DAT[/]");
        table.AddColumn("DAT / folder");
        table.AddColumn(new TableColumn("Verified").RightAligned());
        table.AddColumn(new TableColumn("Mismatched").RightAligned());
        table.AddColumn(new TableColumn("In progress").RightAligned());
        table.AddColumn(new TableColumn("Unrecognized").RightAligned());

        foreach (var group in bySet)
        {
            table.AddRow(
                Markup.Escape(group.Key),
                Count(group, VerificationState.Verified),
                Count(group, VerificationState.Mismatched),
                Count(group, VerificationState.InProgress),
                Count(group, VerificationState.Unrecognized));
        }

        console.Write(table);

        var formats = report.Units
            .Where(unit => unit.Format != RomFormat.Unknown)
            .GroupBy(unit => unit.Format)
            .OrderByDescending(group => group.Count())
            .ToArray();

        if (formats.Length > 0)
        {
            console.MarkupLineInterpolated(
                $"[grey]Formats: {string.Join(" · ", formats.Select(group => $"{group.Key} {group.Count()}"))}[/]");
        }

        console.WriteLine();
    }

    private static void RenderMismatched(IAnsiConsole console, VerificationReport report, bool listAll)
    {
        var mismatched = report.InState(VerificationState.Mismatched);
        if (mismatched.Count == 0)
        {
            return;
        }

        console.MarkupLineInterpolated($"[red]Mismatched ({mismatched.Count})[/] — name matches a DAT entry, hash does not:");
        foreach (var unit in listAll ? mismatched : mismatched.Take(PreviewRows))
        {
            console.MarkupLineInterpolated($"  {unit.Name}");
            console.MarkupLineInterpolated($"[grey]    {unit.Detail}[/]");
        }

        if (!listAll && mismatched.Count > PreviewRows)
        {
            console.MarkupLineInterpolated($"[grey]  ... {mismatched.Count - PreviewRows} more. Use --all to list every one.[/]");
        }

        console.MarkupLine("[grey]Nothing was deleted or quarantined. These are reported only.[/]");
        console.WriteLine();
    }

    private static void RenderInProgress(IAnsiConsole console, VerificationReport report)
    {
        var inProgress = report.InState(VerificationState.InProgress);
        if (inProgress.Count == 0)
        {
            return;
        }

        console.MarkupLineInterpolated($"[yellow]In progress ({inProgress.Count})[/] — still being written, not judged:");
        foreach (var group in inProgress.GroupBy(unit => unit.Signal).OrderByDescending(group => group.Count()))
        {
            console.MarkupLineInterpolated($"  {group.Count()} × {InProgressDetector.Explain(group.Key)}");
        }

        console.MarkupLine("[grey]These are not corruption reports. A file mid-transfer fails a hash check for a reason that resolves itself.[/]");
        console.WriteLine();
    }

    private static void RenderFormatContradictions(IAnsiConsole console, VerificationReport report)
    {
        var contradictions = report.FormatContradictions;
        if (contradictions.Count == 0)
        {
            return;
        }

        console.MarkupLineInterpolated($"[yellow]Format contradictions ({contradictions.Count})[/] — the file's own bytes disagree with its folder:");
        foreach (var unit in contradictions.Take(PreviewRows))
        {
            console.MarkupLineInterpolated($"  {unit.Name}: {unit.FormatContradiction}");
        }

        console.MarkupLine("[grey]Neither side is trusted over the other. The disagreement is the finding.[/]");
        console.WriteLine();
    }

    private static string Count(IEnumerable<VerifiedUnit> units, VerificationState state) =>
        units.Count(unit => unit.State == state).ToString();

    private static string ColourOf(VerificationState state) => state switch
    {
        VerificationState.Verified => "green",
        VerificationState.Mismatched => "red",
        VerificationState.InProgress => "yellow",
        VerificationState.Unrecognized => "blue",
        _ => "grey",
    };

    private static string MeaningOf(VerificationState state) => state switch
    {
        VerificationState.Verified => "Hash matches the DAT entry",
        VerificationState.InProgress => "Being written — not judged",
        VerificationState.Mismatched => "Claims to be a release; the bytes disagree",
        VerificationState.Unrecognized => "No match by hash or by name",
        _ => "Not ROM content",
    };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    private static string Bytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }
}
