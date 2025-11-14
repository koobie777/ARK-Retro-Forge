namespace ARK.Core.Psx;

/// <summary>
/// Represents an immutable plan for PSX rename operations
/// </summary>
public record RenamePlan
{
    /// <summary>
    /// All title groups included in this plan
    /// </summary>
    public required IReadOnlyList<RenameTitlePlan> TitlePlans { get; init; }
    
    /// <summary>
    /// Total number of rename operations
    /// </summary>
    public int TotalRenames => TitlePlans.Sum(t => t.RenameOperations.Count);
    
    /// <summary>
    /// Total number of move operations
    /// </summary>
    public int TotalMoves => TitlePlans.Sum(t => t.MoveOperations.Count);
    
    /// <summary>
    /// Total number of folder deletions
    /// </summary>
    public int TotalFolderDeletions => TitlePlans.Sum(t => t.FolderDeletions.Count);
}

/// <summary>
/// Rename plan for a single title group
/// </summary>
public record RenameTitlePlan
{
    /// <summary>
    /// The title group this plan applies to
    /// </summary>
    public required PsxTitleGroup TitleGroup { get; init; }
    
    /// <summary>
    /// Planned rename operations (filename change in same directory)
    /// </summary>
    public required IReadOnlyList<RenameFileOperation> RenameOperations { get; init; }
    
    /// <summary>
    /// Planned move operations (file moves to different directory)
    /// </summary>
    public required IReadOnlyList<MoveFileOperation> MoveOperations { get; init; }
    
    /// <summary>
    /// Planned folder deletions (empty per-game folders after flatten)
    /// </summary>
    public required IReadOnlyList<DeleteFolderOperation> FolderDeletions { get; init; }
}

/// <summary>
/// Represents a file rename operation (same directory)
/// </summary>
public record RenameFileOperation
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
}

/// <summary>
/// Represents a file move operation (different directory)
/// </summary>
public record MoveFileOperation
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
}

/// <summary>
/// Represents a folder deletion operation
/// </summary>
public record DeleteFolderOperation
{
    public required string FolderPath { get; init; }
}
