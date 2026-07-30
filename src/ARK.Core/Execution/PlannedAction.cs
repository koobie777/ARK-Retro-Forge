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
/// <see cref="ActionKind.Quarantine"/>. For <see cref="ActionKind.WriteText"/> it carries the
/// text content to write. Null for <see cref="ActionKind.CreateDirectory"/>.
/// </param>
/// <param name="Reason">Human-readable justification, preserved in the journal.</param>
public record PlannedAction(
    ActionKind Kind,
    string Source,
    string? Destination,
    string Reason);
