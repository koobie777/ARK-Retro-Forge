namespace ARK.Core.Psx;

/// <summary>
/// Result of a rename operation.
/// </summary>
public class RenameResult
{
    public bool Success { get; set; }
    public List<string> Messages { get; set; } = new();
    public int RenamedFiles { get; set; }
    public int MovedFiles { get; set; }
    public int DeletedFolders { get; set; }
    public int SkippedOperations { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Executes rename operations for PSX files.
/// </summary>
public class PsxRenameExecutor
{
    /// <summary>
    /// Executes the planned operations.
    /// </summary>
    /// <param name="operations">The planned operations to execute.</param>
    /// <param name="dryRun">If true, no actual file operations are performed.</param>
    public RenameResult Execute(List<FileOperation> operations, bool dryRun)
    {
        var result = new RenameResult { Success = true };

        if (dryRun)
        {
            result.Messages.Add("DRY RUN - No files will be modified");
            result.RenamedFiles = operations.Count(o => o.Type == FileOperationType.Rename);
            result.MovedFiles = operations.Count(o => o.Type == FileOperationType.Move);
            result.DeletedFolders = operations.Count(o => o.Type == FileOperationType.DeleteFolder);
            return result;
        }

        // Execute non-delete operations first
        var nonDeleteOps = operations.Where(o => o.Type != FileOperationType.DeleteFolder).ToList();
        foreach (var operation in nonDeleteOps)
        {
            try
            {
                ExecuteOperation(operation, result);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"Failed to {operation.Type} {operation.SourcePath}: {ex.Message}");
                result.SkippedOperations++;
            }
        }

        // Execute folder deletions last (only if folder is now empty)
        var deleteOps = operations.Where(o => o.Type == FileOperationType.DeleteFolder).ToList();
        foreach (var operation in deleteOps)
        {
            try
            {
                if (Directory.Exists(operation.SourcePath))
                {
                    var remainingFiles = Directory.GetFileSystemEntries(operation.SourcePath);
                    if (remainingFiles.Length == 0)
                    {
                        Directory.Delete(operation.SourcePath, false);
                        result.DeletedFolders++;
                        result.Messages.Add($"Deleted empty folder: {operation.SourcePath}");
                    }
                    else
                    {
                        result.Messages.Add($"Skipped folder deletion (not empty): {operation.SourcePath}");
                        result.SkippedOperations++;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to delete folder {operation.SourcePath}: {ex.Message}");
                result.SkippedOperations++;
            }
        }

        return result;
    }

    private void ExecuteOperation(FileOperation operation, RenameResult result)
    {
        if (string.IsNullOrEmpty(operation.TargetPath))
        {
            throw new InvalidOperationException("Target path is required for rename/move operations");
        }

        if (!File.Exists(operation.SourcePath))
        {
            result.SkippedOperations++;
            result.Messages.Add($"Skipped (source not found): {operation.SourcePath}");
            return;
        }

        if (File.Exists(operation.TargetPath))
        {
            result.SkippedOperations++;
            result.Messages.Add($"Skipped (target exists): {operation.TargetPath}");
            return;
        }

        // Ensure target directory exists
        var targetDir = Path.GetDirectoryName(operation.TargetPath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // Perform the operation
        if (operation.Type == FileOperationType.Move)
        {
            File.Move(operation.SourcePath, operation.TargetPath);
            result.MovedFiles++;
            result.Messages.Add($"Moved: {operation.SourcePath} -> {operation.TargetPath}");
        }
        else if (operation.Type == FileOperationType.Rename)
        {
            File.Move(operation.SourcePath, operation.TargetPath);
            result.RenamedFiles++;
            result.Messages.Add($"Renamed: {operation.SourcePath} -> {operation.TargetPath}");
        }
    }
}
