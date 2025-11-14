namespace ARK.Core.PSX;

/// <summary>
/// Represents a PSX CHD conversion operation
/// </summary>
public record PsxConvertOperation
{
    /// <summary>
    /// Source CUE file path
    /// </summary>
    public required string SourceCuePath { get; init; }
    
    /// <summary>
    /// Associated BIN file paths
    /// </summary>
    public required List<string> SourceBinPaths { get; init; }
    
    /// <summary>
    /// Destination CHD file path
    /// </summary>
    public required string DestinationChdPath { get; init; }
    
    /// <summary>
    /// Warning message if any
    /// </summary>
    public string? Warning { get; init; }
    
    /// <summary>
    /// Title of the game
    /// </summary>
    public string? Title { get; init; }
    
    /// <summary>
    /// Disc number for multi-disc titles
    /// </summary>
    public int? DiscNumber { get; init; }
    
    /// <summary>
    /// Serial number
    /// </summary>
    public string? Serial { get; init; }
}
