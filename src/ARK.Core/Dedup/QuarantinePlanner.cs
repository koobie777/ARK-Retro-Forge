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
/// <param name="Policy">Label of whatever chose what to keep.</param>
/// <param name="Root">The root that was deduplicated.</param>
/// <param name="Units">Every unit moved here.</param>
public sealed record QuarantineManifest(
    string SessionId,
    DateTimeOffset CreatedUtc,
    string Policy,
    string Root,
    IReadOnlyList<QuarantinedUnit> Units)
{
    /// <summary>Total bytes held in quarantine.</summary>
    public long TotalBytes => Units.Sum(unit => unit.RomSize);
}

/// <summary>
/// One unit put forward for quarantine, with everything the manifest needs to explain it.
/// </summary>
/// <remarks>
/// Deliberately independent of what decided it. Deduplication removes byte-identical copies and
/// curation removes distinct releases the user does not want; both produce the same request, so
/// both get the same manifest, journal, same-volume guarantee and undo — the machinery is written
/// once and cannot drift apart between them.
/// </remarks>
/// <param name="Path">Primary path of the unit to move.</param>
/// <param name="Name">Display name.</param>
/// <param name="RomCrc32">CRC32 of the ROM, where known.</param>
/// <param name="RomSha1">SHA1, where known.</param>
/// <param name="RomSize">ROM size in bytes.</param>
/// <param name="KeptPath">The copy kept instead.</param>
/// <param name="Reason">Why this one goes and that one stays.</param>
public sealed record QuarantineRequest(
    string Path,
    string Name,
    string? RomCrc32,
    string? RomSha1,
    long RomSize,
    string KeptPath,
    string Reason);

/// <summary>Why a unit could not be quarantined.</summary>
/// <param name="Request">The unit.</param>
/// <param name="Reason">What stopped it.</param>
public sealed record QuarantineRefusal(QuarantineRequest Request, string Reason);

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

        var requests = report.Resolved
            .SelectMany(group => group.Removable.Select(member => new QuarantineRequest(
                member.Path,
                member.Name,
                group.Crc32,
                group.Sha1,
                group.RomSize,
                group.Keep!.Path,
                $"Duplicate of {group.Keep!.Name} (CRC32 {group.Crc32}); {group.Reason}")))
            .ToArray();

        return Build(report.Root, requests, sessionId, createdUtc, report.Policy.ToString(), activeDownloadDirectories);
    }

    /// <summary>
    /// Builds the plan from explicit requests, whatever produced them.
    /// </summary>
    /// <param name="root">Root being operated on; quarantine lands beneath it.</param>
    /// <param name="requests">Units to move.</param>
    /// <param name="sessionId">Session id for the quarantine directory and the journal.</param>
    /// <param name="createdUtc">Plan timestamp.</param>
    /// <param name="policy">Label of whatever decided these, recorded in the manifest.</param>
    /// <param name="activeDownloadDirectories">
    /// Directories showing in-flight transfer signals. Units inside them are refused for writing —
    /// renaming or moving a file an active client owns breaks the transfer, and on a completed
    /// torrent still seeding it silently breaks the seed.
    /// </param>
    [RequiresUnreferencedCode("Serializes QuarantineManifest with reflection-based System.Text.Json.")]
    public static QuarantinePlan Build(
        string root,
        IReadOnlyList<QuarantineRequest> requests,
        string sessionId,
        DateTimeOffset createdUtc,
        string policy,
        IEnumerable<string>? activeDownloadDirectories = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var quarantine = new QuarantinePaths(root, sessionId);

        var active = (activeDownloadDirectories ?? [])
            .Select(directory => directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToArray();

        var actions = new List<PlannedAction>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifestUnits = new List<QuarantinedUnit>();
        var refused = new List<QuarantineRefusal>();

        foreach (var request in requests)
        {
            if (!quarantine.IsSameVolume(request.Path))
            {
                refused.Add(new QuarantineRefusal(request,
                    "on a different volume from the quarantine root — a cross-volume move is a copy, not a move"));
                continue;
            }

            if (active.Any(directory => IsInside(request.Path, directory)))
            {
                refused.Add(new QuarantineRefusal(request,
                    "inside a directory showing active-download signals — moving it would break the transfer"));
                continue;
            }

            var destination = quarantine.DestinationFor(request.Path);

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

            actions.Add(new PlannedAction(ActionKind.Quarantine, request.Path, destination, request.Reason));

            manifestUnits.Add(new QuarantinedUnit(
                request.Path,
                destination,
                request.RomCrc32 ?? string.Empty,
                request.RomSha1,
                request.RomSize,
                request.KeptPath,
                request.Reason));
        }

        var manifest = new QuarantineManifest(sessionId, createdUtc, policy, root, manifestUnits);

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
