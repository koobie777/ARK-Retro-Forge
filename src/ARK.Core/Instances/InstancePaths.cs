using System.Text;
using ARK.Core.Execution;

namespace ARK.Core.Instances;

/// <summary>
/// Resolves every path a single ARK instance uses, and is the sole authority for instance
/// layout: no other type composes instance paths itself. Resolution is pure — creating the
/// directory tree is a filesystem mutation, so provisioning is expressed as a
/// <see cref="Plan"/> (see <see cref="BuildProvisionPlan"/>) and carried out by the
/// <see cref="Executor"/>, the only type permitted to touch the filesystem.
/// </summary>
public sealed class InstancePaths
{
    /// <summary>Instance name used when none is supplied.</summary>
    public const string DefaultInstanceName = "default";

    private const string ProvisionOperation = "instance-provision";

    /// <summary>Creates a resolver for a named instance.</summary>
    /// <param name="instanceName">Instance name; sanitized, falling back to <see cref="DefaultInstanceName"/>.</param>
    /// <param name="rootOverride">
    /// Base directory that holds the <c>instances/</c> folder. Defaults to the executable's
    /// directory, so ARK is portable and behaves identically regardless of the working
    /// directory it is invoked from. Tests supply a temp path here.
    /// </param>
    public InstancePaths(string? instanceName = null, string? rootOverride = null)
    {
        InstanceName = Sanitize(instanceName);
        var baseDirectory = rootOverride ?? AppContext.BaseDirectory;
        Root = Path.Combine(baseDirectory, "instances", InstanceName);
        Db = Path.Combine(Root, "db");
        Dat = Path.Combine(Root, "dat");
        Logs = Path.Combine(Root, "logs");
        Journal = Path.Combine(Root, "journal");
        Quarantine = Path.Combine(Root, "quarantine");
    }

    /// <summary>Sanitized name of this instance.</summary>
    public string InstanceName { get; }

    /// <summary>Root directory for this instance: <c>&lt;base&gt;/instances/&lt;name&gt;</c>.</summary>
    public string Root { get; }

    /// <summary>Database directory.</summary>
    public string Db { get; }

    /// <summary>DAT storage directory.</summary>
    public string Dat { get; }

    /// <summary>Log directory.</summary>
    public string Logs { get; }

    /// <summary>Journal directory holding one file per session.</summary>
    public string Journal { get; }

    /// <summary>Quarantine directory for removal candidates.</summary>
    public string Quarantine { get; }

    /// <summary>Every instance subdirectory, in provisioning order.</summary>
    public IReadOnlyList<string> Directories => new[] { Db, Dat, Logs, Journal, Quarantine };

    /// <summary>Rolling-log file path template consumed by the logging configuration.</summary>
    public string LogFileTemplate => Path.Combine(Logs, "ark-.log");

    /// <summary>Absolute path of the journal file for a given session.</summary>
    public string JournalFileFor(string sessionId) => Path.Combine(Journal, $"{sessionId}.json");

    /// <summary>
    /// Builds the plan that creates this instance's directory tree. Handing it to the
    /// <see cref="Executor"/> with apply is how the tree comes into being — the path type
    /// never mutates the filesystem itself.
    /// </summary>
    public Plan BuildProvisionPlan() => new(
        SessionId: $"provision-{InstanceName}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
        CreatedUtc: DateTimeOffset.UtcNow,
        Operation: ProvisionOperation,
        Actions: Directories
            .Select(directory => new PlannedAction(
                ActionKind.CreateDirectory,
                directory,
                null,
                $"Provision instance directory '{Path.GetFileName(directory)}'"))
            .ToArray());

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultInstanceName;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var sanitized = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? DefaultInstanceName : sanitized;
    }
}
