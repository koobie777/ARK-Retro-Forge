namespace ARK.Core.Tools;

/// <summary>
/// Status of a single external tool: whether it was found, where, which version, and whether it
/// meets the declared minimum. Carries no required/optional verdict — whether an absence matters
/// is decided per-operation, not here.
/// </summary>
public record ToolCheckResult
{
    /// <summary>Logical tool name.</summary>
    public required string Name { get; init; }

    /// <summary>True if the executable was located in <c>tools/</c> or on <c>PATH</c>.</summary>
    public bool IsFound { get; init; }

    /// <summary>Full path to the located executable, or null.</summary>
    public string? Path { get; init; }

    /// <summary>Detected version string, or null if it could not be determined.</summary>
    public string? Version { get; init; }

    /// <summary>Minimum acceptable version the tool declares, or null.</summary>
    public string? MinimumVersion { get; init; }

    /// <summary>True if the detected version satisfies <see cref="MinimumVersion"/>.</summary>
    public bool MeetsMinimumVersion { get; init; }

    /// <summary>Explanation when the tool is missing or could not be inspected.</summary>
    public string? ErrorMessage { get; init; }
}
