namespace ARK.Core.Execution;

/// <summary>
/// Turns a completed session into the plan that reverses it.
/// </summary>
/// <remarks>
/// Actions are inverted in <b>reverse order</b>. Forward order would undo a move before undoing
/// the directory creation that made room for it, and would restore a file before removing what
/// was written over it.
/// </remarks>
public static class JournalInverter
{
    /// <summary>
    /// Builds the inverse plan for <paramref name="journal"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// An action carries a kind that cannot be reversed. Undo never silently skips an action it
    /// cannot undo — a partial reversal presented as a complete one is worse than a refusal.
    /// </exception>
    public static Plan Invert(JournalDocument journal, string sessionId, DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var inverses = journal.CompletedActions
            .Reverse()
            .Select(Inverse)
            .ToArray();

        return new Plan(
            sessionId,
            createdUtc,
            $"undo:{journal.Operation}",
            inverses,
            journal.SessionId);
    }

    /// <summary>The inverse of one action.</summary>
    public static PlannedAction Inverse(PlannedAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        switch (action.Kind)
        {
            case ActionKind.Move:
            case ActionKind.Rename:
                return new PlannedAction(
                    action.Kind,
                    Destination(action),
                    action.Source,
                    $"Undo {action.Kind.ToString().ToLowerInvariant()}: {action.Reason}");

            case ActionKind.Quarantine:
                // Restores to the original path. The inverse of quarantining is an ordinary move
                // back out, not another quarantine.
                return new PlannedAction(
                    ActionKind.Move,
                    Destination(action),
                    action.Source,
                    $"Undo quarantine: {action.Reason}");

            case ActionKind.CreateDirectory:
                return new PlannedAction(
                    ActionKind.RemoveDirectory,
                    action.Source,
                    null,
                    $"Undo create directory: {action.Reason}");

            case ActionKind.WriteText:
                // Null prior content means there was no file here before the session, so the
                // faithful reversal is for it to be gone again rather than emptied.
                return action.PriorContent is null
                    ? new PlannedAction(
                        ActionKind.DeleteFile,
                        action.Source,
                        null,
                        $"Undo write (file did not exist before): {action.Reason}",
                        // What the session wrote. Carried so the delete can tell an untouched
                        // file from one the user has edited since — destroying that edit is the
                        // mirror image of restoring over it, and gets the same refusal.
                        action.Content)
                    : new PlannedAction(
                        ActionKind.WriteText,
                        action.Source,
                        null,
                        $"Undo write: {action.Reason}",
                        action.PriorContent,
                        // What the session put there. Carried so the precondition can tell
                        // "untouched since, safe to restore" from "already restored" from
                        // "someone has edited this since and restoring would discard their work".
                        action.Content);

            case ActionKind.RemoveDirectory:
            case ActionKind.DeleteFile:
                // These exist only as inverses. Undoing an undo is not a feature — the journal
                // record of the undo is the audit trail, and re-inverting a deletion would mean
                // inventing content that was never captured.
                throw new InvalidOperationException(
                    $"'{action.Kind}' is produced by inversion and is not itself invertible: {action.Source}");

            default:
                throw new InvalidOperationException(
                    $"No inverse is defined for '{action.Kind}': {action.Source}");
        }
    }

    private static string Destination(PlannedAction action) =>
        action.Destination
        ?? throw new InvalidOperationException($"{action.Kind} has no destination to invert: {action.Source}");
}
