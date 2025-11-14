namespace ARK.Core.PSX;

/// <summary>
/// Represents a PSX rename operation
/// </summary>
public record PsxRenameOperation
{
    /// <summary>
    /// Source file path
    /// </summary>
    public required string SourcePath { get; init; }
    
    /// <summary>
    /// Destination file path
    /// </summary>
    public required string DestinationPath { get; init; }
    
    /// <summary>
    /// Destination filename only
    /// </summary>
    public required string DestinationFileName { get; init; }
    
    /// <summary>
    /// Whether the file is already correctly named
    /// </summary>
    public bool IsAlreadyNamed { get; init; }
    
    /// <summary>
    /// Warning message if any
    /// </summary>
    public string? Warning { get; init; }
    
    /// <summary>
    /// Serial number for this disc
    /// </summary>
    public string? Serial { get; init; }
    
    /// <summary>
    /// Whether this is a cheat disc
    /// </summary>
    public bool IsCheatDisc { get; init; }
    
    /// <summary>
    /// Whether this is an educational disc
    /// </summary>
    public bool IsEducationalDisc { get; init; }
}
