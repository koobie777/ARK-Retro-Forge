using System.CommandLine;
using ARK.Cli.Rendering;
using ARK.Core.Execution;
using ARK.Core.Renaming;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// Wires the <c>rename</c> and <c>organize</c> verbs. Presentation and wiring only.
/// </summary>
/// <remarks>
/// DRY-RUN by default. Renaming is what v1 destroyed collections doing, so nothing changes without
/// <c>--apply</c>, and <c>--normalize</c> — the only mode that reformats a name ARK cannot verify —
/// has to be asked for explicitly.
/// </remarks>
public static class RenameCommand
{
    /// <summary>Builds the <c>rename</c> command.</summary>
    public static Command BuildRename(
        IAnsiConsole console,
        Func<string, RenameMode, RenameReport> decide,
        Func<RenameReport, bool, (RenamePlan Plan, ExecutionResult? Result)> apply)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(decide);
        ArgumentNullException.ThrowIfNull(apply);

        var rootArgument = new Argument<string>("root") { Description = "Directory tree to rename within." };
        var applyOption = new Option<bool>("--apply") { Description = "Carry the plan out. Without this, nothing is renamed." };
        var normalizeOption = new Option<bool>("--normalize")
        {
            Description = "Reformat each unit's own name instead of taking it from the DAT. Repair, not identification.",
        };
        var allOption = new Option<bool>("--all") { Description = "List every decision, not just a summary." };

        var command = new Command("rename", "Give units their canonical names.");
        command.Add(rootArgument);
        command.Add(applyOption);
        command.Add(normalizeOption);
        command.Add(allOption);

        command.SetAction(parseResult =>
        {
            var root = parseResult.GetValue(rootArgument) ?? string.Empty;
            if (!Directory.Exists(root))
            {
                console.MarkupLineInterpolated($"[red]No such directory:[/] {root}");
                return 1;
            }

            var mode = parseResult.GetValue(normalizeOption) ? RenameMode.Normalize : RenameMode.Canonicalize;
            var report = decide(root, mode);

            RenameRenderer.Render(console, report, parseResult.GetValue(allOption));

            if (report.Renames.Count == 0)
            {
                return 0;
            }

            var applied = parseResult.GetValue(applyOption);
            var (plan, result) = apply(report, applied);
            RenameRenderer.RenderPlan(console, plan, result, applied);
            return result is null || result.Success ? 0 : 1;
        });

        return command;
    }

    /// <summary>Builds the <c>organize</c> command.</summary>
    public static Command BuildOrganize(
        IAnsiConsole console,
        Func<string, bool, (OrganizePlan Plan, ExecutionResult? Result)> organize)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(organize);

        var rootArgument = new Argument<string>("root") { Description = "Directory tree to organize." };
        var applyOption = new Option<bool>("--apply") { Description = "Carry the plan out. Without this, nothing moves." };

        var command = new Command("organize", "File units into directories named after their DAT.");
        command.Add(rootArgument);
        command.Add(applyOption);

        command.SetAction(parseResult =>
        {
            var root = parseResult.GetValue(rootArgument) ?? string.Empty;
            if (!Directory.Exists(root))
            {
                console.MarkupLineInterpolated($"[red]No such directory:[/] {root}");
                return 1;
            }

            var applied = parseResult.GetValue(applyOption);
            var (plan, result) = organize(root, applied);
            RenameRenderer.RenderOrganize(console, plan, result, applied);
            return result is null || result.Success ? 0 : 1;
        });

        return command;
    }
}
