using System.Text.Json;
using System.Text.Json.Serialization;
using ARK.Core.Instances;
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
    private static readonly JsonSerializerOptions JournalJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

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
                Perform(action);
                completed.Add(action);
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

    private void Perform(PlannedAction action)
    {
        switch (action.Kind)
        {
            case ActionKind.CreateDirectory:
                Directory.CreateDirectory(action.Source);
                break;

            case ActionKind.Move:
            case ActionKind.Rename:
            case ActionKind.Quarantine:
                File.Move(action.Source, RequireDestination(action));
                break;

            case ActionKind.WriteText:
                File.WriteAllText(action.Source, action.Destination ?? string.Empty);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "Unsupported action kind");
        }
    }

    private void WriteJournal(Plan plan, IReadOnlyList<PlannedAction> completed, string journalPath)
    {
        Directory.CreateDirectory(_paths.Journal);
        var document = new JournalDocument(plan.SessionId, plan.CreatedUtc, plan.Operation, completed);
        File.WriteAllText(journalPath, JsonSerializer.Serialize(document, JournalJson));
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
                return File.Exists(action.Source)
                    ? (false, $"Target file already exists: {action.Source}")
                    : (true, null);

            default:
                return (false, "Unsupported action kind");
        }
    }
}
