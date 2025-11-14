namespace ARK.Core.Psx;

/// <summary>
/// Plans PSX file conversion operations
/// </summary>
public class PsxConvertPlanner
{
    private readonly PsxGrouper _grouper;
    
    public PsxConvertPlanner()
    {
        _grouper = new PsxGrouper();
    }
    
    /// <summary>
    /// Create a conversion plan for PSX files in the specified directory
    /// </summary>
    public ConvertPlan CreatePlan(
        string rootPath, 
        bool recursive, 
        bool flattenMultidisc,
        string targetFormat,
        ConversionDirection direction,
        bool deleteSource)
    {
        var titleGroups = _grouper.ScanAndGroup(rootPath, recursive).ToList();
        var titlePlans = new List<ConvertTitlePlan>();
        
        foreach (var titleGroup in titleGroups)
        {
            var conversionOps = new List<ConversionOperation>();
            PlaylistOperation? playlistOp = null;
            string? skipReason = null;
            
            if (direction == ConversionDirection.BinCueToChd)
            {
                // Check if title has BIN/CUE files
                var binCueDiscs = titleGroup.Discs.Where(d => d.Format == PsxDiscFormat.BinCue).ToList();
                var chdDiscs = titleGroup.Discs.Where(d => d.Format == PsxDiscFormat.Chd).ToList();
                
                if (binCueDiscs.Count == 0)
                {
                    // No BIN/CUE files to convert
                    if (chdDiscs.Count > 0)
                    {
                        skipReason = "Already in CHD format";
                    }
                    else
                    {
                        skipReason = "No BIN/CUE files found";
                    }
                }
                else if (binCueDiscs.Count > 0 && chdDiscs.Count > 0)
                {
                    // Both formats exist, prefer CHD and skip
                    skipReason = "Both BIN/CUE and CHD exist, preferring CHD";
                }
                else
                {
                    // Plan conversions for each BIN/CUE disc
                    var targetDir = DetermineTargetDirectory(titleGroup, flattenMultidisc);
                    var convertedChdFiles = new List<string>();
                    
                    foreach (var disc in binCueDiscs.OrderBy(d => d.DiscNumber))
                    {
                        var chdFileName = titleGroup.IsMultiDisc
                            ? PsxNamingService.GenerateMultiDiscName(titleGroup, disc, ".chd")
                            : PsxNamingService.GenerateSingleDiscName(titleGroup, ".chd");
                        
                        var chdPath = Path.Combine(targetDir, chdFileName);
                        convertedChdFiles.Add(Path.GetFileName(chdPath));
                        
                        conversionOps.Add(new ConversionOperation
                        {
                            SourcePath = disc.SourcePath,
                            DestinationPath = chdPath,
                            DeleteSourceAfterSuccess = deleteSource,
                            AssociatedFilesToDelete = deleteSource ? disc.BinFiles.ToList() : Array.Empty<string>()
                        });
                    }
                    
                    // Plan .m3u playlist for multi-disc titles
                    if (titleGroup.IsMultiDisc)
                    {
                        var playlistName = PsxNamingService.GeneratePlaylistName(titleGroup);
                        var playlistPath = Path.Combine(targetDir, playlistName);
                        
                        playlistOp = new PlaylistOperation
                        {
                            PlaylistPath = playlistPath,
                            DiscFiles = convertedChdFiles
                        };
                    }
                }
            }
            else // ChdToBinCue
            {
                var chdDiscs = titleGroup.Discs.Where(d => d.Format == PsxDiscFormat.Chd).ToList();
                
                if (chdDiscs.Count == 0)
                {
                    skipReason = "No CHD files found";
                }
                else
                {
                    var targetDir = DetermineTargetDirectory(titleGroup, flattenMultidisc);
                    
                    foreach (var disc in chdDiscs.OrderBy(d => d.DiscNumber))
                    {
                        conversionOps.Add(new ConversionOperation
                        {
                            SourcePath = disc.SourcePath,
                            DestinationPath = targetDir, // For CHD->BIN/CUE, destination is directory
                            DeleteSourceAfterSuccess = deleteSource
                        });
                    }
                }
            }
            
            titlePlans.Add(new ConvertTitlePlan
            {
                TitleGroup = titleGroup,
                ConversionOperations = conversionOps,
                PlaylistOperation = playlistOp,
                SkipReason = skipReason
            });
        }
        
        return new ConvertPlan
        {
            Direction = direction,
            TargetFormat = targetFormat,
            DeleteSource = deleteSource,
            TitlePlans = titlePlans
        };
    }
    
    private static string DetermineTargetDirectory(PsxTitleGroup titleGroup, bool flattenMultidisc)
    {
        if (flattenMultidisc && titleGroup.RootFolder != null)
        {
            // Move to parent directory
            return Path.GetDirectoryName(titleGroup.RootFolder) ?? titleGroup.RootFolder;
        }
        
        // Keep in current directory
        return titleGroup.RootFolder ?? Path.GetDirectoryName(titleGroup.Discs[0].SourcePath) ?? string.Empty;
    }
}
