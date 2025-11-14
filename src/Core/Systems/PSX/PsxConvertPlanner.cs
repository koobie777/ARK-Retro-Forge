namespace ARK.Core.Systems.PSX;

/// <summary>
/// Represents a PSX convert operation (CUE to CHD)
/// </summary>
public record PsxConvertOperation
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required PsxDiscInfo DiscInfo { get; init; }
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
    public List<PsxConvertOperation> PlanConversions(string rootPath, bool recursive = false)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var operations = new List<PsxConvertOperation>();
        
        var cueFiles = Directory.GetFiles(rootPath, "*.cue", searchOption);
        
        foreach (var cueFile in cueFiles)
        {
            var operation = PlanConversion(cueFile);
            operations.Add(operation);
        }
        
        return operations;
    }
    
    /// <summary>
    /// Plan a conversion operation for a single CUE file
    /// </summary>
    public PsxConvertOperation PlanConversion(string cuePath)
    {
        var discInfo = _parser.Parse(cuePath);
        var directory = Path.GetDirectoryName(cuePath) ?? string.Empty;
        
        // Generate canonical CHD filename
        var chdDiscInfo = discInfo with { Extension = ".chd" };
        var chdFileName = PsxNameFormatter.Format(chdDiscInfo);
        var chdPath = Path.Combine(directory, chdFileName);
        
        // Check if already converted
        var alreadyConverted = File.Exists(chdPath);
        
        string? warning = discInfo.Warning;
        
        return new PsxConvertOperation
        {
            SourcePath = cuePath,
            DestinationPath = chdPath,
            DiscInfo = discInfo,
            AlreadyConverted = alreadyConverted,
            Warning = warning
        };
    }
}
