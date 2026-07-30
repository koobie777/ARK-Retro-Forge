using System.Globalization;
using ARK.Core.Diagnostics;
using ARK.Core.Tools;
using Spectre.Console;

namespace ARK.Cli.Rendering;

/// <summary>
/// Renders a <see cref="MedicalBayReport"/> to an <see cref="IAnsiConsole"/>. Presentation only:
/// it surfaces every field of the report and makes no decision from its contents. The console is
/// injected so the output can be captured in tests.
/// </summary>
public static class MedicalBayRenderer
{
    /// <summary>
    /// The <see cref="MedicalBayReport"/> fields this renderer surfaces. The field-parity test
    /// asserts this equals the report's property set, guaranteeing the human view omits nothing the
    /// <c>--json</c> output shows (and vice versa).
    /// </summary>
    public static IReadOnlyList<string> CoveredReportFields { get; } =
    [
        nameof(MedicalBayReport.InstanceName),
        nameof(MedicalBayReport.RomRoot),
        nameof(MedicalBayReport.RomRootSet),
        nameof(MedicalBayReport.ActiveSystem),
        nameof(MedicalBayReport.Tools),
        nameof(MedicalBayReport.DatCatalogs)
    ];

    /// <summary>Renders the full report: header, tool table, and DAT table.</summary>
    public static void Render(IAnsiConsole console, MedicalBayReport report)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(report);

        RenderHeader(console, report);
        RenderTools(console, report.Tools);
        RenderDatCatalogs(console, report.DatCatalogs);
    }

    private static void RenderHeader(IAnsiConsole console, MedicalBayReport report)
    {
        var romRoot = report.RomRootSet ? report.RomRoot! : "Not set";

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow("Instance", Markup.Escape(report.InstanceName));
        grid.AddRow("ROM Root", Markup.Escape(romRoot));
        grid.AddRow("System", Markup.Escape(DescribeSystem(report.ActiveSystem)));

        console.Write(new Panel(grid) { Header = new PanelHeader("Medical Bay") });
    }

    private static string DescribeSystem(ActiveSystemStatus system)
    {
        if (system.Code is null)
        {
            return "Not set";
        }

        return system.Recognized
            ? $"{system.DisplayName} ({system.Code})"
            : $"{system.Code} (unrecognized)";
    }

    private static void RenderTools(IAnsiConsole console, IReadOnlyList<ToolCheckResult> tools)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.Title = new TableTitle("Tools");
        table.AddColumn("Tool");
        table.AddColumn("Status");
        table.AddColumn("Version");
        table.AddColumn("Minimum");
        table.AddColumn("Meets min");
        table.AddColumn("Location / Notes");

        foreach (var tool in tools)
        {
            table.AddRow(
                Markup.Escape(tool.Name),
                tool.IsFound ? "Ready" : "Missing",
                Markup.Escape(tool.Version ?? "n/a"),
                Markup.Escape(tool.MinimumVersion ?? "-"),
                tool.MeetsMinimumVersion ? "yes" : "no",
                Markup.Escape(tool.IsFound ? (tool.Path ?? "detected") : (tool.ErrorMessage ?? "not found")));
        }

        console.Write(table);
    }

    private static void RenderDatCatalogs(IAnsiConsole console, IReadOnlyList<DatStatus> catalogs)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.Title = new TableTitle("DAT Catalogs");
        table.AddColumn("System");
        table.AddColumn("DAT");
        table.AddColumn("Entries");
        table.AddColumn("Version");
        table.AddColumn("Freshness");

        if (catalogs.Count == 0)
        {
            table.AddRow("[dim]none[/]", "[dim]no catalogs imported[/]", "-", "-", "-");
        }

        foreach (var catalog in catalogs)
        {
            table.AddRow(
                Markup.Escape(catalog.System),
                Markup.Escape(catalog.DatName),
                catalog.EntryCount.ToString("N0", CultureInfo.InvariantCulture),
                Markup.Escape(catalog.Version ?? "-"),
                catalog.IsStale ? "Stale" : "Current");
        }

        console.Write(table);
    }
}
