using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ARK.Core.Execution;
using ARK.Core.Instances;
using ARK.Core.Serialization;

namespace ARK.Core.Dedup;

/// <summary>One quarantined unit, as recorded in the manifest.</summary>
/// <param name="OriginalPath">Where it came from.</param>
/// <param name="QuarantinePath">Where it went.</param>
/// <param name="RomCrc32">CRC32 of the ROM inside.</param>
/// <param name="RomSha1">SHA1 that confirmed the match.</param>
/// <param name="RomSize">Size of the ROM inside.</param>
/// <param name="KeptPath">The copy that was kept instead.</param>
/// <param name="KeptReason">Why that copy was the one to keep.</param>
public sealed record QuarantinedUnit(
    string OriginalPath,
    string QuarantinePath,
    string RomCrc32,
    string? RomSha1,
    long RomSize,
    string KeptPath,
    string KeptReason);

/// <summary>
/// The record left in <c>quarantine/&lt;session-id&gt;/</c>.
/// </summary>
/// <remarks>
/// The journal is what reverses the operation; this is what explains it. It must stand on its own
/// — someone finding this directory a year later should understand what happened without needing
/// ARK's journal or ARK itself.
/// </remarks>
/// <param name="SessionId">Session that produced it.</param>
/// <param name="CreatedUtc">When.</param>
/// <param name="Policy">The policy that chose what to keep.</param>
/// <param name="Root">The root that was deduplicated.</param>
/// <param name="Units">Every unit moved here.</param>
public sealed record QuarantineManifest(
    string SessionId,
    DateTimeOffset CreatedUtc,
    KeepPolicy Policy,
    string Root,
    IReadOnlyList<QuarantinedUnit> Units)
{
    /// <summary>Total bytes held in quarantine.</summary>
    public long TotalBytes => Units.Sum(unit => unit.RomSize);
}

/// <summary>Why a unit could not be quarantined.</summary>
/// <param name="Candidate">The unit.</param>
/// <param name="Reason">What stopped it.</param>
public sealed record QuarantineRefusal(DedupCandidate Candidate, string Reason);

/// <summary>The plan to quarantine a dedup report's redundant copies, and what it refused.</summary>
/// <param name="Plan">Actions for the executor. Empty when nothing is quarantinable.</param>
/// <param name="Manifest">The record that will be written alongside them.</param>
/// <param name="Refused">Units deliberately left in place, each with a reason.</param>
public sealed record QuarantinePlan(
    Plan Plan,
    QuarantineManifest Manifest,
    IReadOnlyList<QuarantineRefusal> Refused);

/// <summary>
/// Turns a dedup report into a quarantine plan.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never delete — quarantine.</b> Redundant copies move to
/// <c>&lt;root&gt;/.ark-quarantine/&lt;session-id&gt;/</c>, keeping their path relative to the
/// root so two files sharing a name cannot collide and so the original layout is legible.
/// </para>
/// <para>
/// <b>Same volume by default.</b> A move across volumes is a copy: instant becomes hours, and
/// hundreds of gigabytes get duplicated before anything is freed. A unit on a different volume
/// from the quarantine root is refused rather than silently copied.
/// </para>
/// <para>
/// This builds a plan and nothing else. Only the <see cref="Executor"/> touches the filesystem.
/// </para>
/// </remarks>
public static class QuarantinePlanner
{
    /// <summary>Builds the plan.</summary>
    /// <param name="report">The analysis to act on.</param>
    /// <param name="sessionId">Session id for the quarantine directory and the journal.</param>
    /// <param name="createdUtc">Plan timestamp.</param>
    /// <param name="activeDownloadDirectories">
    /// Directories showing in-flight transfer signals. Units inside them are refused for writing —
    /// renaming or moving a file an active client owns breaks the transfer, and on a completed
    /// torrent still seeding it silently breaks the seed.
    /// </param>
    [RequiresUnreferencedCode("Serializes QuarantineManifest with reflection-based System.Text.Json.")]
    public static QuarantinePlan Build(
        DedupReport report,
        string sessionId,
        DateTimeOffset createdUtc,
        IEnumerable<string>? activeDownloadDirectories = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var quarantine = new QuarantinePaths(report.Root, sessionId);

        var active = (activeDownloadDirectories ?? [])
            .Select(directory => directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToArray();

        var actions = new List<PlannedAction>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestUnits = new List<QuarantinedUnit>();
        var refused = new List<QuarantineRefusal>();

        foreach (var group in report.Resolved)
        {
            foreach (var member in group.Removable)
            {
                if (!quarantine.IsSameVolume(member.Path))
                {
                    refused.Add(new QuarantineRefusal(member,
                        "on a different volume from the quarantine root — a cross-volume move is a copy, not a move"));
                    continue;
                }

                if (active.Any(directory => IsInside(member.Path, directory)))
                {
                    refused.Add(new QuarantineRefusal(member,
                        "inside a directory showing active-download signals — moving it would break the transfer"));
                    continue;
                }

                var destination = quarantine.DestinationFor(member.Path);

                // Every level gets its own action, parent first; undo replays in reverse so they
                // come off leaf-first.
                foreach (var level in quarantine.DirectoriesToCreate(destination))
                {
                    if (directories.Add(level))
                    {
                        actions.Add(new PlannedAction(
                            ActionKind.CreateDirectory, level, null, $"Quarantine directory for session {sessionId}"));
                    }
                }

                actions.Add(new PlannedAction(
                    ActionKind.Quarantine,
                    member.Path,
                    destination,
                    $"Duplicate of {group.Keep!.Name} (CRC32 {group.Crc32}); {group.Reason}"));

                manifestUnits.Add(new QuarantinedUnit(
                    member.Path,
                    destination,
                    group.Crc32,
                    group.Sha1,
                    group.RomSize,
                    group.Keep!.Path,
                    group.Reason));
            }
        }

        var manifest = new QuarantineManifest(sessionId, createdUtc, report.Policy, report.Root, manifestUnits);

        if (actions.Count > 0)
        {
            // Written last so it describes what actually moved, and removed first on undo.
            actions.Add(new PlannedAction(
                ActionKind.WriteText,
                quarantine.ManifestPath,
                null,
                "Quarantine manifest",
                JsonSerializer.Serialize(manifest, ArkJson.Write)));
        }

        return new QuarantinePlan(
            new Plan(sessionId, createdUtc, "dedup-quarantine", actions),
            manifest,
            refused);
    }

    private static bool IsInside(string path, string directory) =>
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
