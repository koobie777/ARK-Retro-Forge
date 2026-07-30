namespace ARK.Core.Tools;

/// <summary>
/// Definition of an external command-line tool ARK can invoke.
/// <para>
/// <see cref="ExecutableName"/> is the platform-neutral base name (no extension); the locator
/// adds the platform-appropriate suffix. There is deliberately no required/optional flag: whether
/// a tool's absence is fatal is the calling operation's judgement, not a global property of the
/// tool. See the Phase 1 brief, defect 2.
/// </para>
/// </summary>
public record ExternalTool
{
    /// <summary>Logical tool name (e.g. "chdman").</summary>
    public required string Name { get; init; }

    /// <summary>Platform-neutral executable base name, without extension (e.g. "chdman").</summary>
    public required string ExecutableName { get; init; }

    /// <summary>Minimum acceptable version, if the tool declares one.</summary>
    public string? MinimumVersion { get; init; }

    /// <summary>Human-readable description of what the tool is for.</summary>
    public string? Description { get; init; }

    /// <summary>Candidate arguments used to query the tool's version, tried in order.</summary>
    public string[]? VersionArguments { get; init; }

    /// <summary>Optional regex for extracting the version from the tool's output.</summary>
    public string? VersionPattern { get; init; }
}
