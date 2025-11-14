namespace ARK.Core.Tools;

/// <summary>
/// Represents an external tool that can be used by the application
/// </summary>
public record ExternalTool
{
    public required string Name { get; init; }
    public required string ExecutableName { get; init; }
    public string? MinimumVersion { get; init; }
    public string? Description { get; init; }
    public bool IsOptional { get; init; }
}
