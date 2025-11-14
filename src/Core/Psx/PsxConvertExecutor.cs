namespace ARK.Core.Psx;

/// <summary>
/// Executes PSX conversion plans
/// </summary>
public class PsxConvertExecutor
{
    private readonly IChdTool _chdTool;
    private readonly int _maxParallelism;
    
    /// <summary>
    /// Result of executing a conversion plan
    /// </summary>
    public record ExecutionResult
    {
        public int ConversionsSucceeded { get; init; }
        public int ConversionsFailed { get; init; }
        public int SourcesDeleted { get; init; }
        public int PlaylistsWritten { get; init; }
        public int Skipped { get; init; }
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    }
    
    public PsxConvertExecutor(IChdTool chdTool, int maxParallelism = 4)
    {
        _chdTool = chdTool ?? throw new ArgumentNullException(nameof(chdTool));
        _maxParallelism = Math.Max(1, maxParallelism);
    }
    
    /// <summary>
    /// Execute a conversion plan
    /// </summary>
    public async Task<ExecutionResult> ExecuteAsync(ConvertPlan plan, bool dryRun, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var conversionsSucceeded = 0;
        var conversionsFailed = 0;
        var sourcesDeleted = 0;
        var playlistsWritten = 0;
        var skipped = 0;
        
        if (dryRun)
        {
            // In dry-run mode, just validate and return counts
            return new ExecutionResult
            {
                ConversionsSucceeded = plan.TotalConversions,
                PlaylistsWritten = plan.TotalPlaylistWrites,
                Skipped = plan.TotalSkipped
            };
        }
        
        // Count skipped titles
        skipped = plan.TitlePlans.Count(t => t.SkipReason != null);
        
        // Execute conversions with bounded parallelism
        var semaphore = new SemaphoreSlim(_maxParallelism);
        var tasks = new List<Task>();
        
        foreach (var titlePlan in plan.TitlePlans.Where(t => t.SkipReason == null))
        {
            foreach (var conversionOp in titlePlan.ConversionOperations)
            {
                var task = Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var success = await ExecuteConversionAsync(conversionOp, plan.Direction, cancellationToken);
                        
                        if (success)
                        {
                            Interlocked.Increment(ref conversionsSucceeded);
                            
                            // Delete sources if requested and conversion succeeded
                            if (conversionOp.DeleteSourceAfterSuccess)
                            {
                                var deleted = DeleteSourceFiles(conversionOp);
                                Interlocked.Add(ref sourcesDeleted, deleted);
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref conversionsFailed);
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref conversionsFailed);
                        lock (errors)
                        {
                            errors.Add($"Conversion failed for {conversionOp.SourcePath}: {ex.Message}");
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);
                
                tasks.Add(task);
            }
        }
        
        await Task.WhenAll(tasks);
        
        // Write playlists after all conversions complete
        foreach (var titlePlan in plan.TitlePlans.Where(t => t.PlaylistOperation != null))
        {
            try
            {
                await WritePlaylistAsync(titlePlan.PlaylistOperation!, cancellationToken);
                playlistsWritten++;
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to write playlist {titlePlan.PlaylistOperation!.PlaylistPath}: {ex.Message}");
            }
        }
        
        return new ExecutionResult
        {
            ConversionsSucceeded = conversionsSucceeded,
            ConversionsFailed = conversionsFailed,
            SourcesDeleted = sourcesDeleted,
            PlaylistsWritten = playlistsWritten,
            Skipped = skipped,
            Errors = errors
        };
    }
    
    private async Task<bool> ExecuteConversionAsync(
        ConversionOperation conversionOp, 
        ConversionDirection direction, 
        CancellationToken cancellationToken)
    {
        try
        {
            if (direction == ConversionDirection.BinCueToChd)
            {
                var exitCode = await _chdTool.ConvertCueToChd(
                    conversionOp.SourcePath, 
                    conversionOp.DestinationPath, 
                    cancellationToken);
                
                return exitCode == 0 && File.Exists(conversionOp.DestinationPath);
            }
            else // ChdToBinCue
            {
                var exitCode = await _chdTool.ConvertChdToBinCue(
                    conversionOp.SourcePath, 
                    conversionOp.DestinationPath, 
                    cancellationToken);
                
                // For CHD->BIN/CUE, destination is a directory
                // Check if CUE file was created
                var baseName = Path.GetFileNameWithoutExtension(conversionOp.SourcePath);
                var expectedCue = Path.Combine(conversionOp.DestinationPath, baseName + ".cue");
                
                return exitCode == 0 && File.Exists(expectedCue);
            }
        }
        catch
        {
            return false;
        }
    }
    
    private static int DeleteSourceFiles(ConversionOperation conversionOp)
    {
        var deleted = 0;
        
        try
        {
            // Delete the main source file
            if (File.Exists(conversionOp.SourcePath))
            {
                File.Delete(conversionOp.SourcePath);
                deleted++;
            }
            
            // Delete associated files (e.g., BIN files)
            foreach (var associatedFile in conversionOp.AssociatedFilesToDelete)
            {
                if (File.Exists(associatedFile))
                {
                    File.Delete(associatedFile);
                    deleted++;
                }
            }
        }
        catch
        {
            // If deletion fails, we don't fail the conversion
        }
        
        return deleted;
    }
    
    private static async Task WritePlaylistAsync(PlaylistOperation playlistOp, CancellationToken cancellationToken)
    {
        var lines = playlistOp.DiscFiles.Select(f => f).ToList();
        await File.WriteAllLinesAsync(playlistOp.PlaylistPath, lines, cancellationToken);
    }
}
