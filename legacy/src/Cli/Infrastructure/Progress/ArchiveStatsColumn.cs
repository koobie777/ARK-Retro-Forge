using Spectre.Console;
using Spectre.Console.Rendering;

namespace ARK.Cli.Infrastructure.Progress;

internal sealed class ArchiveStatsColumn : ProgressColumn
{
    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        ArchiveProgressState state;
        try
        {
            state = task.State.Get<ArchiveProgressState>(ArchiveProgressState.StateKey);
        }
        catch (KeyNotFoundException)
        {
            return Text.Empty;
        }
        catch (InvalidOperationException)
        {
            return Text.Empty;
        }

        var remaining = state.Remaining;
        var elapsed = state.Elapsed;
        var throughput = state.Completed > 0 && elapsed.TotalMinutes > 0
            ? state.Completed / elapsed.TotalMinutes
            : 0;
        var throughputText = throughput > 0 ? $"{throughput:0.0}/min" : "--/min";

        return new Markup(
            $"[green]+{state.Completed}[/]  [red]-{state.Failed}[/]  [yellow]~{remaining}[/]  [grey58]{elapsed:hh\\:mm\\:ss} ({throughputText})[/]");
    }
}
