using System.CommandLine;
using ARK.Cli.Rendering;
using ARK.Core.Diagnostics;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// Wires the <c>medical-bay</c> verb. Presentation only: it obtains the report from Core and hands
/// it to the JSON serializer or the renderer based on <c>--json</c>. It contains no business logic
/// and makes no decision derived from the report's contents.
/// </summary>
public static class MedicalBayCommand
{
    /// <summary>
    /// Builds the command. <paramref name="reportFactory"/> produces the report (Core composition
    /// lives in the composition root); <paramref name="console"/> receives the human render.
    /// </summary>
    public static Command Build(IAnsiConsole console, Func<MedicalBayReport> reportFactory)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(reportFactory);

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit the report as JSON to stdout instead of rendering tables."
        };

        var command = new Command("medical-bay", "Report tool, DAT, and instance status.");
        command.Add(jsonOption);

        command.SetAction(parseResult =>
        {
            var report = reportFactory();
            if (parseResult.GetValue(jsonOption))
            {
                Console.Out.WriteLine(MedicalBayJson.Serialize(report));
            }
            else
            {
                MedicalBayRenderer.Render(console, report);
            }

            return 0;
        });

        return command;
    }
}
