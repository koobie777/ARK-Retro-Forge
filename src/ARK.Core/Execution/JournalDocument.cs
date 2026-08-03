namespace ARK.Core.Execution;

/// <summary>
/// The serialized form of an applied plan. Only actions that completed are recorded, and
/// the file is rewritten after each one succeeds, so a process killed mid-run leaves a
/// journal that reverses exactly the actions that finished.
/// </summary>
/// <param name="SessionId">The plan's session id; matches the journal file name.</param>
/// <param name="CreatedUtc">When the originating plan was built.</param>
/// <param name="Operation">Name of the operation that produced the plan.</param>
/// <param name="CompletedActions">Actions that ran to completion, in execution order.</param>
/// <param name="SchemaVersion">
/// Format version of this journal. A journal newer than the running build is refused rather than
/// best-guessed: a user who upgrades, undoes, then downgrades must not get a silently mangled
/// replay of their own collection.
/// </param>
/// <param name="ReversesSessionId">
/// Set when this journal records an undo, naming the session it reversed. This is what makes undo
/// auditable, and what lets a mistaken undo be seen rather than inferred.
/// </param>
public record JournalDocument(
    string SessionId,
    DateTimeOffset CreatedUtc,
    string Operation,
    IReadOnlyList<PlannedAction> CompletedActions,
    int SchemaVersion = JournalDocument.CurrentSchemaVersion,
    string? ReversesSessionId = null)
{
    /// <summary>Schema version this build writes and is able to read.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>True when this journal records the reversal of another session.</summary>
    public bool IsUndo => ReversesSessionId is { Length: > 0 };
}
