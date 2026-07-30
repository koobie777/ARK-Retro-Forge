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
                Markup.Escape(result.System ?? "(unrecognized)"),
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

    /// <summary>Renders catalog coverage.</summary>
    public static void RenderCoverage(IAnsiConsole console, IReadOnlyList<DatCoverage> coverage)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(coverage);

        var table = new Table().Border(TableBorder.Rounded);
        table.Title = new TableTitle("DAT Catalog Coverage");
        table.AddColumn("System");
        table.AddColumn("DAT");
        table.AddColumn("Entries");
        table.AddColumn("Version");
        table.AddColumn("Date");
        table.AddColumn("Author");

        if (coverage.Count == 0)
        {
            table.AddRow("[dim]none[/]", "[dim]no catalogs imported[/]", "-", "-", "-", "-");
        }

        foreach (var entry in coverage)
        {
            table.AddRow(
                Markup.Escape(entry.System ?? "(unrecognized)"),
                Markup.Escape(entry.DatName),
                entry.EntryCount.ToString("N0", CultureInfo.InvariantCulture),
                Markup.Escape(entry.Version ?? "-"),
                Markup.Escape(entry.Date ?? "-"),
                Markup.Escape(entry.Author ?? "-"));
        }

        console.Write(table);
    }
}
