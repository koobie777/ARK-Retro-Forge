using System.Globalization;
using ARK.Core.Dat;
using Spectre.Console;

namespace ARK.Cli.Rendering;

/// <summary>Renders output for the <c>dat</c> verb. Presentation only.</summary>
public static class DatRenderer
{
    /// <summary>Renders an import summary.</summary>
    public static void RenderImport(IAnsiConsole console, DatImportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(summary);

        var table = new Table().Border(TableBorder.Rounded);
        table.Title = new TableTitle("Imported DATs");
        table.AddColumn("DAT");
        table.AddColumn("System");
        table.AddColumn("Entries");

        foreach (var result in summary.Results)
        {
            table.AddRow(
                Markup.Escape(result.DatName),
                Markup.Escape(result.SystemLabel),
                result.EntryCount.ToString("N0", CultureInfo.InvariantCulture));
        }

        console.Write(table);
        console.MarkupLineInterpolated(
            $"[green]Imported {summary.DatsImported} DAT(s), {summary.EntriesIndexed} entries indexed.[/]");
    }

    /// <summary>Renders a sync summary.</summary>
    public static void RenderSync(IAnsiConsole console, DatSyncSummary summary)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(summary);

        var table = new Table().Border(TableBorder.Rounded);
        table.Title = new TableTitle("Sync");
        table.AddColumn("Source");
        table.AddColumn("Outcome");
        table.AddColumn("Detail");

        foreach (var result in summary.Results)
        {
            table.AddRow(
                Markup.Escape(result.SourceName),
                result.Outcome.ToString(),
                Markup.Escape(result.Detail ?? "-"));
        }

        console.Write(table);
        console.MarkupLineInterpolated(
            $"[green]Sync complete:[/] {summary.Fetched} fetched, {summary.Skipped} cached, {summary.Failed} failed.");
    }

    /// <summary>
    /// Renders catalog coverage. Summary by default; author lists only when asked for, because a
    /// real catalog spans roughly 200 DATs whose author fields run to dozens of names each.
    /// </summary>
    public static void RenderCoverage(IAnsiConsole console, IReadOnlyList<DatCoverage> coverage, bool verbose = false)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(coverage);

        var table = new Table().Border(TableBorder.Rounded);
        table.Title = new TableTitle("DAT Catalog Coverage");
        table.AddColumn("System");
        table.AddColumn("DAT");
        table.AddColumn(new TableColumn("Entries").RightAligned());
        table.AddColumn("Version");

        if (verbose)
        {
            table.AddColumn("Date");
            table.AddColumn("Author");
        }

        if (coverage.Count == 0)
        {
            console.MarkupLine("[yellow]No catalogs match.[/]");
            return;
        }

        foreach (var entry in coverage)
        {
            var cells = new List<string>
            {
                Markup.Escape(Label(entry)),
                Markup.Escape(entry.DatName),
                entry.EntryCount.ToString("N0", CultureInfo.InvariantCulture),
                Markup.Escape(entry.Version ?? "-"),
            };

            if (verbose)
            {
                cells.Add(Markup.Escape(entry.Date ?? "-"));
                cells.Add(Markup.Escape(entry.Author ?? "-"));
            }

            table.AddRow(cells.ToArray());
        }

        console.Write(table);

        var recognized = coverage.Count(entry => entry.System is not null);
        var entries = coverage.Sum(entry => (long)entry.EntryCount);
        console.MarkupLineInterpolated(
            $"[grey]{coverage.Count} DAT(s): {recognized} resolved to a system, {coverage.Count - recognized} unrecognized. {entries:N0} entries.[/]");

        if (!verbose)
        {
            console.MarkupLine("[grey]Use --verbose for dates and authors, --system <code> to narrow.[/]");
        }
    }

    // A format qualifier is part of the identity, not decoration: (Headered) and (Headerless) are
    // two distinct hash sets over the same games.
    private static string Label(DatCoverage entry) => entry.System is null
        ? "(unrecognized)"
        : entry.Qualifier is null ? entry.System : $"{entry.System} ({entry.Qualifier})";
}
