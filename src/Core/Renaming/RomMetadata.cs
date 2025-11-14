namespace ARK.Core.Renaming;

/// <summary>
/// Represents metadata for ROM renaming
/// </summary>
public record RomMetadata
{
    public required string OriginalPath { get; init; }
    public string? Title { get; init; }
    public string? Region { get; init; }
    public string? Id { get; init; }
    public string? Extension { get; init; }
}
