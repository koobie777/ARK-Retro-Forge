using System.Text.RegularExpressions;

namespace ARK.Core.PSX;

/// <summary>
/// Plans PSX rename operations
/// </summary>
public partial class PsxRenamePlanner
{
    private readonly CheatHandlingMode _cheatMode;

    public PsxRenamePlanner(CheatHandlingMode cheatMode = CheatHandlingMode.Standalone)
    {
        _cheatMode = cheatMode;
    }

    // Match region patterns like (USA), (Europe), (Japan), etc.
    [GeneratedRegex(@"\((USA|Europe|Japan|World|Germany|France|Spain|Italy|Korea|Asia)\)", RegexOptions.IgnoreCase)]
    private static partial Regex RegionPattern();

    /// <summary>
    /// Scan directory for PSX files and generate rename plan
    /// </summary>
    public async Task<List<PsxRenameOperation>> PlanRenamesAsync(string rootPath, bool recursive)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var psxExtensions = new[] { ".cue", ".bin", ".chd" };
        
        var files = Directory.GetFiles(rootPath, "*.*", searchOption)
            .Where(f => psxExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Parse disc info for each file
        var discs = new List<PsxDiscInfo>();
        foreach (var file in files)
        {
            var discInfo = await ParseDiscInfoAsync(file);
            discs.Add(discInfo);
        }

        // Group discs by title
        var groups = PsxTitleGrouper.GroupByTitle(discs);

        // Generate rename operations
        var operations = new List<PsxRenameOperation>();
        foreach (var group in groups.Values)
        {
            var groupOps = GenerateRenameOperationsForGroup(group);
            operations.AddRange(groupOps);
        }

        return operations;
    }

    /// <summary>
    /// Parse disc information from a file
    /// </summary>
    private async Task<PsxDiscInfo> ParseDiscInfoAsync(string filePath)
    {
        var filename = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath);

        // Extract title from filename
        var title = ExtractTitle(filename);
        
        // Extract region
        var region = ExtractRegion(filename);
        
        // Resolve serial
        var (serial, serialSource) = await PsxSerialResolver.ResolveSerialAsync(filePath);
        
        // Parse disc number
        var discNumber = DiscSuffixNormalizer.ParseDiscNumber(filename);
        
        // Classify disc
        var (isCheat, isEducational) = PsxDiscClassifier.ClassifyDisc(title, serial);

        var warnings = new List<string>();
        
        // Warn about missing serial
        if (string.IsNullOrWhiteSpace(serial))
        {
            warnings.Add("Serial number not found");
        }
        
        // Warn about Lightspan
        if (PsxDiscClassifier.IsLightspanSerial(serial))
        {
            warnings.Add("Lightspan educational disc detected");
        }

        return new PsxDiscInfo
        {
            FilePath = filePath,
            Title = title,
            Region = region,
            Serial = serial,
            DiscNumber = discNumber,
            Extension = extension,
            IsCheatDisc = isCheat,
            IsEducationalDisc = isEducational,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Extract title from filename
    /// </summary>
    private string ExtractTitle(string filename)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        
        // Remove serial tag if present
        nameWithoutExt = Regex.Replace(nameWithoutExt, @"\[[A-Z]{4}-\d{5}\]", "", RegexOptions.IgnoreCase);
        
        // Remove region tag if present
        nameWithoutExt = RegionPattern().Replace(nameWithoutExt, "");
        
        // Remove disc suffix
        nameWithoutExt = DiscSuffixNormalizer.RemoveDiscSuffix(nameWithoutExt);
        
        return nameWithoutExt.Trim();
    }

    /// <summary>
    /// Extract region from filename
    /// </summary>
    private string? ExtractRegion(string filename)
    {
        var match = RegionPattern().Match(filename);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Generate rename operations for a group of discs
    /// </summary>
    private List<PsxRenameOperation> GenerateRenameOperationsForGroup(List<PsxDiscInfo> group)
    {
        var operations = new List<PsxRenameOperation>();
        
        // Handle cheat discs based on mode
        if (group.Any(d => d.IsCheatDisc))
        {
            if (_cheatMode == CheatHandlingMode.Omit)
            {
                // Skip cheat discs entirely
                return operations;
            }
            // Standalone mode: treat each cheat disc individually
        }

        var isMultiDisc = PsxTitleGrouper.IsMultiDisc(group);
        var canonicalTitle = PsxTitleGrouper.GetCanonicalTitle(group);

        foreach (var disc in group)
        {
            var operation = GenerateRenameOperation(disc, canonicalTitle, isMultiDisc);
            operations.Add(operation);
        }

        return operations;
    }

    /// <summary>
    /// Generate a single rename operation
    /// </summary>
    private PsxRenameOperation GenerateRenameOperation(PsxDiscInfo disc, string canonicalTitle, bool isMultiDisc)
    {
        var directory = Path.GetDirectoryName(disc.FilePath) ?? string.Empty;
        var extension = disc.Extension ?? Path.GetExtension(disc.FilePath);

        // Build canonical filename: "Title (Region) [SERIAL]" or "Title (Region) [SERIAL] (Disc N)"
        var parts = new List<string> { canonicalTitle };

        if (!string.IsNullOrWhiteSpace(disc.Region))
        {
            parts.Add($"({disc.Region})");
        }

        if (!string.IsNullOrWhiteSpace(disc.Serial))
        {
            parts.Add($"[{disc.Serial}]");
        }

        var baseName = string.Join(" ", parts);

        // Add disc suffix for multi-disc titles
        string newFileName;
        if (isMultiDisc && disc.DiscNumber.HasValue)
        {
            newFileName = $"{baseName} (Disc {disc.DiscNumber.Value}){extension}";
        }
        else if (isMultiDisc && !disc.DiscNumber.HasValue)
        {
            // Multi-disc but no disc number detected - this is a warning
            newFileName = $"{baseName}{extension}";
        }
        else
        {
            // Single disc - no disc suffix
            newFileName = $"{baseName}{extension}";
        }

        var newPath = Path.Combine(directory, newFileName);
        var currentFileName = Path.GetFileName(disc.FilePath);
        var isAlreadyNamed = string.Equals(currentFileName, newFileName, StringComparison.OrdinalIgnoreCase);

        // Build warning message
        string? warning = null;
        if (disc.Warnings.Any())
        {
            warning = string.Join("; ", disc.Warnings);
        }

        if (File.Exists(newPath) && !string.Equals(disc.FilePath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            warning = warning == null ? "Destination file already exists" : $"{warning}; Destination file already exists";
        }

        return new PsxRenameOperation
        {
            SourcePath = disc.FilePath,
            DestinationPath = newPath,
            DestinationFileName = newFileName,
            IsAlreadyNamed = isAlreadyNamed,
            Warning = warning,
            Serial = disc.Serial,
            IsCheatDisc = disc.IsCheatDisc,
            IsEducationalDisc = disc.IsEducationalDisc
        };
    }
}
