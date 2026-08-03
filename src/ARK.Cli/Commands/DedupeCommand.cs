using System.CommandLine;
using ARK.Cli.Rendering;
using ARK.Core.Dedup;
using ARK.Core.Execution;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// Wires the <c>dedupe</c> verb. Presentation and wiring only.
/// </summary>
/// <remarks>
/// Report-only is the default policy <b>and</b> DRY-RUN is the default mode. A first run with no
/// flags analyses the set, prints what it found, and moves nothing — twice over.
/// </remarks>
public static class DedupeCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Build(
        IAnsiConsole console,
        Func<string, KeepPolicy, DedupReport> analyze,
        Func<DedupReport, bool, (QuarantinePlan Plan, ExecutionResult? Result)> quarantine)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(analyze);
        ArgumentNullException.ThrowIfNull(quarantine);

        var rootArgument = new Argument<string>("root") { Description = "Directory tree to deduplicate." };
        var applyOption = new Option<bool>("--apply")
        {
            Description = "Quarantine the redundant copies. Without this, nothing is moved."
        };
        var policyOption = new Option<KeepPolicy>("--policy")
        {
            Description = "Which copy to keep. Defaults to report-only, which decides nothing.",
            DefaultValueFactory = _ => KeepPolicy.ReportOnly,
        };
        var allOption = new Option<bool>("--all") { Description = "List every group, not just a summary." };

        var command = new Command("dedupe", "Find byte-identical game units and quarantine the redundant copies.");
        command.Add(rootArgument);
        command.Add(applyOption);
        command.Add(policyOption);
        command.Add(allOption);

        command.SetAction(parseResult =>
        {
            var root = parseResult.GetValue(rootArgument) ?? string.Empty;
            if (!Directory.Exists(root))
            {
                console.MarkupLineInterpolated($"[red]No such directory:[/] {root}");
                return 1;
            }

            var policy = parseResult.GetValue(policyOption);
            var apply = parseResult.GetValue(applyOption);
            var report = analyze(root, policy);

            DedupeRenderer.Render(console, report, parseResult.GetValue(allOption));

            if (policy == KeepPolicy.ReportOnly)
            {
                if (apply)
                {
                    console.MarkupLine("[yellow]--apply has no effect under the report-only policy.[/] Choose a --policy to decide what to keep.");
                }

                return 0;
            }

            var (plan, result) = quarantine(report, apply);
            DedupeRenderer.RenderQuarantine(console, plan, result, apply);
            return result is null || result.Success ? 0 : 1;
        });

        return command;
    }
}
