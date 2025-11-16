namespace ARK.Core.Systems.PSX;

/// <summary>
/// Represents the desired conversion target.
/// </summary>
public enum PsxConversionTarget
{
    Chd,
    BinCue,
    Iso
}

/// <summary>
/// Represents a PSX convert operation (multi-target)
/// </summary>
public record PsxConvertOperation
{
    public required string SourcePath { get; init; }
    public string? DestinationPath { get; init; }
    public string? DestinationCuePath { get; init; }
    public string? DestinationBinPath { get; init; }
    public required PsxDiscInfo DiscInfo { get; init; }
    public required PsxConversionTarget Target { get; init; }
    public required ChdMediaType MediaType { get; init; }
    public bool AlreadyConverted { get; init; }
    public string? Warning { get; init; }
}

/// <summary>
/// Plans convert operations for PSX CUE files to CHD format
/// </summary>
public class PsxConvertPlanner
{
    private readonly PsxNameParser _parser;
    
    public PsxConvertPlanner(PsxNameParser? parser = null)
    {
        _parser = parser ?? new PsxNameParser();
    }
    
    /// <summary>
    /// Plan convert operations for CUE files in a directory
    /// </summary>
    /// <param name="rootPath">Root directory to scan</param>
    /// <param name="recursive">Whether to scan recursively</param>
    /// <param name="rebuild">Force rebuild even if destination exists</param>
    /// <param name="target">Desired target format</param>
    public List<PsxConvertOperation> PlanConversions(string rootPath, bool recursive = false, bool rebuild = false, PsxConversionTarget target = PsxConversionTarget.Chd)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return target switch
        {
            PsxConversionTarget.Chd => PlanCueToChd(rootPath, searchOption, rebuild),
            PsxConversionTarget.BinCue => PlanChdToBinCue(rootPath, searchOption, rebuild),
            PsxConversionTarget.Iso => PlanChdToIso(rootPath, searchOption, rebuild),
            _ => new List<PsxConvertOperation>()
        };
    }

    private List<PsxConvertOperation> PlanCueToChd(string rootPath, SearchOption searchOption, bool rebuild)
    {
        var operations = new List<PsxConvertOperation>();
        var cueFiles = Directory.GetFiles(rootPath, "*.cue", searchOption);

        foreach (var cueFile in cueFiles)
        {
            var directory = Path.GetDirectoryName(cueFile) ?? string.Empty;
            var discInfo = _parser.Parse(cueFile);
            var mediaType = ChdMediaTypeHelper.DetermineFromFilePath(cueFile, "PSX");
            var chdDiscInfo = discInfo with { Extension = ".chd" };
            var destinationPath = Path.Combine(directory, PsxNameFormatter.Format(chdDiscInfo));
            var alreadyConverted = !rebuild && File.Exists(destinationPath);

            operations.Add(new PsxConvertOperation
            {
                SourcePath = cueFile,
                DestinationPath = destinationPath,
                DiscInfo = discInfo,
                Target = PsxConversionTarget.Chd,
                MediaType = mediaType,
                AlreadyConverted = alreadyConverted,
                Warning = null
            });
        }

        return operations;
    }

    private List<PsxConvertOperation> PlanChdToBinCue(string rootPath, SearchOption searchOption, bool rebuild)
    {
        var operations = new List<PsxConvertOperation>();
        var chdFiles = Directory.GetFiles(rootPath, "*.chd", searchOption);

        foreach (var chdFile in chdFiles)
        {
            var directory = Path.GetDirectoryName(chdFile) ?? string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(chdFile);
            var destinationCue = Path.Combine(directory, baseName + ".cue");
            var destinationBin = Path.Combine(directory, baseName + ".bin");
            var mediaType = ChdMediaTypeHelper.DetermineFromFilePath(chdFile);
            var alreadyConverted = !rebuild && File.Exists(destinationCue) && File.Exists(destinationBin);

            var warning = mediaType == ChdMediaType.DVD
                ? "DVD images should be extracted to ISO instead of BIN/CUE"
                : null;

            operations.Add(new PsxConvertOperation
            {
                SourcePath = chdFile,
                DestinationCuePath = destinationCue,
                DestinationBinPath = destinationBin,
                DiscInfo = new PsxDiscInfo { FilePath = chdFile },
                Target = PsxConversionTarget.BinCue,
                MediaType = mediaType,
                AlreadyConverted = alreadyConverted,
                Warning = warning
            });
        }

        return operations;
    }

    private List<PsxConvertOperation> PlanChdToIso(string rootPath, SearchOption searchOption, bool rebuild)
    {
        var operations = new List<PsxConvertOperation>();
        var chdFiles = Directory.GetFiles(rootPath, "*.chd", searchOption);

        foreach (var chdFile in chdFiles)
        {
            var directory = Path.GetDirectoryName(chdFile) ?? string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(chdFile);
            var destinationPath = Path.Combine(directory, baseName + ".iso");
            var mediaType = ChdMediaTypeHelper.DetermineFromFilePath(chdFile);
            var alreadyConverted = !rebuild && File.Exists(destinationPath);

            string? warning = null;
            if (mediaType == ChdMediaType.CD)
            {
                warning = "ISO extraction is intended for DVD media; use --to bin for CD titles";
            }

            operations.Add(new PsxConvertOperation
            {
                SourcePath = chdFile,
                DestinationPath = destinationPath,
                DiscInfo = new PsxDiscInfo { FilePath = chdFile },
                Target = PsxConversionTarget.Iso,
                MediaType = mediaType,
                AlreadyConverted = alreadyConverted,
                Warning = warning
            });
        }

        return operations;
    }
}
