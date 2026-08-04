using ARK.Core.Execution;
using ARK.Core.Instances;
using ARK.Core.Scanning;
using ARK.Core.Verification;

namespace ARK.Core.Renaming;

/// <summary>One unit moved into its destination directory.</summary>
/// <param name="OriginalPath">Where it was.</param>
/// <param name="DestinationPath">Where it goes.</param>
/// <param name="Folder">The directory it was filed under.</param>
public sealed record OrganizedUnit(string OriginalPath, string DestinationPath, string Folder);

/// <summary>The plan to organize a set, and what it refused.</summary>
/// <param name="Plan">Actions for the executor.</param>
/// <param name="Moves">What would move, and where.</param>
/// <param name="AlreadyFiled">Units already in the right directory. No filesystem write.</param>
/// <param name="Refused">Units left alone, each with a reason.</param>
public sealed record OrganizePlan(
    Plan Plan,
    IReadOnlyList<OrganizedUnit> Moves,
    IReadOnlyList<string> AlreadyFiled,
    IReadOnlyList<(string Path, string Reason)> Refused);

/// <summary>
/// Files units into a directory structure.
/// </summary>
/// <remarks>
/// <para>
/// <b>A different operation from renaming.</b> Rename changes a filename in place; organize moves
/// units between directories. Both are journaled and reversible, both are reported separately, and
/// neither is a removal — nothing leaves the collection.
/// </para>
/// <para>
/// The default structure is the DAT name, which is what real collections already use.
/// </para>
/// </remarks>
public static class OrganizePlanner
{
    /// <summary>Builds the plan.</summary>
    /// <param name="scan">The scanned set.</param>
    /// <param name="verification">Verification states, used to leave unjudgeable units alone.</param>
    /// <param name="sessionId">Session id for the journal.</param>
    /// <param name="createdUtc">Plan timestamp.</param>
    /// <param name="activeDownloadDirectories">Directories showing in-flight transfer signals.</param>
    public static OrganizePlan Build(
        ScanReport scan,
        VerificationReport verification,
        string sessionId,
        DateTimeOffset createdUtc,
        IEnumerable<string>? activeDownloadDirectories = null)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var paths = new OrganizePaths(scan.Root);
        var scopes = scan.Directories
            .Where(directory => directory.Scope is not null)
            .ToDictionary(directory => directory.Name, directory => directory.Scope!.DatName, StringComparer.OrdinalIgnoreCase);

        var active = (activeDownloadDirectories ?? [])
            .Select(directory => directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToArray();

        var actions = new List<PlannedAction>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var moves = new List<OrganizedUnit>();
        var alreadyFiled = new List<string>();
        var refused = new List<(string, string)>();

        foreach (var scanned in scan.Units)
        {
            var unit = scanned.Unit;
            var file = unit.Files[0];

            if (!scopes.TryGetValue(unit.SetFolder, out var folder))
            {
                // No DAT resolved for this directory, so there is no structure to file it under.
                refused.Add((unit.PrimaryPath, "no DAT resolved for this directory — nothing to file it under"));
                continue;
            }

            if (active.Any(directory => IsInside(unit.PrimaryPath, directory)))
            {
                refused.Add((unit.PrimaryPath, "inside a directory showing active-download signals"));
                continue;
            }

            if (!paths.IsSameVolume(unit.PrimaryPath))
            {
                refused.Add((unit.PrimaryPath, "on a different volume — a cross-volume move is a copy, not a move"));
                continue;
            }

            var destination = paths.DestinationFor(folder, file.Name);
            if (string.Equals(destination, unit.PrimaryPath, StringComparison.Ordinal))
            {
                alreadyFiled.Add(unit.PrimaryPath);
                continue;
            }

            if (File.Exists(destination))
            {
                refused.Add((unit.PrimaryPath, $"'{destination}' already exists — never overwritten"));
                continue;
            }

            // Each level journaled separately so undo can take the whole tree back out.
            foreach (var level in paths.DirectoriesToCreate(destination))
            {
                if (directories.Add(level))
                {
                    actions.Add(new PlannedAction(
                        ActionKind.CreateDirectory, level, null, $"Organize directory for session {sessionId}"));
                }
            }

            actions.Add(new PlannedAction(
                ActionKind.Move, unit.PrimaryPath, destination, $"Organize into '{folder}'"));

            moves.Add(new OrganizedUnit(unit.PrimaryPath, destination, folder));
        }

        return new OrganizePlan(
            new Plan(sessionId, createdUtc, "organize", actions), moves, alreadyFiled, refused);
    }

    private static bool IsInside(string path, string directory) =>
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
