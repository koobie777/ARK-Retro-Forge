namespace ARK.Core.Psx;

/// <summary>
/// Executes PSX rename plans
/// </summary>
public class PsxRenameExecutor
{
    /// <summary>
    /// Result of executing a rename plan
    /// </summary>
    public record ExecutionResult
    {
        public int RenamesSucceeded { get; init; }
        public int RenamesFailed { get; init; }
        public int MovesSucceeded { get; init; }
        public int MovesFailed { get; init; }
        public int FoldersDeleted { get; init; }
        public int Conflicts { get; init; }
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    }
    
    /// <summary>
    /// Execute a rename plan
    /// </summary>
    public ExecutionResult Execute(RenamePlan plan, bool dryRun)
    {
        var errors = new List<string>();
        var renamesSucceeded = 0;
        var renamesFailed = 0;
        var movesSucceeded = 0;
        var movesFailed = 0;
        var foldersDeleted = 0;
        var conflicts = 0;
        
        if (dryRun)
        {
            // In dry-run mode, just validate and return counts
            return new ExecutionResult
            {
                RenamesSucceeded = plan.TotalRenames,
                MovesSucceeded = plan.TotalMoves,
                FoldersDeleted = plan.TotalFolderDeletions
            };
        }
        
        // Execute renames and moves first
        foreach (var titlePlan in plan.TitlePlans)
        {
            // Execute renames
            foreach (var renameOp in titlePlan.RenameOperations)
            {
                try
                {
                    if (File.Exists(renameOp.DestinationPath) && 
                        !string.Equals(renameOp.SourcePath, renameOp.DestinationPath, StringComparison.OrdinalIgnoreCase))
                    {
                        conflicts++;
                        errors.Add($"Conflict: {renameOp.DestinationPath} already exists");
                        continue;
                    }
                    
                    File.Move(renameOp.SourcePath, renameOp.DestinationPath);
                    renamesSucceeded++;
                }
                catch (Exception ex)
                {
                    renamesFailed++;
                    errors.Add($"Failed to rename {renameOp.SourcePath}: {ex.Message}");
                }
            }
            
            // Execute moves
            foreach (var moveOp in titlePlan.MoveOperations)
            {
                try
                {
                    if (File.Exists(moveOp.DestinationPath) && 
                        !string.Equals(moveOp.SourcePath, moveOp.DestinationPath, StringComparison.OrdinalIgnoreCase))
                    {
                        conflicts++;
                        errors.Add($"Conflict: {moveOp.DestinationPath} already exists");
                        continue;
                    }
                    
                    // Ensure destination directory exists
                    var destDir = Path.GetDirectoryName(moveOp.DestinationPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    
                    File.Move(moveOp.SourcePath, moveOp.DestinationPath);
                    movesSucceeded++;
                }
                catch (Exception ex)
                {
                    movesFailed++;
                    errors.Add($"Failed to move {moveOp.SourcePath}: {ex.Message}");
                }
            }
        }
        
        // Delete folders only after all file operations
        foreach (var titlePlan in plan.TitlePlans)
        {
            foreach (var deleteFolderOp in titlePlan.FolderDeletions)
            {
                try
                {
                    // Only delete if folder is now empty
                    if (Directory.Exists(deleteFolderOp.FolderPath) && 
                        !Directory.EnumerateFileSystemEntries(deleteFolderOp.FolderPath).Any())
                    {
                        Directory.Delete(deleteFolderOp.FolderPath, false);
                        foldersDeleted++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to delete folder {deleteFolderOp.FolderPath}: {ex.Message}");
                }
            }
        }
        
        return new ExecutionResult
        {
            RenamesSucceeded = renamesSucceeded,
            RenamesFailed = renamesFailed,
            MovesSucceeded = movesSucceeded,
            MovesFailed = movesFailed,
            FoldersDeleted = foldersDeleted,
            Conflicts = conflicts,
            Errors = errors
        };
    }
}
