using System.CommandLine;
using ARK.Cli.Rendering;
using ARK.Core.Scanning;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// Wires the <c>scan</c> verb. Presentation only: it asks Core for a report and renders it.
/// </summary>
/// <remarks>
/// Scanning is read-only and produces an inventory, so there is no <c>--apply</c> here and no
/// plan to preview. Nothing this verb can do needs undoing.
/// </remarks>
public static class ScanCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Build(IAnsiConsole console, Func<string, ScanReport> scan)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(scan);

        var rootArgument = new Argument<string>("root")
        {
            Description = "Directory tree to scan. Read only — nothing is written or moved."
        };

        var allOption = new Option<bool>("--all")
        {
            Description = "List every excluded directory, not just a summary."
        };

        var command = new Command("scan", "Inventory a directory tree: identified, candidate, and excluded.");
        command.Add(rootArgument);
        command.Add(allOption);

        command.SetAction(parseResult =>
        {
            var root = parseResult.GetValue(rootArgument) ?? string.Empty;
            if (!Directory.Exists(root))
            {
                console.MarkupLineInterpolated($"[red]No such directory:[/] {root}");
                return 1;
            }

            ScanRenderer.Render(console, scan(root), parseResult.GetValue(allOption));
            return 0;
        });

        return command;
    }
}
