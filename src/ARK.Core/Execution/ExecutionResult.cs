namespace ARK.Core.Execution;

/// <summary>The disposition of a single action after an <see cref="Executor"/> pass.</summary>
public enum ActionStatus
{
    /// <summary>Dry-run: the action appears able to run against the current filesystem.</summary>
    Feasible,

    /// <summary>Dry-run: the action cannot run as described against the current filesystem.</summary>
    Blocked,

    /// <summary>Apply: the action ran successfully and was journaled.</summary>
    Completed,

    /// <summary>Apply: the action threw while running; execution stopped here.</summary>
    Failed,

    /// <summary>Apply: an earlier action failed, so this one was never attempted.</summary>
    Skipped
}

/// <summary>The outcome of a single action within an execution.</summary>
/// <param name="Action">The action that was assessed or run.</param>
/// <param name="Status">Its disposition.</param>
/// <param name="Detail">Optional explanation (feasibility note or failure message).</param>
public record ActionResult(PlannedAction Action, ActionStatus Status, string? Detail);

/// <summary>The result of a single <see cref="Executor.Execute"/> call.</summary>
/// <param name="SessionId">The plan's session id.</param>
/// <param name="Applied">True if this was an apply pass; false for dry-run.</param>
/// <param name="JournalPath">Path to the journal written, or null (dry-run, or nothing completed).</param>
/// <param name="Results">Per-action outcomes, in plan order.</param>
public record ExecutionResult(
    string SessionId,
    bool Applied,
    string? JournalPath,
    IReadOnlyList<ActionResult> Results)
{
    /// <summary>True when every action is feasible (dry-run) or completed (apply).</summary>
    public bool Success => Results.All(r => r.Status is ActionStatus.Feasible or ActionStatus.Completed);

    /// <summary>Number of actions that actually ran to completion.</summary>
    public int CompletedCount => Results.Count(r => r.Status == ActionStatus.Completed);
}
