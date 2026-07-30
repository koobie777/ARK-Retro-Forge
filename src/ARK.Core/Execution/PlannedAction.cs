namespace ARK.Core.Execution;

/// <summary>
/// A single intended filesystem mutation. Actions are inert data: constructing one
/// touches nothing. Only the <see cref="Executor"/> ever acts on them.
/// </summary>
/// <param name="Kind">The mutation to perform.</param>
/// <param name="Source">
/// The primary path the action operates on. For <see cref="ActionKind.CreateDirectory"/>
/// this is the directory to create; for <see cref="ActionKind.WriteText"/> it is the file
/// to write; otherwise it is the file being moved, renamed, or quarantined.
/// </param>
/// <param name="Destination">
/// The target path for <see cref="ActionKind.Move"/>, <see cref="ActionKind.Rename"/>, and
/// <see cref="ActionKind.Quarantine"/>. Null for other kinds. Destination is always a path and
/// never carries file content.
/// </param>
/// <param name="Reason">Human-readable justification, preserved in the journal.</param>
/// <param name="Content">
/// Text content written by <see cref="ActionKind.WriteText"/>. Null for every other kind.
/// Kept separate from <see cref="Destination"/> so a path field never doubles as a data field —
/// Phase 7 quarantine manifests depend on this separation.
/// </param>
public record PlannedAction(
    ActionKind Kind,
    string Source,
    string? Destination,
    string Reason,
    string? Content = null);
