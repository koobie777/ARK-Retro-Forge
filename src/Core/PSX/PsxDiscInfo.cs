namespace ARK.Core.PSX;

/// <summary>
/// Represents information about a PlayStation disc
/// </summary>
public record PsxDiscInfo
{
    /// <summary>
    /// Original file path
    /// </summary>
    public required string FilePath { get; init; }
    
    /// <summary>
    /// Game title
    /// </summary>
    public string? Title { get; init; }
    
    /// <summary>
    /// Region (e.g., USA, Europe, Japan)
    /// </summary>
    public string? Region { get; init; }
    
    /// <summary>
    /// Serial number (e.g., SLUS-00001, SCUS-94163)
    /// </summary>
    public string? Serial { get; init; }
    
    /// <summary>
    /// Disc number (1-based) for multi-disc titles
    /// </summary>
    public int? DiscNumber { get; init; }
    
    /// <summary>
    /// Total number of discs in the set
    /// </summary>
    public int? TotalDiscs { get; init; }
    
    /// <summary>
    /// File extension (.bin, .cue, .chd)
    /// </summary>
    public string? Extension { get; init; }
    
    /// <summary>
    /// Whether this disc is a cheat/utility disc
    /// </summary>
    public bool IsCheatDisc { get; init; }
    
    /// <summary>
    /// Whether this disc is an educational/Lightspan disc
    /// </summary>
    public bool IsEducationalDisc { get; init; }
    
    /// <summary>
    /// Warning messages about this disc
    /// </summary>
    public List<string> Warnings { get; init; } = new();
}
