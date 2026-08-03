using ARK.Core.Dedup;
using ARK.Core.Execution;
using ARK.Core.Policy;
using Spectre.Console;

namespace ARK.Cli.Rendering;

/// <summary>Renders curation results. Rendering only — it decides nothing.</summary>
public static class CurateRenderer
{
    private const int PreviewRows = 12;

    /// <summary>Writes the analysis.</summary>
    public static void Render(IAnsiConsole console, CurationReport report, bool listAll)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(report);

        console.MarkupLineInterpolated($"[bold]Curate[/] {report.Root}");
        console.MarkupLineInterpolated($"[grey]Policy: {report.Policy.Name}[/]");

        RenderAxes(console, report);
        RenderGroups(console, report, listAll);
        RenderRefusals(console, report);
        RenderExclusions(console, report);
    }

    // The same collection curates differently depending on which axes identify a game and which
    // vary within one, so the report has to say which reading produced these decisions.
    private static void RenderAxes(IAnsiConsole console, CurationReport report)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Axes[/]");
        table.AddColumn("Role");
        table.AddColumn("Axes");
        table.AddColumn("Meaning");

        table.AddRow(
            "[blue]Identity[/]",
            Markup.Escape(Join(report.IdentityAxes)),
            "Differing here means a different game — never compared");
        table.AddRow(
            "[green]Variance[/]",
            Markup.Escape(Join(report.VarianceAxes)),
            "Differing here means variants of one game — ranked");
        table.AddRow(
            "[grey]Ignored[/]",
            Markup.Escape(Join(report.IgnoredAxes)),
            "Left entirely alone");

        console.Write(table);
        console.MarkupLine("[grey]Hardware flags never participate: they describe cartridge capability, not release lineage.[/]");
        console.WriteLine();
    }

    private static void RenderGroups(IAnsiConsole console, CurationReport report, bool listAll)
    {
        var multi = report.Multi;
        if (multi.Count == 0)
        {
            console.MarkupLine("[green]No variant groups: every release is its own game under this policy.[/]");
            console.WriteLine();
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).Title($"[bold]Variant groups ({multi.Count})[/]");
        table.AddColumn("Title");
        table.AddColumn(new TableColumn("Releases").RightAligned());
        table.AddColumn(new TableColumn("Keep").RightAligned());
        table.AddColumn(new TableColumn("Remove").RightAligned());
        table.AddColumn("Decision");

        var shown = listAll ? multi : multi.Take(PreviewRows).ToArray();
        foreach (var group in shown)
        {
            table.AddRow(
                Markup.Escape(Truncate(group.Title, 40)),
                group.Members.Count.ToString(),
                group.Keep.Count.ToString(),
                group.Remove.Count == 0 ? "—" : $"[red]{group.Remove.Count}[/]",
                group.IsRefused ? $"[yellow]{Markup.Escape(group.Reason)}[/]" : Markup.Escape(Truncate(group.Reason, 44)));
        }

        console.Write(table);

        if (!listAll && multi.Count > shown.Count)
        {
            console.MarkupLineInterpolated($"[grey]... {multi.Count - shown.Count} more. Use --all to list every group.[/]");
        }

        console.MarkupLineInterpolated(
            $"[grey]{report.Removable.Count} release(s) would be removed, {Bytes(report.ReclaimableBytes)} reclaimable.[/]");

        if (report.Policy.IsReportOnly)
        {
            console.MarkupLine("[grey]Report-only policy: nothing is chosen and nothing moves. Pass --policy to decide.[/]");
        }

        console.WriteLine();
    }

    private static void RenderRefusals(IAnsiConsole console, CurationReport report)
    {
        var refused = report.Refused;
        if (refused.Count == 0)
        {
            return;
        }

        console.MarkupLineInterpolated($"[yellow]Reported and skipped ({refused.Count})[/] — an axis could not be ordered:");
        foreach (var group in refused.Take(PreviewRows))
        {
            console.MarkupLineInterpolated($"  {Truncate(group.Title, 50)}");
            foreach (var refusal in group.Refusals)
            {
                console.MarkupLineInterpolated($"[grey]    {refusal.Axis}: {refusal.Reason}[/]");
            }
        }

        if (refused.Count > PreviewRows)
        {
            console.MarkupLineInterpolated($"[grey]  ... {refused.Count - PreviewRows} more.[/]");
        }

        console.MarkupLine("[grey]These are left whole. A guess wearing a confident label is worse than an unanswered question.[/]");
        console.WriteLine();
    }

    private static void RenderExclusions(IAnsiConsole console, CurationReport report)
    {
        var grouped = report.Excluded
            .GroupBy(entry => entry.Reason)
            .OrderByDescending(group => group.Count())
            .ToArray();

        if (grouped.Length == 0)
        {
            return;
        }

        console.MarkupLine("[bold]Not curated[/]");
        foreach (var group in grouped)
        {
            console.MarkupLineInterpolated($"  [grey]{group.Key}[/] — {group.Count()}: {group.First().Detail}");
        }

        console.WriteLine();
    }

    /// <summary>Writes the quarantine plan and, when applied, its outcome.</summary>
    public static void RenderQuarantine(IAnsiConsole console, QuarantinePlan plan, ExecutionResult? result, bool applied)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var refusal in plan.Refused)
        {
            console.MarkupLineInterpolated($"[yellow]Refused:[/] {refusal.Request.Name} — {refusal.Reason}");
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
                $"[yellow]DRY-RUN.[/] {moves} release(s) would be quarantined. Nothing was touched. Re-run with --apply.");
            console.MarkupLine("[grey]These are distinct releases, not redundant copies — none of them can be recovered from another file.[/]");
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
            $"[green]Quarantined {moves} release(s).[/] Manifest and journal written; reverse with [bold]ark undo {plan.Plan.SessionId}[/].");
    }

    /// <summary>Writes the sort plan. Organization, and said so plainly.</summary>
    public static void RenderSort(IAnsiConsole console, SortPlan plan, ExecutionResult? result, bool applied)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var (path, reason) in plan.Refused)
        {
            console.MarkupLineInterpolated($"[yellow]Refused:[/] {Path.GetFileName(path)} — {reason}");
        }

        if (plan.Moves.Count == 0)
        {
            console.MarkupLine("[green]Nothing to sort.[/]");
            return;
        }

        if (!applied)
        {
            console.MarkupLineInterpolated(
                $"[yellow]DRY-RUN.[/] {plan.Moves.Count} release(s) would move into '{plan.Moves[0].Subfolder}'. Nothing was touched. Re-run with --apply.");
            console.MarkupLine("[grey]This is organization, not removal: every release stays in the collection.[/]");
            return;
        }

        if (result is null)
        {
            return;
        }

        console.MarkupLineInterpolated(
            $"[green]Sorted {plan.Moves.Count} release(s) into '{plan.Moves[0].Subfolder}'.[/] Nothing was removed; reverse with [bold]ark undo {plan.Plan.SessionId}[/].");
    }

    private static string Join(IReadOnlyList<VariantAxis> axes) =>
        axes.Count == 0 ? "—" : string.Join(", ", axes);

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
