using System.CommandLine;
using ARK.Cli.Rendering;
using ARK.Core.Policy;
using ARK.Core.Reporting;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>How to pivot the report.</summary>
public enum ReportPivot
{
    /// <summary>By system.</summary>
    System = 0,

    /// <summary>By region.</summary>
    Region,

    /// <summary>By system and region together.</summary>
    Both,
}

/// <summary>
/// Wires the <c>report</c> verb. Presentation and wiring only.
/// </summary>
/// <remarks>
/// Read-only throughout. Nothing moves, nothing is quarantined, and there is no <c>--apply</c>
/// because there is nothing to apply.
/// </remarks>
public static class ReportCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Build(
        IAnsiConsole console,
        Func<string, VariantPolicy, CollectionReport> report,
        VariantPolicy configured)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(configured);

        var rootArgument = new Argument<string>("root") { Description = "Directory tree to report on. Read only." };
        var policyOption = new Option<string?>("--policy")
        {
            Description = $"Policy defining the target set: {string.Join(", ", VariantPresets.All.Keys)}. Defaults to the configured policy.",
        };
        var pivotOption = new Option<ReportPivot>("--pivot")
        {
            Description = "Pivot the counts by system, region, or both.",
            DefaultValueFactory = _ => ReportPivot.System,
        };
        var jsonOption = new Option<bool>("--json") { Description = "Emit the report as JSON instead of tables." };
        var missingOption = new Option<bool>("--missing")
        {
            Description = "Emit only the missing list, one line per title, as a re-acquisition queue.",
        };
        var allOption = new Option<bool>("--all") { Description = "List every row, not just a summary." };

        var command = new Command("report", "Compare a collection against the target set your policy declares.");
        command.Add(rootArgument);
        command.Add(policyOption);
        command.Add(pivotOption);
        command.Add(jsonOption);
        command.Add(missingOption);
        command.Add(allOption);

        command.SetAction(parseResult =>
        {
            var root = parseResult.GetValue(rootArgument) ?? string.Empty;
            if (!Directory.Exists(root))
            {
                console.MarkupLineInterpolated($"[red]No such directory:[/] {root}");
                return 1;
            }

            var policy = configured;
            var requested = parseResult.GetValue(policyOption);
            if (requested is { Length: > 0 })
            {
                var found = VariantPresets.Find(requested);
                if (found is null)
                {
                    console.MarkupLineInterpolated($"[red]Unknown policy '{requested}'.[/] Available: {string.Join(", ", VariantPresets.All.Keys)}");
                    return 1;
                }

                policy = found;
            }

            var result = report(root, policy);

            if (parseResult.GetValue(jsonOption))
            {
                Console.Out.WriteLine(CollectionReportJson.Serialize(result));
                return 0;
            }

            if (parseResult.GetValue(missingOption))
            {
                // Straight to stdout, no decoration: this is meant to be piped.
                foreach (var line in result.MissingQueue())
                {
                    Console.Out.WriteLine(line);
                }

                return 0;
            }

            ReportRenderer.Render(console, result, parseResult.GetValue(pivotOption), parseResult.GetValue(allOption));
            return 0;
        });

        return command;
    }
}
