namespace ARK.Core.Psx;

/// <summary>
/// Conversion direction
/// </summary>
public enum ConversionDirection
{
    BinCueToChd,
    ChdToBinCue
}

/// <summary>
/// Represents an immutable plan for PSX conversion operations
/// </summary>
public record ConvertPlan
{
    /// <summary>
    /// Conversion direction
    /// </summary>
    public required ConversionDirection Direction { get; init; }
    
    /// <summary>
    /// Target format (e.g., "chd")
    /// </summary>
    public required string TargetFormat { get; init; }
    
    /// <summary>
    /// Whether to delete source files after successful conversion
    /// </summary>
    public required bool DeleteSource { get; init; }
    
    /// <summary>
    /// All title groups included in this plan
    /// </summary>
    public required IReadOnlyList<ConvertTitlePlan> TitlePlans { get; init; }
    
    /// <summary>
    /// Total number of conversions
    /// </summary>
    public int TotalConversions => TitlePlans.Sum(t => t.ConversionOperations.Count);
    
    /// <summary>
    /// Total number of playlist writes
    /// </summary>
    public int TotalPlaylistWrites => TitlePlans.Count(t => t.PlaylistOperation != null);
    
    /// <summary>
    /// Total number of skipped titles
    /// </summary>
    public int TotalSkipped => TitlePlans.Count(t => t.SkipReason != null);
}

/// <summary>
/// Conversion plan for a single title group
/// </summary>
public record ConvertTitlePlan
{
    /// <summary>
    /// The title group this plan applies to
    /// </summary>
    public required PsxTitleGroup TitleGroup { get; init; }
    
    /// <summary>
    /// Planned conversion operations
    /// </summary>
    public required IReadOnlyList<ConversionOperation> ConversionOperations { get; init; }
    
    /// <summary>
    /// Planned playlist write operation (for multi-disc titles converting to CHD)
    /// </summary>
    public PlaylistOperation? PlaylistOperation { get; init; }
    
    /// <summary>
    /// Reason for skipping this title (if applicable)
    /// </summary>
    public string? SkipReason { get; init; }
}

/// <summary>
/// Represents a file conversion operation
/// </summary>
public record ConversionOperation
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required bool DeleteSourceAfterSuccess { get; init; }
    
    /// <summary>
    /// Associated files to delete (e.g., BIN files for CUE conversion)
    /// </summary>
    public IReadOnlyList<string> AssociatedFilesToDelete { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Represents a playlist write operation
/// </summary>
public record PlaylistOperation
{
    public required string PlaylistPath { get; init; }
    public required IReadOnlyList<string> DiscFiles { get; init; }
}
