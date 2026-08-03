namespace ARK.Core.Execution;

/// <summary>What a precondition check concluded about one inverse action.</summary>
public enum PreconditionState
{
    /// <summary>Safe to apply.</summary>
    Ready = 0,

    /// <summary>
    /// The world already looks the way this inverse would leave it. A previous undo run got this
    /// far before being interrupted, so re-running resumes rather than double-applying.
    /// </summary>
    AlreadyReversed,

    /// <summary>
    /// Deliberately not applied, and not because anything went wrong — a directory the user has
    /// filled since is left exactly as it is. Kept distinct from
    /// <see cref="AlreadyReversed"/> so the report does not claim an earlier run did something it
    /// never did.
    /// </summary>
    LeftAlone,

    /// <summary>Cannot be applied safely. The run stops here.</summary>
    Blocked,
}

/// <summary>One inverse action and what the current filesystem says about it.</summary>
/// <param name="Action">The inverse.</param>
/// <param name="State">Whether it can proceed.</param>
/// <param name="Detail">Why, when it cannot.</param>
public sealed record UndoStep(PlannedAction Action, PreconditionState State, string? Detail);

/// <summary>The outcome of preparing an undo.</summary>
/// <param name="SessionId">The session being reversed.</param>
/// <param name="Plan">The inverse plan, or null when the session was refused outright.</param>
/// <param name="Steps">Per-action precondition results, in the order they would run.</param>
/// <param name="Refusal">Why the whole session was refused, when it was.</param>
public sealed record UndoPreview(
    string SessionId,
    Plan? Plan,
    IReadOnlyList<UndoStep> Steps,
    string? Refusal)
{
    /// <summary>True when the undo can proceed.</summary>
    public bool CanApply => Refusal is null && Steps.All(step => step.State != PreconditionState.Blocked);

    /// <summary>Steps that would actually change something.</summary>
    public IReadOnlyList<UndoStep> Actionable =>
        Steps.Where(step => step.State == PreconditionState.Ready).ToArray();

    /// <summary>Steps already reversed by an earlier, interrupted run.</summary>
    public IReadOnlyList<UndoStep> AlreadyDone =>
        Steps.Where(step => step.State == PreconditionState.AlreadyReversed).ToArray();

    /// <summary>Steps deliberately not applied, such as a directory the user has since filled.</summary>
    public IReadOnlyList<UndoStep> LeftAlone =>
        Steps.Where(step => step.State == PreconditionState.LeftAlone).ToArray();

    /// <summary>The first step that blocks the run, if any.</summary>
    public UndoStep? Blocker => Steps.FirstOrDefault(step => step.State == PreconditionState.Blocked);
}

/// <summary>
/// Reverses a journaled session.
/// </summary>
/// <remarks>
/// <para>
/// Undo verifies before it acts and never assumes the world is unchanged. When a precondition
/// fails it stops, names the action and the reason, and changes nothing further — it does not
/// partially apply, skip and continue, or guess.
/// </para>
/// <para>
/// DRY-RUN is the default here as everywhere. Undo writes to user files and gets no exemption.
/// </para>
/// </remarks>
public sealed class UndoService
{
    private readonly JournalStore _journals;
    private readonly Executor _executor;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>Creates an undo service.</summary>
    public UndoService(JournalStore journals, Executor executor, Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(journals);
        ArgumentNullException.ThrowIfNull(executor);
        _journals = journals;
        _executor = executor;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Builds the inverse plan and checks it against the current filesystem. Touches nothing.
    /// </summary>
    /// <param name="sessionId">Session to reverse.</param>
    /// <param name="force">
    /// Allow an inverse to overwrite a file that has appeared at its destination. Explicit,
    /// per-run, and never the default — restoring over a different file is data loss wearing a
    /// safe name.
    /// </param>
    public UndoPreview Prepare(string sessionId, bool force = false)
    {
        var load = _journals.Read(sessionId);
        if (!load.Succeeded)
        {
            return new UndoPreview(sessionId, null, [], load.Detail ?? load.Fault.ToString());
        }

        if (load.Document.IsUndo)
        {
            return new UndoPreview(
                sessionId, null, [],
                $"Session '{sessionId}' is itself an undo of '{load.Document.ReversesSessionId}'. Undo-of-undo is not a feature; the journal is the audit trail.");
        }

        // Deliberately no "this was already reversed" refusal. A run killed halfway leaves the
        // session marked reversed while half its actions still stand, and refusing on that flag
        // would make the interrupted case unrecoverable. The per-action preconditions below
        // already distinguish "done" from "to do", so re-running resumes instead of repeating.
        Plan plan;
        try
        {
            plan = JournalInverter.Invert(load.Document, NewSessionId(sessionId), _now());
        }
        catch (InvalidOperationException ex)
        {
            // An action with no inverse refuses the whole session rather than being skipped.
            return new UndoPreview(sessionId, null, [], ex.Message);
        }

        // Checked against a projection of the filesystem as each earlier inverse leaves it, not
        // against the world as it stands now. Undoing A->B then B->C means restoring C->B before
        // B->A; judging both against the starting state would call the second one impossible, and
        // would let a directory be judged "not empty" by the very files a later step removes.
        var projection = new Projection();
        var steps = plan.Actions.Select(action => Check(action, force, projection)).ToArray();
        return new UndoPreview(sessionId, plan, steps, null);
    }

    /// <summary>
    /// Applies the reversal. Actions already reversed by an interrupted run are dropped, so
    /// re-running resumes instead of double-applying.
    /// </summary>
    public (UndoPreview Preview, ExecutionResult? Result) Apply(string sessionId, bool force = false)
    {
        var preview = Prepare(sessionId, force);
        if (!preview.CanApply || preview.Plan is null)
        {
            return (preview, null);
        }

        var actionable = preview.Actionable.Select(step => step.Action).ToArray();
        if (actionable.Length == 0)
        {
            return (preview, null);
        }

        return (preview, _executor.Execute(preview.Plan with { Actions = actionable }, apply: true));
    }

    private static UndoStep Check(PlannedAction action, bool force, Projection projection)
    {
        switch (action.Kind)
        {
            case ActionKind.Move:
            case ActionKind.Rename:
            {
                var destination = action.Destination!;
                var sourceExists = projection.Exists(action.Source);
                var destinationExists = projection.Exists(destination);

                if (!sourceExists)
                {
                    // Gone from where the journal says it is, but present where the inverse would
                    // put it: an earlier undo run already did this one.
                    return destinationExists
                        ? Done(action, "already restored")
                        : Blocked(action, $"nothing to restore from: {action.Source}");
                }

                if (destinationExists && !force)
                {
                    return Blocked(
                        action,
                        $"something already exists at {destination}; restoring over it would lose that file. Re-run with --force to overwrite.");
                }

                projection.Move(action.Source, destination);
                return Ready(action);
            }

            case ActionKind.RemoveDirectory:
                if (!projection.Exists(action.Source))
                {
                    return Done(action, "directory already removed");
                }

                if (!projection.IsEmptyDirectory(action.Source))
                {
                    // A directory the user has filled since is left exactly alone.
                    return new UndoStep(action, PreconditionState.LeftAlone, "directory is not empty");
                }

                projection.Remove(action.Source);
                return Ready(action);

            case ActionKind.DeleteFile:
            {
                if (!projection.Exists(action.Source))
                {
                    return Done(action, "file already removed");
                }

                // Symmetric with the restore path: if the file no longer holds what the session
                // wrote, someone has changed it, and deleting it would destroy that change.
                var content = ReadOrNull(action.Source);
                if (action.Content is not null &&
                    !string.Equals(content, action.Content, StringComparison.Ordinal) &&
                    !force)
                {
                    return Blocked(
                        action,
                        $"{action.Source} has been modified since the session created it; deleting it would discard that change. Re-run with --force to remove it anyway.");
                }

                projection.Remove(action.Source);
                return Ready(action);
            }

            case ActionKind.WriteText:
            {
                if (!projection.Exists(action.Source))
                {
                    return Blocked(action, $"nothing to restore over: {action.Source} no longer exists");
                }

                var current = ReadOrNull(action.Source);

                // Already holds what we would restore: an earlier run got here.
                if (string.Equals(current, action.Content, StringComparison.Ordinal))
                {
                    return Done(action, "content already restored");
                }

                // Still holds exactly what the session wrote, so nothing has been edited since.
                if (action.PriorContent is null || string.Equals(current, action.PriorContent, StringComparison.Ordinal))
                {
                    return Ready(action);
                }

                return force
                    ? Ready(action)
                    : Blocked(
                        action,
                        $"{action.Source} has been modified since the session ran; restoring would discard that change. Re-run with --force to overwrite.");
            }

            default:
                return Blocked(action, $"no precondition rule for '{action.Kind}'");
        }
    }

    /// <summary>
    /// The filesystem as the inverse plan will leave it, step by step. Only the deltas are held;
    /// anything untouched is answered from the real filesystem.
    /// </summary>
    private sealed class Projection
    {
        private readonly HashSet<string> _created = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);

        public bool Exists(string path) =>
            _created.Contains(path) ||
            (!_removed.Contains(path) && (File.Exists(path) || Directory.Exists(path)));

        public void Move(string source, string destination)
        {
            _removed.Add(source);
            _created.Remove(source);
            _created.Add(destination);
            _removed.Remove(destination);
        }

        public void Remove(string path)
        {
            _removed.Add(path);
            _created.Remove(path);
        }

        public bool IsEmptyDirectory(string path)
        {
            if (_created.Any(entry => IsDirectlyInside(entry, path)))
            {
                return false;
            }

            if (!Directory.Exists(path))
            {
                return true;
            }

            return Directory
                .EnumerateFileSystemEntries(path)
                .All(entry => _removed.Contains(entry));
        }

        private static bool IsDirectlyInside(string entry, string directory) =>
            string.Equals(Path.GetDirectoryName(entry), directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static UndoStep Ready(PlannedAction action) => new(action, PreconditionState.Ready, null);

    private static UndoStep Done(PlannedAction action, string detail) => new(action, PreconditionState.AlreadyReversed, detail);

    private static UndoStep Blocked(PlannedAction action, string detail) => new(action, PreconditionState.Blocked, detail);

    private string NewSessionId(string sessionId) => $"undo-{sessionId}-{_now():yyyyMMddHHmmssfff}";
}
