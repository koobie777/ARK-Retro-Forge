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
public record JournalDocument(
    string SessionId,
    DateTimeOffset CreatedUtc,
    string Operation,
    IReadOnlyList<PlannedAction> CompletedActions);
