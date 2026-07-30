using System.Globalization;
using ARK.Core.Settings;
using Spectre.Console;

namespace ARK.Cli.Rendering;

/// <summary>Renders settings output for the <c>config</c> verb. Presentation only.</summary>
public static class ConfigRenderer
{
    /// <summary>Renders the current settings as a panel.</summary>
    public static void RenderSettings(IAnsiConsole console, ArkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(settings);

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow("Schema version", settings.SchemaVersion.ToString(CultureInfo.InvariantCulture));
        grid.AddRow("ROM root", Markup.Escape(settings.RomRoot ?? "Not set"));
        grid.AddRow("Active system", Markup.Escape(settings.ActiveSystem ?? "Not set"));

        console.Write(new Panel(grid) { Header = new PanelHeader("Settings") });
    }

    /// <summary>Renders confirmation that a single setting was changed.</summary>
    public static void RenderSet(IAnsiConsole console, string label, string value)
    {
        ArgumentNullException.ThrowIfNull(console);
        console.MarkupLineInterpolated($"[green]{label} set to:[/] {value}");
    }
}
