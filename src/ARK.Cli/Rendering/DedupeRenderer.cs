using ARK.Core.Dedup;
using ARK.Core.Execution;
using Spectre.Console;

namespace ARK.Cli.Rendering;

/// <summary>Renders deduplication results. Rendering only — it decides nothing.</summary>
public static class DedupeRenderer
{
    private const int PreviewRows = 10;

    /// <summary>Writes the analysis.</summary>
    public static void Render(IAnsiConsole console, DedupReport report, bool listAll)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(report);

        console.MarkupLineInterpolated($"[bold]Dedupe[/] {report.Root}");
        console.MarkupLineInterpolated(
            $"[grey]Policy: {report.Policy}. {report.Groups.Count} duplicate group(s), {report.Hashed} unit(s) hashed this run.[/]");
        console.WriteLine();

        if (report.Groups.Count == 0)
        {
            console.MarkupLine("[green]No byte-identical duplicates found.[/]");
        }
        else
        {
            RenderGroups(console, report, listAll);
        }

        RenderExclusions(console, report);
    }

    private static void RenderGroups(IAnsiConsole console, DedupReport report, bool listAll)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Duplicate groups[/]");
        table.AddColumn("CRC32");
        table.AddColumn(new TableColumn("Copies").RightAligned());
        table.AddColumn(new TableColumn("ROM size").RightAligned());
        table.AddColumn("Keep");
        table.AddColumn("Decision");

        var shown = listAll ? report.Groups : report.Groups.Take(PreviewRows).ToArray();
        foreach (var group in shown)
        {
            table.AddRow(
                Markup.Escape(group.Crc32),
                group.Members.Count.ToString(),
                Bytes(group.RomSize),
                group.Keep is null ? "[yellow]—[/]" : Markup.Escape(group.Keep.Name),
                group.IsTie ? $"[yellow]{Markup.Escape(group.Reason)}[/]" : Markup.Escape(group.Reason));
        }

        console.Write(table);

        if (!listAll && report.Groups.Count > shown.Count)
        {
            console.MarkupLineInterpolated($"[grey]... {report.Groups.Count - shown.Count} more. Use --all to list every group.[/]");
        }

        console.MarkupLineInterpolated(
            $"[grey]{report.Resolved.Count} group(s) resolved, {report.Removable.Count} redundant copy(ies), {Bytes(report.ReclaimableBytes)} reclaimable.[/]");

        if (report.Ties.Count > 0)
        {
            // A coin flip presented as a decision is a guess wearing a confident label.
            console.MarkupLineInterpolated(
                $"[yellow]{report.Ties.Count} group(s) the policy could not decide[/] — reported and skipped, never resolved arbitrarily.");
        }

        console.WriteLine();
    }

    private static void RenderExclusions(IAnsiConsole console, DedupReport report)
    {
        var grouped = report.Excluded
            .GroupBy(entry => entry.Reason)
            .OrderByDescending(group => group.Count())
            .ToArray();

        if (grouped.Length == 0)
        {
            return;
        }

        console.MarkupLine("[bold]Not considered[/]");
        foreach (var group in grouped)
        {
            var colour = group.Key is DedupExclusion.InProgress or DedupExclusion.Mismatched ? "yellow" : "grey";
            console.MarkupLineInterpolated($"  [{colour}]{group.Key}[/] — {group.Count()}: {group.First().Detail}");
        }

        console.WriteLine();
    }

    /// <summary>Writes the quarantine plan and, when applied, its outcome.</summary>
    public static void RenderQuarantine(
        IAnsiConsole console,
        QuarantinePlan plan,
        ExecutionResult? result,
        bool applied)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var refusal in plan.Refused)
        {
            console.MarkupLineInterpolated($"[yellow]Refused:[/] {refusal.Candidate.Name} — {refusal.Reason}");
        }

        var moves = plan.Manifest.Units.Count;
        if (moves == 0)
        {
            console.MarkupLine("[green]Nothing to quarantine.[/]");
            return;
        }

        if (!applied)
        {
            console.MarkupLineInterpolated(
                $"[yellow]DRY-RUN.[/] {moves} unit(s) would move to quarantine. Nothing was touched. Re-run with --apply.");
            return;
        }

        if (result is null)
        {
            return;
        }

        foreach (var failure in result.Results.Where(entry => entry.Status == ActionStatus.Failed))
        {
            console.MarkupLineInterpolated($"[red]Failed:[/] {failure.Action.Source} — {failure.Detail}");
        }

        console.MarkupLineInterpolated(
            $"[green]Quarantined {moves} unit(s).[/] Manifest and journal written; reverse with [bold]ark undo {plan.Plan.SessionId}[/].");
    }

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
