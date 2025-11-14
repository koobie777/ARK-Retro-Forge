namespace ARK.Core.Systems.PSX;

/// <summary>
/// Represents a PSX rename operation with diagnostic information
/// </summary>
public record PsxRenameOperation
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required PsxDiscInfo DiscInfo { get; init; }
    public bool IsAlreadyNamed { get; init; }
    public string? Warning { get; init; }
}

/// <summary>
/// Plans rename operations for PSX files
/// </summary>
public class PsxRenamePlanner
{
    private readonly PsxNameParser _parser;
    
    public PsxRenamePlanner(PsxNameParser? parser = null)
    {
        _parser = parser ?? new PsxNameParser();
    }
    
    /// <summary>
    /// Plan rename operations for PSX files in a directory
    /// </summary>
    public List<PsxRenameOperation> PlanRenames(string rootPath, bool recursive = false)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var psxExtensions = new[] { ".bin", ".cue", ".chd", ".pbp", ".iso" };
        
        // First pass: collect all disc info
        var allDiscs = new List<PsxDiscInfo>();
        
        foreach (var ext in psxExtensions)
        {
            var files = Directory.GetFiles(rootPath, $"*{ext}", searchOption);
            
            foreach (var file in files)
            {
                var discInfo = _parser.Parse(file);
                
                // Skip audio track BINs - they shouldn't be renamed independently
                if (discInfo.IsAudioTrack)
                {
                    continue;
                }
                
                allDiscs.Add(discInfo);
            }
        }
        
        // Group by title+region to detect multi-disc sets
        var multiDiscSets = allDiscs
            .Where(d => !string.IsNullOrWhiteSpace(d.Title) && !string.IsNullOrWhiteSpace(d.Region))
            .GroupBy(d => (d.Title, d.Region, d.Extension))
            .Where(g => g.Count() > 1 || g.Any(d => d.DiscNumber.HasValue))
            .ToDictionary(g => g.Key, g => g.Count());
        
        // Second pass: create operations with corrected disc count
        var operations = new List<PsxRenameOperation>();
        
        foreach (var discInfo in allDiscs)
        {
            var key = (discInfo.Title, discInfo.Region, discInfo.Extension);
            
            // If this title/region/extension combo has multiple entries, set DiscCount
            var correctedDiscInfo = discInfo;
            if (multiDiscSets.TryGetValue(key, out var count) && count > 1)
            {
                correctedDiscInfo = discInfo with { DiscCount = count };
            }
            
            var operation = PlanRename(correctedDiscInfo);
            operations.Add(operation);
        }
        
        return operations;
    }
    
    /// <summary>
    /// Plan a rename operation for a single file (or PsxDiscInfo with pre-computed DiscCount)
    /// </summary>
    private PsxRenameOperation PlanRename(PsxDiscInfo discInfo)
    {
        var currentFileName = Path.GetFileName(discInfo.FilePath);
        var directory = Path.GetDirectoryName(discInfo.FilePath) ?? string.Empty;
        
        // Generate canonical name
        var canonicalName = PsxNameFormatter.Format(discInfo);
        
        // Destination path
        var destinationPath = Path.Combine(directory, canonicalName);
        
        // Check if already named (compare actual current name with canonical, not normalized)
        var isAlreadyNamed = string.Equals(currentFileName, canonicalName, StringComparison.OrdinalIgnoreCase);
        
        // Check for conflicts
        string? warning = discInfo.Warning;
        if (File.Exists(destinationPath) && !string.Equals(discInfo.FilePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            warning = warning != null ? $"{warning}; Destination file already exists" : "Destination file already exists";
        }
        
        return new PsxRenameOperation
        {
            SourcePath = discInfo.FilePath,
            DestinationPath = destinationPath,
            DiscInfo = discInfo,
            IsAlreadyNamed = isAlreadyNamed,
            Warning = warning
        };
    }
}
