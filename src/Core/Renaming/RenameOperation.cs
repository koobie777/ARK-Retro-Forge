namespace ARK.Core.Renaming;

/// <summary>
/// Represents a rename operation
/// </summary>
public record RenameOperation
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required string DestinationFileName { get; init; }
    public bool IsAlreadyNamed { get; init; }
    public string? Warning { get; init; }
}
