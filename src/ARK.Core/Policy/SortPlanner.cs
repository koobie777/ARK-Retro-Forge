using ARK.Core.Execution;
using ARK.Core.Instances;

namespace ARK.Core.Policy;

/// <summary>One unit moved into a subfolder, and where it came from.</summary>
/// <param name="OriginalPath">Where it was.</param>
/// <param name="DestinationPath">Where it went.</param>
/// <param name="Subfolder">The subfolder it was sorted into.</param>
/// <param name="Reason">Why.</param>
public sealed record SortedUnit(string OriginalPath, string DestinationPath, string Subfolder, string Reason);

/// <summary>The plan to sort variants into subfolders, and what it refused.</summary>
/// <param name="Plan">Actions for the executor.</param>
/// <param name="Moves">What would move, and where.</param>
/// <param name="Refused">Units deliberately left in place, each with a reason.</param>
public sealed record SortPlan(
    Plan Plan,
    IReadOnlyList<SortedUnit> Moves,
    IReadOnlyList<(string Path, string Reason)> Refused);

/// <summary>
/// Moves non-kept variants into a subfolder instead of removing them.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is organization, not deletion, and must never be presented as one.</b> Files move but
/// nothing leaves the collection: every release is still there, still readable, still on the same
/// volume. The risk profile is entirely different from quarantine, and conflating the two in the
/// report would teach users to fear a safe operation or to shrug at a dangerous one.
/// </para>
/// <para>
/// Still journaled and still reversible — a move the user did not want is a move they can undo.
/// </para>
/// </remarks>
public static class SortPlanner
{
    /// <summary>Subfolder non-kept variants are sorted into.</summary>
    public const string DefaultSubfolder = "variants";

    /// <summary>Builds the plan.</summary>
    /// <param name="report">The curation analysis to act on.</param>
    /// <param name="sessionId">Session id for the journal.</param>
    /// <param name="createdUtc">Plan timestamp.</param>
    /// <param name="subfolder">Subfolder name; defaults to <see cref="DefaultSubfolder"/>.</param>
    /// <param name="activeDownloadDirectories">
    /// Directories showing in-flight transfer signals. Units inside them are refused, exactly as
    /// for quarantine: moving a file an active client owns breaks the transfer whether or not the
    /// move is a deletion.
    /// </param>
    public static SortPlan Build(
        CurationReport report,
        string sessionId,
        DateTimeOffset createdUtc,
        string? subfolder = null,
        IEnumerable<string>? activeDownloadDirectories = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var name = string.IsNullOrWhiteSpace(subfolder) ? DefaultSubfolder : subfolder;
        var paths = new SortPaths(report.Root, name);

        var active = (activeDownloadDirectories ?? [])
            .Select(directory => directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToArray();

        var actions = new List<PlannedAction>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var moves = new List<SortedUnit>();
        var refused = new List<(string, string)>();

        foreach (var group in report.Resolved)
        {
            foreach (var member in group.Remove)
            {
                if (!paths.IsSameVolume(member.Path))
                {
                    refused.Add((member.Path, "on a different volume — a cross-volume move is a copy, not a move"));
                    continue;
                }

                if (active.Any(directory => IsInside(member.Path, directory)))
                {
                    refused.Add((member.Path, "inside a directory showing active-download signals"));
                    continue;
                }

                var destination = paths.DestinationFor(member.Path);

                // Each level journaled separately so undo can take the whole tree back out.
                foreach (var level in paths.DirectoriesToCreate(destination))
                {
                    if (directories.Add(level))
                    {
                        actions.Add(new PlannedAction(
                            ActionKind.CreateDirectory, level, null, $"Variant subfolder for session {sessionId}"));
                    }
                }

                actions.Add(new PlannedAction(
                    ActionKind.Move, member.Path, destination, $"Sorted as a variant: {group.Reason}"));

                moves.Add(new SortedUnit(member.Path, destination, name, group.Reason));
            }
        }

        return new SortPlan(new Plan(sessionId, createdUtc, "curate-sort", actions), moves, refused);
    }

    private static bool IsInside(string path, string directory) =>
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
