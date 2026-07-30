namespace ARK.Core.Tools;

/// <summary>
/// Result of checking for an external tool
/// </summary>
public record ToolCheckResult
{
    public required string Name { get; init; }
    public bool IsFound { get; init; }
    public string? Path { get; init; }
    public string? Version { get; init; }
    public string? MinimumVersion { get; init; }
    public bool MeetsMinimumVersion { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsOptional { get; init; }
}
