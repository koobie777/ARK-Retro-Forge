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
        nameof(MedicalBayReport.ActiveSystemProfile),
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
        var profile = report.ActiveSystemProfile;

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow("Instance", Markup.Escape(report.InstanceName));
        grid.AddRow("ROM Root", Markup.Escape(romRoot));
        grid.AddRow("System", Markup.Escape($"{profile.Name} ({profile.Code})"));

        console.Write(new Panel(grid) { Header = new PanelHeader("Medical Bay") });
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
        table.AddColumn("Catalog");
        table.AddColumn("Files");
        table.AddColumn("Last Sync (UTC)");

        foreach (var catalog in catalogs)
        {
            var status = !catalog.HasCatalog ? "Missing" : catalog.IsStale ? "Stale" : "Ready";
            table.AddRow(
                Markup.Escape(catalog.System),
                status,
                catalog.LocalFileCount.ToString("N0", CultureInfo.InvariantCulture),
                catalog.LastUpdatedUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "-");
        }

        console.Write(table);
    }
}
