using System.CommandLine;
using ARK.Cli.Rendering;
using ARK.Core.Dedup;
using ARK.Core.Execution;
using ARK.Core.Policy;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// Wires the <c>curate</c> verb. Presentation and wiring only.
/// </summary>
/// <remarks>
/// Report-only is the default policy and DRY-RUN the default mode. Everything curation removes is
/// a distinct release with different bytes — unlike a deduplicated copy, it cannot be recovered
/// from a twin — so a first run with no flags decides nothing and moves nothing.
/// </remarks>
public static class CurateCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Build(
        IAnsiConsole console,
        Func<string, VariantPolicy, CurationReport> analyze,
        Func<CurationReport, bool, (QuarantinePlan Plan, ExecutionResult? Result)> quarantine,
        Func<CurationReport, string?, bool, (SortPlan Plan, ExecutionResult? Result)> sort,
        VariantPolicy configured)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(analyze);
        ArgumentNullException.ThrowIfNull(quarantine);
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(configured);

        var rootArgument = new Argument<string>("root") { Description = "Directory tree to curate." };
        var policyOption = new Option<string?>("--policy")
        {
            Description = $"Preset to apply: {string.Join(", ", VariantPresets.All.Keys)}. Defaults to the configured policy.",
        };
        var applyOption = new Option<bool>("--apply") { Description = "Carry the plan out. Without this, nothing moves." };
        var sortOption = new Option<bool>("--sort")
        {
            Description = "Move non-kept variants into a subfolder instead of quarantining them. Organization, not removal.",
        };
        var subfolderOption = new Option<string?>("--subfolder")
        {
            Description = $"Subfolder for --sort. Defaults to '{SortPlanner.DefaultSubfolder}'.",
        };
        var allOption = new Option<bool>("--all") { Description = "List every group, not just a summary." };

        var command = new Command("curate", "Group release variants and apply a keep policy.");
        command.Add(rootArgument);
        command.Add(policyOption);
        command.Add(applyOption);
        command.Add(sortOption);
        command.Add(subfolderOption);
        command.Add(allOption);

        command.SetAction(parseResult =>
        {
            var root = parseResult.GetValue(rootArgument) ?? string.Empty;
            if (!Directory.Exists(root))
            {
                console.MarkupLineInterpolated($"[red]No such directory:[/] {root}");
                return 1;
            }

            var requested = parseResult.GetValue(policyOption);
            var policy = configured;
            if (requested is { Length: > 0 })
            {
                // An unrecognized preset is refused rather than falling back to one that removes
                // files — a typo must never curate a collection.
                var found = VariantPresets.Find(requested);
                if (found is null)
                {
                    console.MarkupLineInterpolated($"[red]Unknown policy '{requested}'.[/] Available: {string.Join(", ", VariantPresets.All.Keys)}");
                    return 1;
                }

                policy = found;
            }

            var report = analyze(root, policy);
            CurateRenderer.Render(console, report, parseResult.GetValue(allOption));

            if (report.Removable.Count == 0)
            {
                return 0;
            }

            var apply = parseResult.GetValue(applyOption);

            if (parseResult.GetValue(sortOption))
            {
                var (plan, result) = sort(report, parseResult.GetValue(subfolderOption), apply);
                CurateRenderer.RenderSort(console, plan, result, apply);
                return result is null || result.Success ? 0 : 1;
            }

            var (quarantinePlan, quarantineResult) = quarantine(report, apply);
            CurateRenderer.RenderQuarantine(console, quarantinePlan, quarantineResult, apply);
            return quarantineResult is null || quarantineResult.Success ? 0 : 1;
        });

        return command;
    }
}
