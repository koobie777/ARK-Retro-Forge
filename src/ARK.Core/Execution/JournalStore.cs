using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ARK.Core.Instances;
using ARK.Core.Serialization;

namespace ARK.Core.Execution;

/// <summary>Why a journal could not be used.</summary>
public enum JournalFault
{
    /// <summary>No fault.</summary>
    None = 0,

    /// <summary>No journal with that session id.</summary>
    NotFound,

    /// <summary>The file could not be read or parsed.</summary>
    Unreadable,

    /// <summary>Written by a newer build than this one.</summary>
    SchemaTooNew,

    /// <summary>Contains an action kind this build does not recognize.</summary>
    UnknownActionKind,
}

/// <summary>A journal on disk, with a summary that does not require loading its actions.</summary>
/// <param name="SessionId">Session identifier.</param>
/// <param name="CreatedUtc">When the originating plan was built.</param>
/// <param name="Operation">Operation that produced it.</param>
/// <param name="ActionCount">Actions recorded.</param>
/// <param name="ReversesSessionId">The session this one reversed, when it is an undo.</param>
/// <param name="ReversedBySessionId">The undo session that reversed this one, when one exists.</param>
/// <param name="Fault">Why it is unusable, if it is.</param>
public sealed record JournalSummary(
    string SessionId,
    DateTimeOffset CreatedUtc,
    string Operation,
    int ActionCount,
    string? ReversesSessionId,
    string? ReversedBySessionId,
    JournalFault Fault = JournalFault.None)
{
    /// <summary>True when this session has already been reversed.</summary>
    public bool IsReversed => ReversedBySessionId is { Length: > 0 };

    /// <summary>True when this journal records an undo of another session.</summary>
    public bool IsUndo => ReversesSessionId is { Length: > 0 };
}

/// <summary>Reading a journal either yields a document or states why it cannot be trusted.</summary>
/// <param name="Document">The journal, when it loaded cleanly.</param>
/// <param name="Fault">Why not, otherwise.</param>
/// <param name="Detail">Human-readable explanation.</param>
public sealed record JournalLoad(JournalDocument? Document, JournalFault Fault, string? Detail)
{
    /// <summary>True when a usable journal was loaded.</summary>
    [MemberNotNullWhen(true, nameof(Document))]
    public bool Succeeded => Fault == JournalFault.None && Document is not null;
}

/// <summary>
/// Reads the instance's journal directory. Read-only: it never writes a journal, which remains
/// the <see cref="Executor"/>'s job alone.
/// </summary>
public sealed class JournalStore
{
    private readonly InstancePaths _paths;

    /// <summary>Creates a store over an instance's journal directory.</summary>
    public JournalStore(InstancePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    /// <summary>
    /// Every session, newest first, each knowing whether it has since been reversed. A journal
    /// that cannot be read is listed with its fault rather than omitted — a session you cannot
    /// undo is exactly the thing you need to be told about.
    /// </summary>
    public IReadOnlyList<JournalSummary> List()
    {
        if (!Directory.Exists(_paths.Journal))
        {
            return [];
        }

        var loaded = Directory
            .EnumerateFiles(_paths.Journal, "*.json", SearchOption.TopDirectoryOnly)
            .Select(file => (Path: file, Load: Read(Path.GetFileNameWithoutExtension(file))))
            .ToList();

        var reversedBy = loaded
            .Where(entry => entry.Load.Succeeded && entry.Load.Document!.IsUndo)
            .GroupBy(entry => entry.Load.Document!.ReversesSessionId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Load.Document!.SessionId, StringComparer.OrdinalIgnoreCase);

        return loaded
            .Select(entry =>
            {
                var id = Path.GetFileNameWithoutExtension(entry.Path);
                var document = entry.Load.Document;
                reversedBy.TryGetValue(id, out var reverser);

                return new JournalSummary(
                    id,
                    document?.CreatedUtc ?? default,
                    document?.Operation ?? "(unreadable)",
                    document?.CompletedActions.Count ?? 0,
                    document?.ReversesSessionId,
                    reverser,
                    entry.Load.Fault);
            })
            .OrderByDescending(summary => summary.CreatedUtc)
            .ThenBy(summary => summary.SessionId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Loads one session, refusing anything it cannot faithfully replay.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Any failure to read a journal is reported as a fault, never thrown at the user as a crash.")]
    public JournalLoad Read(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new JournalLoad(null, JournalFault.NotFound, "No session id given");
        }

        var path = _paths.JournalFileFor(sessionId);
        if (!File.Exists(path))
        {
            return new JournalLoad(null, JournalFault.NotFound, $"No journal for session '{sessionId}'");
        }

        JournalDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<JournalDocument>(File.ReadAllText(path), ArkJson.Read);
        }
        catch (Exception ex)
        {
            return new JournalLoad(null, JournalFault.Unreadable, ex.Message);
        }

        if (document is null)
        {
            return new JournalLoad(null, JournalFault.Unreadable, "Journal was empty");
        }

        // Refused, not best-guessed. Upgrading, undoing, then downgrading must not silently
        // replay a session this build only partly understands.
        if (document.SchemaVersion > JournalDocument.CurrentSchemaVersion)
        {
            return new JournalLoad(
                null,
                JournalFault.SchemaTooNew,
                $"Journal schema v{document.SchemaVersion} is newer than this build understands (v{JournalDocument.CurrentSchemaVersion})");
        }

        // An unrecognized kind decoded to Unknown. Replaying it would mean guessing what another
        // build meant by it, which is exactly how a safety net becomes a hazard.
        var unknown = document.CompletedActions.Count(action => action.Kind == ActionKind.Unknown);
        if (unknown > 0)
        {
            return new JournalLoad(
                null,
                JournalFault.UnknownActionKind,
                $"{unknown} action(s) use a kind this build does not recognize");
        }

        return new JournalLoad(document, JournalFault.None, null);
    }
}
