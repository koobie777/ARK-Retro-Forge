namespace ARK.Core.Psx;

/// <summary>
/// Represents a single PSX disc with its metadata
/// </summary>
public record PsxDisc
{
    /// <summary>
    /// Disc number (1-based for multi-disc titles, 1 for single-disc)
    /// </summary>
    public required int DiscNumber { get; init; }
    
    /// <summary>
    /// Serial number (e.g., SLUS-01234, SCUS-94163)
    /// </summary>
    public string? Serial { get; init; }
    
    /// <summary>
    /// Source file path (CUE, CHD, or PBP file)
    /// </summary>
    public required string SourcePath { get; init; }
    
    /// <summary>
    /// File format (BinCue, Chd, Pbp)
    /// </summary>
    public required PsxDiscFormat Format { get; init; }
    
    /// <summary>
    /// Associated BIN files for BIN/CUE format (empty for CHD/PBP)
    /// </summary>
    public IReadOnlyList<string> BinFiles { get; init; } = Array.Empty<string>();
}

/// <summary>
/// PSX disc file formats
/// </summary>
public enum PsxDiscFormat
{
    BinCue,
    Chd,
    Pbp
}
