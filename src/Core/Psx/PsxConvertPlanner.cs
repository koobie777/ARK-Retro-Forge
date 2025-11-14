namespace ARK.Core.Psx;

/// <summary>
/// Represents a planned conversion operation.
/// </summary>
public class ConversionOperation
{
    public string TitleName { get; set; } = string.Empty;
    public ConversionDirection Direction { get; set; }
    public ConversionStatus Status { get; set; }
    public string? SourcePath { get; set; }
    public string? TargetPath { get; set; }
    public List<string> FilesToDelete { get; set; } = new();
    public string? SkipReason { get; set; }
    public int DiscNumber { get; set; }
}

public enum ConversionDirection
{
    BinCueToChd,
    ChdToBinCue
}

public enum ConversionStatus
{
    Planned,
    Skipped,
    InProgress,
    Completed,
    Failed
}

/// <summary>
/// Plans conversion operations for PSX titles.
/// </summary>
public class PsxConvertPlanner
{
    private readonly PsxNamingService _namingService = new();

    /// <summary>
    /// Creates a conversion plan for PSX titles.
    /// </summary>
    public List<ConversionOperation> PlanConversions(
        List<PsxTitleGroup> groups,
        ConversionDirection direction,
        bool deleteSource)
    {
        var operations = new List<ConversionOperation>();

        foreach (var group in groups)
        {
            if (direction == ConversionDirection.BinCueToChd)
            {
                PlanBinCueToChd(group, deleteSource, operations);
            }
            else
            {
                PlanChdToBinCue(group, deleteSource, operations);
            }
        }

        return operations;
    }

    private void PlanBinCueToChd(PsxTitleGroup group, bool deleteSource, List<ConversionOperation> operations)
    {
        foreach (var disc in group.Discs)
        {
            // Skip if no BIN/CUE present
            if (!disc.HasBinCue)
            {
                if (!disc.HasChd)
                {
                    operations.Add(new ConversionOperation
                    {
                        TitleName = group.Title,
                        DiscNumber = disc.DiscNumber,
                        Direction = ConversionDirection.BinCueToChd,
                        Status = ConversionStatus.Skipped,
                        SkipReason = "No BIN/CUE files found"
                    });
                }
                continue;
            }

            // If CHD already exists, skip
            if (disc.HasChd)
            {
                operations.Add(new ConversionOperation
                {
                    TitleName = group.Title,
                    DiscNumber = disc.DiscNumber,
                    Direction = ConversionDirection.BinCueToChd,
                    Status = ConversionStatus.Skipped,
                    SkipReason = "Already in CHD format"
                });
                continue;
            }

            // Plan conversion
            var baseName = group.IsMultiDisc
                ? _namingService.GenerateMultiDiscName(group, disc)
                : _namingService.GenerateSingleDiscName(group);

            var targetPath = Path.Combine(
                Path.GetDirectoryName(disc.CuePath!) ?? string.Empty,
                baseName + ".chd");

            var filesToDelete = new List<string>();
            if (deleteSource)
            {
                filesToDelete.Add(disc.CuePath!);
                filesToDelete.AddRange(disc.BinPaths);
            }

            operations.Add(new ConversionOperation
            {
                TitleName = group.Title,
                DiscNumber = disc.DiscNumber,
                Direction = ConversionDirection.BinCueToChd,
                Status = ConversionStatus.Planned,
                SourcePath = disc.CuePath,
                TargetPath = targetPath,
                FilesToDelete = filesToDelete
            });
        }

        // For multi-disc titles, also plan to create/update .m3u playlist
        if (group.IsMultiDisc && group.Discs.Any(d => d.HasBinCue && !d.HasChd))
        {
            // This will be handled during execution
        }
    }

    private void PlanChdToBinCue(PsxTitleGroup group, bool deleteSource, List<ConversionOperation> operations)
    {
        foreach (var disc in group.Discs)
        {
            // Only consider discs with CHD
            if (!disc.HasChd)
            {
                continue;
            }

            var outputDir = Path.GetDirectoryName(disc.ChdPath!) ?? string.Empty;

            var filesToDelete = new List<string>();
            if (deleteSource)
            {
                filesToDelete.Add(disc.ChdPath!);
            }

            operations.Add(new ConversionOperation
            {
                TitleName = group.Title,
                DiscNumber = disc.DiscNumber,
                Direction = ConversionDirection.ChdToBinCue,
                Status = ConversionStatus.Planned,
                SourcePath = disc.ChdPath,
                TargetPath = outputDir,
                FilesToDelete = filesToDelete
            });
        }
    }
}
