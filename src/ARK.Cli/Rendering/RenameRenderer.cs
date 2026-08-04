using ARK.Core.Execution;
using ARK.Core.Renaming;
using Spectre.Console;

namespace ARK.Cli.Rendering;

/// <summary>Renders rename and organize results. Rendering only — it decides nothing.</summary>
public static class RenameRenderer
{
    private const int PreviewRows = 15;

    /// <summary>Writes the rename decisions.</summary>
    public static void Render(IAnsiConsole console, RenameReport report, bool listAll)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(report);

        console.MarkupLineInterpolated($"[bold]Rename[/] {report.Root}");
        console.MarkupLine(report.Mode == RenameMode.Canonicalize
            ? "[grey]Mode: canonicalize — names come from the DAT entry each unit's hash confirmed.[/]"
            : "[grey]Mode: normalize — each unit's own name, reformatted. This repairs damage; it does not claim the name is correct.[/]");
        console.WriteLine();

        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Decisions[/]");
        table.AddColumn("Outcome");
        table.AddColumn(new TableColumn("Units").RightAligned());
        table.AddColumn("Meaning");

        table.AddRow("[green]Rename[/]", report.Renames.Count.ToString(), "Name would change");
        table.AddRow("[grey]Already correct[/]", report.AlreadyCorrect.Count.ToString(), "No filesystem write at all");
        table.AddRow("[yellow]Refused[/]", report.Refused.Count.ToString(), "Left alone, with a reason");

        console.Write(table);

        if (report.Renames.Count == 0 && report.Refused.Count == 0)
        {
            // The correct result on a conformant set, and worth saying out loud.
            console.MarkupLine("[green]Every unit already carries its canonical name. Nothing to do.[/]");
            console.WriteLine();
            return;
        }

        RenameList(console, report, listAll);
        RefusalList(console, report);
    }

    private static void RenameList(IAnsiConsole console, RenameReport report, bool listAll)
    {
        if (report.Renames.Count == 0)
        {
            return;
        }

        console.MarkupLineInterpolated($"[green]Would rename ({report.Renames.Count})[/]:");
        foreach (var decision in listAll ? report.Renames : report.Renames.Take(PreviewRows))
        {
            console.MarkupLineInterpolated($"  {decision.CurrentName}");
            console.MarkupLineInterpolated($"[grey]    → {decision.ProposedName}[/]");
        }

        if (!listAll && report.Renames.Count > PreviewRows)
        {
            console.MarkupLineInterpolated($"[grey]  ... {report.Renames.Count - PreviewRows} more. Use --all.[/]");
        }

        console.WriteLine();
    }

    private static void RefusalList(IAnsiConsole console, RenameReport report)
    {
        if (report.Refused.Count == 0)
        {
            return;
        }

        console.MarkupLine("[bold]Refused[/]");
        foreach (var group in report.Refused.GroupBy(decision => decision.Refusal).OrderByDescending(group => group.Count()))
        {
            console.MarkupLineInterpolated($"  [yellow]{group.Key}[/] — {group.Count()}: {group.First().Detail}");

            if (group.Key == RenameRefusal.Collision)
            {
                foreach (var decision in group.Take(4))
                {
                    console.MarkupLineInterpolated($"[grey]    {decision.CurrentName} → {decision.ProposedName}[/]");
                }
            }
        }

        console.WriteLine();
    }

    /// <summary>Writes the rename plan and, when applied, its outcome.</summary>
    public static void RenderPlan(IAnsiConsole console, RenamePlan plan, ExecutionResult? result, bool applied)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var (decision, reason) in plan.Refused)
        {
            console.MarkupLineInterpolated($"[yellow]Refused:[/] {decision.CurrentName} — {reason}");
        }

        if (plan.Staged.Count > 0)
        {
            // Worth surfacing: it means the batch contained a swap, a cycle, or a case-only change,
            // any of which a naive one-at-a-time rename would have corrupted.
            console.MarkupLineInterpolated(
                $"[blue]{plan.Staged.Count} unit(s) routed through a temporary name[/] to break a rename cycle safely.");
        }

        if (plan.Renamed.Count == 0)
        {
            console.MarkupLine("[green]Nothing to rename.[/]");
            return;
        }

        if (!applied)
        {
            console.MarkupLineInterpolated(
                $"[yellow]DRY-RUN.[/] {plan.Renamed.Count} unit(s) would be renamed. Nothing was touched. Re-run with --apply.");
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
            $"[green]Renamed {plan.Renamed.Count} unit(s).[/] Archive and ROM bytes unchanged; reverse with [bold]ark undo {plan.Plan.SessionId}[/].");
    }

    /// <summary>Writes the organize plan. A different operation, and said so.</summary>
    public static void RenderOrganize(IAnsiConsole console, OrganizePlan plan, ExecutionResult? result, bool applied)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(plan);

        console.MarkupLine("[bold]Organize[/]");
        console.MarkupLineInterpolated(
            $"[grey]{plan.Moves.Count} to file, {plan.AlreadyFiled.Count} already in place, {plan.Refused.Count} left alone.[/]");

        foreach (var group in plan.Refused.GroupBy(entry => entry.Reason).OrderByDescending(group => group.Count()))
        {
            console.MarkupLineInterpolated($"[yellow]  {group.Count()} ×[/] {group.Key}");
        }

        foreach (var group in plan.Moves.GroupBy(move => move.Folder).OrderByDescending(group => group.Count()).Take(PreviewRows))
        {
            console.MarkupLineInterpolated($"  {group.Count()} → {group.Key}");
        }

        if (plan.Moves.Count == 0)
        {
            console.MarkupLine("[green]Nothing to organize.[/]");
            return;
        }

        if (!applied)
        {
            console.MarkupLineInterpolated(
                $"[yellow]DRY-RUN.[/] {plan.Moves.Count} unit(s) would move. Nothing was touched. Re-run with --apply.");
            console.MarkupLine("[grey]This is filing, not removal: every unit stays in the collection.[/]");
            return;
        }

        if (result is null)
        {
            return;
        }

        console.MarkupLineInterpolated(
            $"[green]Organized {plan.Moves.Count} unit(s).[/] Nothing was removed; reverse with [bold]ark undo {plan.Plan.SessionId}[/].");
    }
}
