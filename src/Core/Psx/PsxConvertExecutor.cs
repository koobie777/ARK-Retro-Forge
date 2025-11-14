namespace ARK.Core.Psx;

/// <summary>
/// Result of a conversion operation.
/// </summary>
public class ConversionResult
{
    public bool Success { get; set; }
    public List<string> Messages { get; set; } = new();
    public int ConvertedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<ConversionOperation> Operations { get; set; } = new();
}

/// <summary>
/// Executes conversion operations for PSX files.
/// </summary>
public class PsxConvertExecutor
{
    private readonly IChdTool _chdTool;

    public PsxConvertExecutor(IChdTool chdTool)
    {
        _chdTool = chdTool ?? throw new ArgumentNullException(nameof(chdTool));
    }

    /// <summary>
    /// Executes the planned conversion operations.
    /// </summary>
    public async Task<ConversionResult> ExecuteAsync(
        List<ConversionOperation> operations,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var result = new ConversionResult
        {
            Success = true,
            Operations = operations
        };

        if (dryRun)
        {
            result.Messages.Add("DRY RUN - No conversions will be performed");
            result.ConvertedCount = operations.Count(o => o.Status == ConversionStatus.Planned);
            result.SkippedCount = operations.Count(o => o.Status == ConversionStatus.Skipped);
            return result;
        }

        foreach (var operation in operations)
        {
            if (operation.Status == ConversionStatus.Skipped)
            {
                result.SkippedCount++;
                result.Messages.Add($"Skipped {operation.TitleName} (Disc {operation.DiscNumber}): {operation.SkipReason}");
                continue;
            }

            try
            {
                operation.Status = ConversionStatus.InProgress;
                await ExecuteOperationAsync(operation, cancellationToken);
                operation.Status = ConversionStatus.Completed;
                result.ConvertedCount++;
                result.Messages.Add($"Converted {operation.TitleName} (Disc {operation.DiscNumber})");
            }
            catch (Exception ex)
            {
                operation.Status = ConversionStatus.Failed;
                result.FailedCount++;
                result.Success = false;
                var errorMsg = $"Failed to convert {operation.TitleName} (Disc {operation.DiscNumber}): {ex.Message}";
                result.Errors.Add(errorMsg);
                result.Messages.Add(errorMsg);
            }
        }

        return result;
    }

    private async Task ExecuteOperationAsync(ConversionOperation operation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.SourcePath))
        {
            throw new InvalidOperationException("Source path is required");
        }

        if (string.IsNullOrEmpty(operation.TargetPath))
        {
            throw new InvalidOperationException("Target path is required");
        }

        int exitCode;

        if (operation.Direction == ConversionDirection.BinCueToChd)
        {
            // Convert CUE to CHD
            exitCode = await _chdTool.ConvertCueToChd(operation.SourcePath, operation.TargetPath, cancellationToken);
        }
        else
        {
            // Convert CHD to BIN/CUE
            exitCode = await _chdTool.ConvertChdToBinCue(operation.SourcePath, operation.TargetPath, cancellationToken);
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"chdman exited with code {exitCode}");
        }

        // Only delete source files if conversion was successful
        if (operation.FilesToDelete.Any())
        {
            foreach (var file in operation.FilesToDelete)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
