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

    private readonly string _baseDirectory;

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
        _baseDirectory = rootOverride ?? AppContext.BaseDirectory;
        Root = Path.Combine(_baseDirectory, "instances", InstanceName);
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

    /// <summary>
    /// Shared external-tools directory (<c>&lt;base&gt;/tools</c>). Shared across instances, not
    /// per-instance — it is independent of <see cref="InstanceName"/>.
    /// </summary>
    public string ToolsRoot => Path.Combine(_baseDirectory, "tools");

    /// <summary>
    /// Path to the shared DAT source manifest (<c>&lt;base&gt;/config/dat/dat-sources.json</c>).
    /// Shared across instances.
    /// </summary>
    public string DatSourcesManifestPath => Path.Combine(_baseDirectory, "config", "dat", "dat-sources.json");

    /// <summary>
    /// Shared system-definitions directory (<c>&lt;base&gt;/config/systems</c>). Shared across instances.
    /// </summary>
    public string SystemsDirectory => Path.Combine(_baseDirectory, "config", "systems");

    /// <summary>This instance's SQLite DAT catalog database (<c>&lt;root&gt;/db/catalog.db</c>).</summary>
    public string CatalogDatabase => Path.Combine(Db, "catalog.db");

    /// <summary>This instance's settings file (<c>&lt;root&gt;/settings.json</c>).</summary>
    public string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>Absolute path of the journal file for a given session.</summary>
    public string JournalFileFor(string sessionId) => Path.Combine(Journal, $"{sessionId}.json");

    /// <summary>This instance's DAT directory for a specific system.</summary>
    public string DatDirectoryFor(string system) => Path.Combine(Dat, system);

    /// <summary>
    /// Builds the plan that creates this instance's directory tree. Handing it to the
    /// <see cref="Executor"/> with apply is how the tree comes into being — the path type
    /// never mutates the filesystem itself.
    /// </summary>
    /// <remarks>
    /// Bootstrap cycle: this plan creates <c>journal/</c>, yet the executor journals each
    /// action into <c>journal/</c> as it completes. The executor breaks the cycle by ensuring
    /// <c>journal/</c> exists ahead of every journal write, so the directory is created before
    /// the plan's own action for it runs (idempotently). See <c>Executor.WriteJournal</c>.
    /// </remarks>
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
