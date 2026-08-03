using ARK.Core.Execution;
using Spectre.Console;

namespace ARK.Cli.Rendering;

/// <summary>Renders journal listings and undo previews. Rendering only — it decides nothing.</summary>
public static class UndoRenderer
{
    /// <summary>Writes the session list.</summary>
    public static void RenderList(IAnsiConsole console, IReadOnlyList<JournalSummary> sessions)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(sessions);

        if (sessions.Count == 0)
        {
            console.MarkupLine("[yellow]No sessions journaled yet.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Sessions[/]");
        table.AddColumn("Session");
        table.AddColumn("When");
        table.AddColumn("Operation");
        table.AddColumn(new TableColumn("Actions").RightAligned());
        table.AddColumn("State");

        foreach (var session in sessions)
        {
            table.AddRow(
                Markup.Escape(session.SessionId),
                session.CreatedUtc == default ? "-" : session.CreatedUtc.ToString("u"),
                Markup.Escape(session.Operation),
                session.ActionCount.ToString(),
                StateOf(session));
        }

        console.Write(table);
    }

    /// <summary>Writes one session's recorded actions.</summary>
    public static void RenderShow(IAnsiConsole console, JournalLoad load)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(load);

        if (!load.Succeeded)
        {
            console.MarkupLineInterpolated($"[red]{load.Fault}:[/] {load.Detail}");
            return;
        }

        var document = load.Document;
        console.MarkupLineInterpolated($"[bold]{document.SessionId}[/] — {document.Operation} (schema v{document.SchemaVersion})");
        if (document.IsUndo)
        {
            console.MarkupLineInterpolated($"[grey]Reverses session {document.ReversesSessionId}[/]");
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("#");
        table.AddColumn("Kind");
        table.AddColumn("Source");
        table.AddColumn("Destination");
        table.AddColumn("Reason");

        for (var i = 0; i < document.CompletedActions.Count; i++)
        {
            var action = document.CompletedActions[i];
            table.AddRow(
                (i + 1).ToString(),
                action.Kind.ToString(),
                Markup.Escape(action.Source),
                Markup.Escape(action.Destination ?? "-"),
                Markup.Escape(action.Reason));
        }

        console.Write(table);
    }

    /// <summary>Writes an undo preview, whether or not it was applied.</summary>
    public static void RenderPreview(IAnsiConsole console, UndoPreview preview, bool applied)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(preview);

        console.MarkupLineInterpolated($"[bold]Undo[/] {preview.SessionId}");

        if (preview.Refusal is not null)
        {
            console.MarkupLineInterpolated($"[red]Refused:[/] {preview.Refusal}");
            console.MarkupLine("[grey]Nothing was changed.[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Inverse actions[/]");
        table.AddColumn("#");
        table.AddColumn("Kind");
        table.AddColumn("From");
        table.AddColumn("To");
        table.AddColumn("State");

        for (var i = 0; i < preview.Steps.Count; i++)
        {
            var step = preview.Steps[i];
            table.AddRow(
                (i + 1).ToString(),
                step.Action.Kind.ToString(),
                Markup.Escape(step.Action.Source),
                Markup.Escape(step.Action.Destination ?? "-"),
                $"[{ColourOf(step.State)}]{step.State}[/]{(step.Detail is null ? string.Empty : " — " + Markup.Escape(step.Detail))}");
        }

        console.Write(table);

        if (preview.AlreadyDone.Count > 0)
        {
            console.MarkupLineInterpolated(
                $"[grey]{preview.AlreadyDone.Count} action(s) were already reversed by an earlier run and are skipped.[/]");
        }

        if (preview.LeftAlone.Count > 0)
        {
            console.MarkupLineInterpolated(
                $"[grey]{preview.LeftAlone.Count} action(s) left alone — restoring the prior state never means discarding what arrived since.[/]");
        }

        if (preview.Blocker is { } blocker)
        {
            // Stop, name the action, change nothing further. Never partially apply.
            console.MarkupLineInterpolated(
                $"[red]Blocked at {blocker.Action.Kind} {blocker.Action.Source}:[/] {blocker.Detail}");
            console.MarkupLine("[grey]Nothing was changed. Undo does not partially apply.[/]");
            return;
        }

        if (!applied)
        {
            console.MarkupLineInterpolated(
                $"[yellow]DRY-RUN.[/] {preview.Actionable.Count} action(s) would run. Nothing was touched. Re-run with --apply.");
        }
        else if (preview.Actionable.Count == 0)
        {
            console.MarkupLine("[green]Nothing to do — this session is already fully reversed.[/]");
        }
    }

    /// <summary>Writes the executor's result for an applied undo.</summary>
    public static void RenderResult(IAnsiConsole console, ExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(result);

        var failed = result.Results.Where(entry => entry.Status == ActionStatus.Failed).ToArray();
        foreach (var failure in failed)
        {
            console.MarkupLineInterpolated($"[red]Failed:[/] {failure.Action.Kind} {failure.Action.Source} — {failure.Detail}");
        }

        console.MarkupLineInterpolated(
            $"[green]Reversed {result.CompletedCount} action(s).[/] Journal: {result.JournalPath ?? "(none)"}");
    }

    private static string StateOf(JournalSummary session) => session switch
    {
        { Fault: not JournalFault.None } => $"[red]{session.Fault}[/]",
        { IsUndo: true } => $"[blue]undo of {Markup.Escape(session.ReversesSessionId!)}[/]",
        { IsReversed: true } => "[grey]reversed[/]",
        _ => "[green]applied[/]",
    };

    private static string ColourOf(PreconditionState state) => state switch
    {
        PreconditionState.Ready => "green",
        PreconditionState.AlreadyReversed or PreconditionState.LeftAlone => "grey",
        _ => "red",
    };
}
