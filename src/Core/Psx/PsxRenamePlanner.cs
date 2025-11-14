namespace ARK.Core.Psx;

/// <summary>
/// Plans PSX file rename and move operations
/// </summary>
public class PsxRenamePlanner
{
    private readonly PsxGrouper _grouper;
    private readonly PsxNamingService _namingService;
    
    public PsxRenamePlanner()
    {
        _grouper = new PsxGrouper();
        _namingService = new PsxNamingService();
    }
    
    /// <summary>
    /// Create a rename plan for PSX files in the specified directory
    /// </summary>
    public RenamePlan CreatePlan(string rootPath, bool recursive, bool flattenMultidisc)
    {
        var titleGroups = _grouper.ScanAndGroup(rootPath, recursive).ToList();
        var titlePlans = new List<RenameTitlePlan>();
        
        foreach (var titleGroup in titleGroups)
        {
            var renameOps = new List<RenameFileOperation>();
            var moveOps = new List<MoveFileOperation>();
            var folderDeletions = new List<DeleteFolderOperation>();
            
            // Determine target directory
            string targetDir;
            if (flattenMultidisc && titleGroup.RootFolder != null)
            {
                // Move to parent directory
                targetDir = Path.GetDirectoryName(titleGroup.RootFolder) ?? titleGroup.RootFolder;
            }
            else
            {
                // Keep in current directory
                targetDir = titleGroup.RootFolder ?? Path.GetDirectoryName(titleGroup.Discs[0].SourcePath) ?? string.Empty;
            }
            
            // Process each disc
            foreach (var disc in titleGroup.Discs)
            {
                var sourceDir = Path.GetDirectoryName(disc.SourcePath) ?? string.Empty;
                var sourceExt = Path.GetExtension(disc.SourcePath);
                
                string newFileName;
                if (titleGroup.IsMultiDisc)
                {
                    newFileName = PsxNamingService.GenerateMultiDiscName(titleGroup, disc, sourceExt);
                }
                else
                {
                    newFileName = PsxNamingService.GenerateSingleDiscName(titleGroup, sourceExt);
                }
                
                var destinationPath = Path.Combine(targetDir, newFileName);
                
                // Skip if source and destination are the same
                if (string.Equals(disc.SourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                // Determine if this is a rename (same dir) or move (different dir)
                if (string.Equals(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase))
                {
                    renameOps.Add(new RenameFileOperation
                    {
                        SourcePath = disc.SourcePath,
                        DestinationPath = destinationPath
                    });
                }
                else
                {
                    moveOps.Add(new MoveFileOperation
                    {
                        SourcePath = disc.SourcePath,
                        DestinationPath = destinationPath
                    });
                }
                
                // Also handle associated BIN files for BIN/CUE format
                if (disc.Format == PsxDiscFormat.BinCue && disc.BinFiles.Any())
                {
                    foreach (var binFile in disc.BinFiles)
                    {
                        var binExt = Path.GetExtension(binFile);
                        var binFileName = Path.GetFileNameWithoutExtension(newFileName);
                        
                        // For multi-BIN tracks, preserve the track suffix
                        var originalBinName = Path.GetFileNameWithoutExtension(binFile);
                        if (originalBinName.Contains("Track", StringComparison.OrdinalIgnoreCase) ||
                            originalBinName.Contains("(", StringComparison.OrdinalIgnoreCase))
                        {
                            // Extract track/part suffix
                            var idx = originalBinName.LastIndexOf('(');
                            if (idx == -1)
                            {
                                idx = originalBinName.LastIndexOfAny(new[] { ' ', '_', '-' });
                            }
                            
                            if (idx > 0 && idx < originalBinName.Length - 1)
                            {
                                var suffix = originalBinName.Substring(idx);
                                binFileName = binFileName + suffix;
                            }
                        }
                        
                        var newBinFileName = binFileName + binExt;
                        var binDestPath = Path.Combine(targetDir, newBinFileName);
                        
                        if (string.Equals(binFile, binDestPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        
                        var binSourceDir = Path.GetDirectoryName(binFile) ?? string.Empty;
                        if (string.Equals(binSourceDir, targetDir, StringComparison.OrdinalIgnoreCase))
                        {
                            renameOps.Add(new RenameFileOperation
                            {
                                SourcePath = binFile,
                                DestinationPath = binDestPath
                            });
                        }
                        else
                        {
                            moveOps.Add(new MoveFileOperation
                            {
                                SourcePath = binFile,
                                DestinationPath = binDestPath
                            });
                        }
                    }
                }
            }
            
            // Plan folder deletion if flattening and folder will be empty
            if (flattenMultidisc && titleGroup.RootFolder != null && moveOps.Count > 0)
            {
                folderDeletions.Add(new DeleteFolderOperation
                {
                    FolderPath = titleGroup.RootFolder
                });
            }
            
            titlePlans.Add(new RenameTitlePlan
            {
                TitleGroup = titleGroup,
                RenameOperations = renameOps,
                MoveOperations = moveOps,
                FolderDeletions = folderDeletions
            });
        }
        
        return new RenamePlan
        {
            TitlePlans = titlePlans
        };
    }
}
