namespace ARK.Core.Execution;

/// <summary>
/// An ordered list of intended actions produced by an operation. A plan is inert: it can
/// be rendered (dry-run) or handed to the <see cref="Executor"/> (apply). The executed
/// plan, serialized, is the journal; inverted, it is an undo. This is the spine of the tool.
/// </summary>
/// <param name="SessionId">Identifier tying the plan to its journal file.</param>
/// <param name="CreatedUtc">When the plan was built.</param>
/// <param name="Operation">Name of the operation that produced the plan.</param>
/// <param name="Actions">The actions to carry out, in order.</param>
/// <param name="ReversesSessionId">
/// Set when this plan is the inverse of an earlier session, naming that session. It is carried
/// into the journal so an undo is auditable as an undo rather than looking like ordinary work.
/// </param>
public record Plan(
    string SessionId,
    DateTimeOffset CreatedUtc,
    string Operation,
    IReadOnlyList<PlannedAction> Actions,
    string? ReversesSessionId = null);
