using System.CommandLine;
using ARK.Cli.Rendering;
using ARK.Core.Verification;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// Wires the <c>verify</c> verb. Presentation and wiring only — the five-state model, hashing and
/// caching all live in Core.
/// </summary>
/// <remarks>
/// Read-only, like scan. There is no <c>--apply</c> because there is nothing to apply: verification
/// reports, and quarantine arrives with a later phase once undo exists.
/// </remarks>
public static class VerifyCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Build(
        IAnsiConsole console,
        Func<string, IProgress<VerificationProgress>?, CancellationToken, VerificationReport> verify)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(verify);

        var rootArgument = new Argument<string>("root")
        {
            Description = "Directory tree to verify. Read only — nothing is written or moved."
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit the report as JSON to stdout instead of rendering tables."
        };

        var allOption = new Option<bool>("--all")
        {
            Description = "List every unit in every state, not just a summary."
        };

        var command = new Command("verify", "Hash a set and sort it into the five verification states.");
        command.Add(rootArgument);
        command.Add(jsonOption);
        command.Add(allOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var root = parseResult.GetValue(rootArgument) ?? string.Empty;
            if (!Directory.Exists(root))
            {
                console.MarkupLineInterpolated($"[red]No such directory:[/] {root}");
                return Task.FromResult(1);
            }

            var json = parseResult.GetValue(jsonOption);

            // A full pass takes hours, so progress is mandatory — but it must not pollute stdout
            // when the caller asked for machine-readable output.
            var report = json
                ? verify(root, null, cancellationToken)
                : VerifyRenderer.WithProgress(console, progress => verify(root, progress, cancellationToken));

            if (json)
            {
                Console.Out.WriteLine(VerificationJson.Serialize(report));
            }
            else
            {
                VerifyRenderer.Render(console, report, parseResult.GetValue(allOption));
            }

            return Task.FromResult(0);
        });

        return command;
    }
}
