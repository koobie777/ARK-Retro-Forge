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
        
        var operations = new List<PsxRenameOperation>();
        
        foreach (var ext in psxExtensions)
        {
            var files = Directory.GetFiles(rootPath, $"*{ext}", searchOption);
            
            foreach (var file in files)
            {
                var operation = PlanRename(file);
                operations.Add(operation);
            }
        }
        
        return operations;
    }
    
    /// <summary>
    /// Plan a rename operation for a single file
    /// </summary>
    public PsxRenameOperation PlanRename(string filePath)
    {
        var discInfo = _parser.Parse(filePath);
        var currentFileName = Path.GetFileName(filePath);
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        
        // Generate canonical name
        var canonicalName = PsxNameFormatter.Format(discInfo);
        
        // Destination path
        var destinationPath = Path.Combine(directory, canonicalName);
        
        // Check if already named (compare actual current name with canonical, not normalized)
        var isAlreadyNamed = string.Equals(currentFileName, canonicalName, StringComparison.OrdinalIgnoreCase);
        
        // Check for conflicts
        string? warning = discInfo.Warning;
        if (File.Exists(destinationPath) && !string.Equals(filePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            warning = warning != null ? $"{warning}; Destination file already exists" : "Destination file already exists";
        }
        
        return new PsxRenameOperation
        {
            SourcePath = filePath,
            DestinationPath = destinationPath,
            DiscInfo = discInfo,
            IsAlreadyNamed = isAlreadyNamed,
            Warning = warning
        };
    }
}
