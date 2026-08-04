using ARK.Core.Naming;
using ARK.Core.Scanning;
using ARK.Core.Units;
using Spectre.Console;

namespace ARK.Cli.Rendering;

/// <summary>Renders a <see cref="ScanReport"/>. Rendering only — it decides nothing.</summary>
public static class ScanRenderer
{
    private const int PreviewRows = 12;

    /// <summary>Writes the three buckets, the directory evidence, and the anomalies.</summary>
    public static void Render(IAnsiConsole console, ScanReport report, bool listAll)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(report);

        console.MarkupLineInterpolated($"[bold]Scan[/] {report.Root}");
        console.MarkupLineInterpolated(
            $"[grey]{report.Directories.Count} directories, {report.TotalFiles} files — read-only, nothing written.[/]");
        console.WriteLine();

        RenderBuckets(console, report);
        RenderRomSets(console, report);
        RenderExclusions(console, report, listAll);
        RenderUnsupported(console, report);
        RenderAnomalies(console, report);
        RenderFlagged(console, report);

        if (!report.IsComplete)
        {
            // The three buckets must account for every file seen; if they ever do not, say so
            // loudly rather than presenting a total that silently lost files.
            console.MarkupLineInterpolated(
                $"[red]Accounting error:[/] {report.TotalFiles} files seen but {report.IdentifiedFileCount + report.CandidateFileCount + report.ExcludedFileCount} bucketed.");
        }
    }

    private static void RenderBuckets(IAnsiConsole console, ScanReport report)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Buckets[/]");
        table.AddColumn("Bucket");
        table.AddColumn(new TableColumn("Files").RightAligned());
        table.AddColumn("Meaning");

        table.AddRow("[green]Identified[/]", report.IdentifiedFileCount.ToString(), "Name matches a DAT entry");
        table.AddRow("[yellow]Candidate[/]", report.CandidateFileCount.ToString(), "In a ROM set, no DAT match — never acted on");
        table.AddRow("[grey]Excluded[/]", report.ExcludedFileCount.ToString(), "Not ROM content");

        console.Write(table);
        console.MarkupLine("[grey]Identification is by name only. Hash verification is a later phase.[/]");

        // Which resolver claimed what. A collection that is mostly discs and a collection that is
        // mostly cartridges behave differently downstream, and the count is the fastest way to see
        // that a disc set resolved rather than falling through as unsupported.
        var cartridges = report.Units.Count(unit => unit.Unit.Kind == GameUnitKind.Cartridge);
        var discs = report.Units.Count(unit => unit.Unit.Kind == GameUnitKind.Disc);

        if (discs > 0)
        {
            var multiDisc = report.Units
                .Where(unit => unit.Unit.Kind == GameUnitKind.Disc)
                .Count(unit => unit.Unit.Name.TokensOf(TokenCategory.Disc).Count > 0);

            console.MarkupLineInterpolated(
                $"[grey]{report.Units.Count} unit(s): {cartridges} cartridge, {discs} disc ({multiDisc} carrying a disc number).[/]");
        }

        console.WriteLine();
    }

    private static void RenderRomSets(IAnsiConsole console, ScanReport report)
    {
        var romSets = report.RomSetDirectories;
        if (romSets.Count == 0)
        {
            console.MarkupLine("[yellow]No ROM-set directories found.[/]");
            console.WriteLine();
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).Title($"[bold]ROM sets ({romSets.Count})[/]");
        table.AddColumn("Directory");
        table.AddColumn(new TableColumn("Files").RightAligned());
        table.AddColumn(new TableColumn("Ext").RightAligned());
        table.AddColumn(new TableColumn("Named").RightAligned());
        table.AddColumn("Compared against");

        foreach (var directory in romSets.OrderByDescending(d => d.FileCount))
        {
            var name = directory.Profile.Warnings.Count > 0
                ? $"[yellow]{Markup.Escape(directory.Name)} (!)[/]"
                : Markup.Escape(directory.Name);

            // Naming the DAT is the point: a user has to be able to see what their files were
            // compared against, and see when nothing was.
            var scope = directory.Scope is null
                ? "[yellow]no DAT — nothing identified[/]"
                : Markup.Escape(directory.Scope.ToString());

            table.AddRow(
                name,
                directory.FileCount.ToString(),
                $"{directory.Profile.ExtensionHomogeneity:P0} {Markup.Escape(directory.Profile.DominantExtension)}",
                $"{directory.Profile.NamingConformance:P0}",
                scope);
        }

        console.Write(table);

        foreach (var directory in romSets.Where(d => d.Profile.Warnings.Count > 0))
        {
            foreach (var warning in directory.Profile.Warnings)
            {
                console.MarkupLineInterpolated($"[yellow](!)[/] {directory.Name}: {warning}");
            }
        }

        if (report.UnscopedRomSets.Count > 0)
        {
            console.MarkupLineInterpolated(
                $"[yellow]{report.UnscopedRomSets.Count} ROM-set director(ies) matched no imported DAT.[/] Import the matching DAT to identify them; nothing was guessed.");
        }

        console.WriteLine();
    }

    private static void RenderExclusions(IAnsiConsole console, ScanReport report, bool listAll)
    {
        var excluded = report.ExcludedDirectories;
        if (excluded.Count == 0)
        {
            return;
        }

        var grouped = excluded
            .GroupBy(directory => directory.Reason)
            .OrderByDescending(group => group.Sum(directory => directory.FileCount))
            .ToArray();

        var table = new Table().Border(TableBorder.Rounded)
            .Title($"[bold]Excluded ({excluded.Count} directories, {report.ExcludedFileCount} files)[/]");
        table.AddColumn("Reason");
        table.AddColumn(new TableColumn("Dirs").RightAligned());
        table.AddColumn(new TableColumn("Files").RightAligned());

        foreach (var group in grouped)
        {
            table.AddRow(
                Markup.Escape(group.Key.ToString()),
                group.Count().ToString(),
                group.Sum(directory => directory.FileCount).ToString());
        }

        console.Write(table);

        var shown = listAll ? excluded : excluded.OrderByDescending(d => d.FileCount).Take(PreviewRows).ToArray();
        foreach (var directory in shown)
        {
            console.MarkupLineInterpolated($"[grey]{directory.Name} — {directory.Explain()}[/]");
        }

        if (!listAll && excluded.Count > shown.Count)
        {
            console.MarkupLineInterpolated($"[grey]... {excluded.Count - shown.Count} more. Use --all to list every one.[/]");
        }

        console.WriteLine();
    }

    // Correct structure with no resolver for it yet. Summarized by count and never listed per
    // file: on the reference drive this is 1,762 PlayStation disc images, and printing one line
    // each would bury every genuine finding under normal files.
    private static void RenderUnsupported(IAnsiConsole console, ScanReport report)
    {
        var unsupported = report.UnsupportedFormats;
        if (unsupported.Count == 0)
        {
            return;
        }

        console.MarkupLineInterpolated($"[blue]Unsupported formats ({unsupported.Count})[/] — correct files, no resolver yet:");

        foreach (var group in unsupported
            .SelectMany(scanned => scanned.Unit.UnsupportedFormats.Select(issue => (scanned.Unit.SetFolder, issue.Code)))
            .GroupBy(pair => (pair.SetFolder, pair.Code))
            .OrderByDescending(group => group.Count()))
        {
            console.MarkupLineInterpolated(
                $"  {group.Count()} × [blue]{group.Key.Code}[/] in {group.Key.SetFolder}");
        }

        console.MarkupLine("[grey]These are not defects. They are counted, not listed.[/]");
        console.WriteLine();
    }

    private static void RenderAnomalies(IAnsiConsole console, ScanReport report)
    {
        var anomalies = report.Anomalies;
        if (anomalies.Count == 0)
        {
            return;
        }

        console.MarkupLineInterpolated($"[yellow]Anomalies ({anomalies.Count})[/] — reported, not resolved by guessing:");

        // One cause usually explains many files. Group by code so a systemic problem reads as one
        // finding rather than scrolling the actual findings off the screen.
        var grouped = anomalies
            .SelectMany(scanned => scanned.Unit.Anomalies.Select(anomaly => (scanned.Unit, anomaly)))
            .GroupBy(pair => pair.anomaly.Code, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count());

        foreach (var group in grouped)
        {
            console.MarkupLineInterpolated($"  [yellow]{group.Key}[/] — {group.Count()} unit(s)");
            foreach (var (unit, anomaly) in group.Take(3))
            {
                console.MarkupLineInterpolated($"    {Path.GetFileName(unit.PrimaryPath)}: {anomaly.Detail}");
            }

            if (group.Count() > 3)
            {
                console.MarkupLineInterpolated($"[grey]    ... {group.Count() - 3} more with the same cause.[/]");
            }
        }

        console.WriteLine();
    }

    private static void RenderFlagged(IAnsiConsole console, ScanReport report)
    {
        if (report.Flagged.Count == 0)
        {
            return;
        }

        console.MarkupLineInterpolated($"[yellow]In-flight transfers ({report.Flagged.Count})[/] — excluded from write operations:");
        foreach (var file in report.Flagged.Take(PreviewRows))
        {
            console.MarkupLineInterpolated($"  {file.Name}");
        }

        console.MarkupLine("[grey]An incomplete extension is a signal, not proof. A pre-allocated file with the right size and wrong bytes passes every cheap check.[/]");
        console.WriteLine();
    }
}
