using System.CommandLine;
using ARK.Cli.Rendering;
using ARK.Core.Execution;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// Wires <c>journal list</c>, <c>journal show</c>, and <c>undo</c>. Presentation only.
/// </summary>
public static class UndoCommand
{
    /// <summary>Builds the <c>journal</c> command tree.</summary>
    public static Command BuildJournal(IAnsiConsole console, JournalStore journals)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(journals);

        var journal = new Command("journal", "Inspect executed sessions.");

        var list = new Command("list", "List sessions, newest first.");
        list.SetAction(_ =>
        {
            UndoRenderer.RenderList(console, journals.List());
            return 0;
        });

        var sessionArgument = new Argument<string>("session-id") { Description = "Session to show." };
        var show = new Command("show", "Show the actions recorded in one session.");
        show.Add(sessionArgument);
        show.SetAction(parseResult =>
        {
            var load = journals.Read(parseResult.GetValue(sessionArgument) ?? string.Empty);
            UndoRenderer.RenderShow(console, load);
            return load.Succeeded ? 0 : 1;
        });

        journal.Add(list);
        journal.Add(show);
        return journal;
    }

    /// <summary>Builds the <c>undo</c> command.</summary>
    /// <remarks>
    /// DRY-RUN by default. Undo writes to user files, so it gets the same treatment as every other
    /// operation that does: nothing happens without <c>--apply</c>.
    /// </remarks>
    public static Command BuildUndo(IAnsiConsole console, UndoService undo)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(undo);

        var sessionArgument = new Argument<string>("session-id") { Description = "Session to reverse." };
        var applyOption = new Option<bool>("--apply") { Description = "Actually reverse the session. Without this, nothing is touched." };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Allow a restore to overwrite a file that has appeared at its destination."
        };

        var command = new Command("undo", "Reverse a journaled session.");
        command.Add(sessionArgument);
        command.Add(applyOption);
        command.Add(forceOption);

        command.SetAction(parseResult =>
        {
            var sessionId = parseResult.GetValue(sessionArgument) ?? string.Empty;
            var force = parseResult.GetValue(forceOption);

            if (!parseResult.GetValue(applyOption))
            {
                var preview = undo.Prepare(sessionId, force);
                UndoRenderer.RenderPreview(console, preview, applied: false);
                return preview.Refusal is null && preview.Blocker is null ? 0 : 1;
            }

            var (applied, result) = undo.Apply(sessionId, force);
            UndoRenderer.RenderPreview(console, applied, applied: true);

            if (result is not null)
            {
                UndoRenderer.RenderResult(console, result);
                return result.Success ? 0 : 1;
            }

            return applied.Refusal is null && applied.Blocker is null ? 0 : 1;
        });

        return command;
    }
}
