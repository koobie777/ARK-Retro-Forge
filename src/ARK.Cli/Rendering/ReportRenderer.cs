using ARK.Cli.Commands;
using ARK.Core.Reporting;
using Spectre.Console;

namespace ARK.Cli.Rendering;

/// <summary>Renders a <see cref="CollectionReport"/>. Rendering only — it decides nothing.</summary>
public static class ReportRenderer
{
    private const int PreviewRows = 15;

    /// <summary>Writes the states, the pivot, the upgrades, and the missing queue.</summary>
    public static void Render(IAnsiConsole console, CollectionReport report, ReportPivot pivot, bool listAll)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(report);

        console.MarkupLineInterpolated($"[bold]Report[/] {report.Root}");

        // Completeness against the whole DAT would be true and useless, so the report always says
        // which policy and which DATs produced the number it is quoting.
        console.MarkupLineInterpolated(
            $"[grey]Target set: {report.TargetSetSize} release(s) from policy '{report.PolicyName}' over {report.DatNames.Count} DAT(s).[/]");
        if (report.DatNames.Count > 0)
        {
            console.MarkupLineInterpolated($"[grey]  {string.Join(", ", report.DatNames.Take(6))}{(report.DatNames.Count > 6 ? ", …" : string.Empty)}[/]");
        }

        console.WriteLine();

        RenderStates(console, report);
        RenderPivot(console, report, pivot);
        RenderUpgrades(console, report, listAll);
        RenderMissing(console, report, listAll);

        if (report.RefusedGroups > 0)
        {
            console.MarkupLineInterpolated(
                $"[yellow]{report.RefusedGroups} target group(s) the policy could not rank[/] — every member is treated as wanted, never guessed at.");
        }

        console.MarkupLineInterpolated(
            $"[grey]Catalog names: {report.CatalogNamesTokenized} tokenized, {report.CatalogNameCacheHits} from cache. Read-only — nothing was written.[/]");
    }

    private static void RenderStates(IAnsiConsole console, CollectionReport report)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Collection[/]");
        table.AddColumn("State");
        table.AddColumn(new TableColumn("Titles").RightAligned());
        table.AddColumn("What to do");

        foreach (var (state, count) in report.Counts)
        {
            table.AddRow($"[{ColourOf(state)}]{state}[/]", count.ToString(), ActionFor(state));
        }

        console.Write(table);
        console.MarkupLineInterpolated($"[bold]{report.Completeness:P1}[/] of the target set present and verified.");
        console.WriteLine();
    }

    private static void RenderPivot(IAnsiConsole console, CollectionReport report, ReportPivot pivot)
    {
        var table = new Table().Border(TableBorder.Rounded).Title($"[bold]Missing by {pivot.ToString().ToLowerInvariant()}[/]");

        switch (pivot)
        {
            case ReportPivot.Region:
                table.AddColumn("Region");
                table.AddColumn(new TableColumn("Missing").RightAligned());
                foreach (var (region, count) in report.ByRegion(CollectionState.Missing).Take(PreviewRows))
                {
                    table.AddRow(Markup.Escape(region), count.ToString());
                }

                break;

            case ReportPivot.Both:
                table.AddColumn("System");
                table.AddColumn("Region");
                table.AddColumn(new TableColumn("Missing").RightAligned());
                foreach (var (system, region, count) in report.BySystemAndRegion(CollectionState.Missing).Take(PreviewRows))
                {
                    table.AddRow(Markup.Escape(system), Markup.Escape(region), count.ToString());
                }

                break;

            default:
                table.AddColumn("System");
                table.AddColumn(new TableColumn("Missing").RightAligned());
                table.AddColumn(new TableColumn("Present").RightAligned());
                table.AddColumn(new TableColumn("Damaged").RightAligned());

                var present = report.BySystem(CollectionState.Present).ToDictionary(entry => entry.System, entry => entry.Count, StringComparer.OrdinalIgnoreCase);
                var damaged = report.BySystem(CollectionState.Damaged).ToDictionary(entry => entry.System, entry => entry.Count, StringComparer.OrdinalIgnoreCase);

                foreach (var (system, count) in report.BySystem(CollectionState.Missing).Take(PreviewRows))
                {
                    table.AddRow(
                        Markup.Escape(system),
                        count.ToString(),
                        present.GetValueOrDefault(system).ToString(),
                        damaged.GetValueOrDefault(system).ToString());
                }

                break;
        }

        console.Write(table);
        console.WriteLine();
    }

    private static void RenderUpgrades(IAnsiConsole console, CollectionReport report, bool listAll)
    {
        if (report.Upgradable.Count == 0)
        {
            return;
        }

        // A missing list tells you what to hunt; this tells you what you believe is fine and isn't.
        console.MarkupLineInterpolated($"[blue]Upgradable ({report.Upgradable.Count})[/] — held and verified, but the target set ranks something higher:");
        foreach (var upgrade in listAll ? report.Upgradable : report.Upgradable.Take(PreviewRows))
        {
            console.MarkupLineInterpolated(
                $"  {upgrade.Held.Title} {Rev(upgrade.Held.Revision)} → {Rev(upgrade.Better.Revision)}");
        }

        if (!listAll && report.Upgradable.Count > PreviewRows)
        {
            console.MarkupLineInterpolated($"[grey]  ... {report.Upgradable.Count - PreviewRows} more. Use --all.[/]");
        }

        console.WriteLine();
    }

    private static void RenderMissing(IAnsiConsole console, CollectionReport report, bool listAll)
    {
        var missing = report.InState(CollectionState.Missing);
        if (missing.Count == 0)
        {
            console.MarkupLine("[green]Nothing missing from the target set.[/]");
            console.WriteLine();
            return;
        }

        console.MarkupLineInterpolated($"[yellow]Missing ({missing.Count})[/] — in the target set, not on disk:");
        foreach (var row in listAll ? missing : missing.Take(PreviewRows))
        {
            console.MarkupLineInterpolated(
                $"  {row.Title} [grey]{string.Join(",", row.Regions)} {Rev(row.Revision)}[/]");
        }

        if (!listAll && missing.Count > PreviewRows)
        {
            console.MarkupLineInterpolated($"[grey]  ... {missing.Count - PreviewRows} more. Use --all to list every one.[/]");
        }

        console.MarkupLine("[grey]Use --missing for a machine-readable queue, one line per title.[/]");
        console.WriteLine();
    }

    private static string Rev(string revision) => revision.Length == 0 ? "Rev 0" : revision;

    private static string ColourOf(CollectionState state) => state switch
    {
        CollectionState.Present => "green",
        CollectionState.Missing => "yellow",
        CollectionState.Damaged => "red",
        _ => "blue",
    };

    private static string ActionFor(CollectionState state) => state switch
    {
        CollectionState.Present => "Nothing",
        CollectionState.Missing => "Acquire",
        CollectionState.Damaged => "Re-acquire this specific title",
        _ => "Investigate — bad dump, hack, homebrew, or from elsewhere",
    };
}
