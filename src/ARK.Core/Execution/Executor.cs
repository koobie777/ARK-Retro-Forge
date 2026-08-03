using System.Text.Json;
using ARK.Core.Instances;
using ARK.Core.Serialization;
using Serilog;

namespace ARK.Core.Execution;

/// <summary>
/// The single type in the solution permitted to mutate the filesystem — the only caller of
/// <c>File.Move</c>, <c>File.Delete</c>, <c>File.WriteAllText</c>, and
/// <c>Directory.CreateDirectory</c>. Operations return a <see cref="Plan"/>; the executor is
/// what turns intent into effect. On apply it journals each action the moment it succeeds, so
/// a process killed mid-run leaves a journal that reverses exactly what completed.
/// </summary>
public sealed class Executor
{
    private readonly InstancePaths _paths;
    private readonly ILogger _log;

    /// <summary>Creates an executor that journals under <paramref name="paths"/>.</summary>
    /// <param name="paths">The instance whose journal directory receives session journals.</param>
    /// <param name="logger">Logger to use; defaults to the global Serilog logger.</param>
    public Executor(InstancePaths paths, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        _log = logger ?? Log.Logger;
    }

    /// <summary>
    /// Runs <paramref name="plan"/>. With <paramref name="apply"/> false this is a dry-run:
    /// every action is assessed for feasibility, nothing is touched, and no journal is written.
    /// With <paramref name="apply"/> true the actions run in order and each success is journaled
    /// immediately; the first failure halts execution.
    /// </summary>
    public ExecutionResult Execute(Plan plan, bool apply)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return apply ? Apply(plan) : DryRun(plan);
    }

    private ExecutionResult DryRun(Plan plan)
    {
        var results = new List<ActionResult>(plan.Actions.Count);
        foreach (var action in plan.Actions)
        {
            var (feasible, detail) = AssessFeasibility(action);
            results.Add(new ActionResult(action, feasible ? ActionStatus.Feasible : ActionStatus.Blocked, detail));
        }

        _log.Information(
            "Dry-run {Operation} ({SessionId}): {Count} action(s) assessed, filesystem untouched",
            plan.Operation, plan.SessionId, plan.Actions.Count);

        return new ExecutionResult(plan.SessionId, Applied: false, JournalPath: null, results);
    }

    private ExecutionResult Apply(Plan plan)
    {
        var results = new List<ActionResult>(plan.Actions.Count);
        var completed = new List<PlannedAction>(plan.Actions.Count);
        var journalPath = _paths.JournalFileFor(plan.SessionId);
        var halted = false;

        foreach (var action in plan.Actions)
        {
            if (halted)
            {
                results.Add(new ActionResult(action, ActionStatus.Skipped, "Skipped: an earlier action failed"));
                continue;
            }

            try
            {
                // Perform returns the action as it should be journaled: a WriteText comes back
                // carrying whatever it displaced, which is the only way it can ever be reversed.
                completed.Add(Perform(action));
                WriteJournal(plan, completed, journalPath); // incremental: persist before moving on
                results.Add(new ActionResult(action, ActionStatus.Completed, null));
                _log.Information("Applied {Kind}: {Source} -> {Destination}", action.Kind, action.Source, action.Destination);
            }
            catch (Exception ex)
            {
                halted = true;
                results.Add(new ActionResult(action, ActionStatus.Failed, ex.Message));
                _log.Error(ex, "Action failed ({Kind}: {Source}); halting execution", action.Kind, action.Source);
            }
        }

        return new ExecutionResult(
            plan.SessionId,
            Applied: true,
            completed.Count > 0 ? journalPath : null,
            results);
    }

    private static PlannedAction Perform(PlannedAction action)
    {
        switch (action.Kind)
        {
            case ActionKind.CreateDirectory:
                Directory.CreateDirectory(action.Source);
                return action;

            case ActionKind.Move:
            case ActionKind.Rename:
            case ActionKind.Quarantine:
                File.Move(action.Source, RequireDestination(action));
                return action;

            case ActionKind.WriteText:
            {
                // Captured before the write, because afterwards it is gone. Null records that
                // there was no file here, which makes the inverse "remove it" rather than
                // "restore nothing".
                var prior = File.Exists(action.Source) ? File.ReadAllText(action.Source) : null;
                File.WriteAllText(action.Source, action.Content ?? string.Empty);
                return action with { PriorContent = prior };
            }

            case ActionKind.RemoveDirectory:
                // Only when empty. A directory the user has filled since is left exactly alone.
                if (Directory.Exists(action.Source) && Directory.EnumerateFileSystemEntries(action.Source).Any())
                {
                    throw new IOException($"Directory is not empty, refusing to remove: {action.Source}");
                }

                if (Directory.Exists(action.Source))
                {
                    Directory.Delete(action.Source);
                }

                return action;

            case ActionKind.DeleteFile:
                File.Delete(action.Source);
                return action;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "Unsupported action kind");
        }
    }

    private void WriteJournal(Plan plan, IReadOnlyList<PlannedAction> completed, string journalPath)
    {
        // Resolves the bootstrap cycle: the instance-provision plan itself creates journal/,
        // so the journal directory cannot be assumed to exist when the plan's first action is
        // journaled. The executor therefore ensures journal/ ahead of every write, independent
        // of the plan. This CreateDirectory is idempotent, so the plan's own CreateDirectory for
        // journal/ later runs as a no-op and is journaled normally.
        Directory.CreateDirectory(_paths.Journal);
        var document = new JournalDocument(
            plan.SessionId,
            plan.CreatedUtc,
            plan.Operation,
            completed,
            JournalDocument.CurrentSchemaVersion,
            plan.ReversesSessionId);
        File.WriteAllText(journalPath, JsonSerializer.Serialize(document, ArkJson.Write));
    }

    private static string RequireDestination(PlannedAction action) =>
        action.Destination
        ?? throw new ArgumentException($"{action.Kind} requires a destination for '{action.Source}'", nameof(action));

    private static (bool Feasible, string? Detail) AssessFeasibility(PlannedAction action)
    {
        switch (action.Kind)
        {
            case ActionKind.CreateDirectory:
                return Directory.Exists(action.Source)
                    ? (true, "Directory already exists")
                    : (true, null);

            case ActionKind.Move:
            case ActionKind.Rename:
            case ActionKind.Quarantine:
                if (action.Destination is null)
                {
                    return (false, "No destination specified");
                }
                if (!File.Exists(action.Source) && !Directory.Exists(action.Source))
                {
                    return (false, $"Source not found: {action.Source}");
                }
                if (File.Exists(action.Destination) || Directory.Exists(action.Destination))
                {
                    return (false, $"Destination already exists: {action.Destination}");
                }
                return (true, null);

            case ActionKind.WriteText:
                // Overwriting is feasible now that the executor records what it displaces, so an
                // overwrite is reversible rather than a one-way door.
                return File.Exists(action.Source)
                    ? (true, "Overwrites an existing file; prior content is captured for undo")
                    : (true, null);

            case ActionKind.RemoveDirectory:
                if (!Directory.Exists(action.Source))
                {
                    return (true, "Directory already absent");
                }

                return Directory.EnumerateFileSystemEntries(action.Source).Any()
                    ? (false, $"Directory is not empty: {action.Source}")
                    : (true, null);

            case ActionKind.DeleteFile:
                return File.Exists(action.Source)
                    ? (true, null)
                    : (true, "File already absent");

            default:
                return (false, "Unsupported action kind");
        }
    }
}
