using ARK.Core.Execution;
using ARK.Core.Instances;

namespace ARK.Core.Renaming;

/// <summary>The plan to rename a batch, and what it refused.</summary>
/// <param name="Plan">Actions for the executor. Empty when nothing needs renaming.</param>
/// <param name="Renamed">Units that would change name.</param>
/// <param name="Staged">Units routed through a temporary name to break a cycle or a case-only clash.</param>
/// <param name="Refused">Units deliberately left alone, each with a reason.</param>
public sealed record RenamePlan(
    Plan Plan,
    IReadOnlyList<RenameDecision> Renamed,
    IReadOnlyList<RenameDecision> Staged,
    IReadOnlyList<(RenameDecision Decision, string Reason)> Refused);

/// <summary>
/// Turns rename decisions into an ordered, safe plan.
/// </summary>
/// <remarks>
/// <para>
/// <b>The planner projects the filesystem across the whole batch before planning any move.</b> A
/// batch that is individually valid and collectively destructive is the worst outcome available
/// here: if A must become B while B must become C, renaming A first destroys B.
/// </para>
/// <para>
/// Where a cycle exists — including a swap, and including a case-only rename, which is a cycle of
/// length one on a case-insensitive filesystem — one member is staged through a temporary name
/// first. Every staged step is itself an ordinary rename, so undo reverses the whole thing without
/// knowing a cycle was involved.
/// </para>
/// </remarks>
public static class RenamePlanner
{
    /// <summary>Builds the plan.</summary>
    /// <param name="report">The decisions to act on.</param>
    /// <param name="sessionId">Session id for the journal.</param>
    /// <param name="createdUtc">Plan timestamp.</param>
    /// <param name="activeDownloadDirectories">
    /// Directories showing in-flight transfer signals. Renaming a file an active client owns breaks
    /// the transfer, and on a completed torrent still seeding it silently breaks the seed.
    /// </param>
    public static RenamePlan Build(
        RenameReport report,
        string sessionId,
        DateTimeOffset createdUtc,
        IEnumerable<string>? activeDownloadDirectories = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var active = (activeDownloadDirectories ?? [])
            .Select(directory => directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToArray();

        var refused = new List<(RenameDecision, string)>();
        var pending = new List<Move>();

        foreach (var decision in report.Renames)
        {
            if (active.Any(directory => IsInside(decision.Path, directory)))
            {
                refused.Add((decision, "inside a directory showing active-download signals"));
                continue;
            }

            pending.Add(new Move(decision, decision.Path, RenamePaths.DestinationFor(decision.Path, decision.ProposedName)));
        }

        // Occupancy across the whole batch: every source currently holds a file, and any
        // destination already on disk that no one is vacating is an obstacle.
        var occupied = new HashSet<string>(pending.Select(move => move.Source), StringComparer.OrdinalIgnoreCase);
        foreach (var move in pending)
        {
            if (!occupied.Contains(move.Destination) && Exists(move.Destination))
            {
                occupied.Add(move.Destination);
            }
        }

        var blocked = pending
            .Where(move => Exists(move.Destination) && !pending.Any(other => Same(other.Source, move.Destination)))
            .ToArray();

        foreach (var move in blocked)
        {
            refused.Add((move.Decision, $"'{move.Decision.ProposedName}' already exists and nothing in this batch is vacating it"));
            occupied.Remove(move.Source);
        }

        var remaining = pending.Except(blocked).ToList();
        var actions = new List<PlannedAction>();
        var staged = new List<RenameDecision>();
        var deferred = new List<Move>();
        var temp = 0;

        while (remaining.Count > 0)
        {
            var free = remaining.FirstOrDefault(move => !occupied.Contains(move.Destination));

            if (free is not null)
            {
                actions.Add(Rename(free, free.Source, free.Destination));
                occupied.Remove(free.Source);
                occupied.Add(free.Destination);
                remaining.Remove(free);
                continue;
            }

            // Nothing can move directly, so every remaining move is waiting on another. Break the
            // deadlock by parking one of them out of the way.
            var cycle = remaining[0];
            var parked = RenamePaths.TempFor(cycle.Destination, temp++);

            actions.Add(Rename(cycle, cycle.Source, parked));
            occupied.Remove(cycle.Source);
            remaining.Remove(cycle);
            deferred.Add(cycle with { Source = parked });
            staged.Add(cycle.Decision);
        }

        // Parked files land last, once whatever was in their way has moved.
        foreach (var move in deferred)
        {
            actions.Add(Rename(move, move.Source, move.Destination));
        }

        return new RenamePlan(
            new Plan(sessionId, createdUtc, $"rename-{report.Mode.ToString().ToLowerInvariant()}", actions),
            pending.Except(blocked).Select(move => move.Decision).ToArray(),
            staged,
            refused);
    }

    private static PlannedAction Rename(Move move, string from, string to) =>
        new(ActionKind.Rename, from, to, $"{move.Decision.Source ?? "rename"}: {move.Decision.CurrentName} -> {move.Decision.ProposedName}");

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string path, string directory) =>
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private sealed record Move(RenameDecision Decision, string Source, string Destination);
}
