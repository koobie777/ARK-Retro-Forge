using System.Text.RegularExpressions;

namespace ARK.Core.Psx;

/// <summary>
/// Scans directories and groups PSX files into logical title groups
/// </summary>
public partial class PsxGrouper
{
    private static readonly HashSet<string> CueExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cue" };
    private static readonly HashSet<string> ChdExtensions = new(StringComparer.OrdinalIgnoreCase) { ".chd" };
    private static readonly HashSet<string> PbpExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pbp" };
    private static readonly HashSet<string> M3uExtensions = new(StringComparer.OrdinalIgnoreCase) { ".m3u" };
    
    [GeneratedRegex(@"\(Disc\s*(\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex DiscNumberPattern();
    
    [GeneratedRegex(@"\(([^)]+)\)")]
    private static partial Regex RegionPattern();
    
    [GeneratedRegex(@"\[([A-Z]{4}[-_]\d{5})\]", RegexOptions.IgnoreCase)]
    private static partial Regex SerialPattern();
    
    [GeneratedRegex(@"\[v([\d.]+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();
    
    /// <summary>
    /// Scan a directory and group PSX files into title groups
    /// </summary>
    public IEnumerable<PsxTitleGroup> ScanAndGroup(string rootPath, bool recursive)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");
        }
        
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var allFiles = Directory.GetFiles(rootPath, "*.*", searchOption);
        
        // Find all PSX-related files
        var psxFiles = allFiles
            .Where(f => IsPsxFile(f))
            .ToList();
        
        // Group files by potential title
        var titleGroups = GroupFilesByTitle(psxFiles);
        
        return titleGroups;
    }
    
    private static bool IsPsxFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return CueExtensions.Contains(ext) || 
               ChdExtensions.Contains(ext) || 
               PbpExtensions.Contains(ext) ||
               M3uExtensions.Contains(ext);
    }
    
    private IEnumerable<PsxTitleGroup> GroupFilesByTitle(List<string> files)
    {
        // Group files by their parent directory
        var directoryGroups = files.GroupBy(f => Path.GetDirectoryName(f) ?? string.Empty);
        
        foreach (var dirGroup in directoryGroups)
        {
            var filesInDir = dirGroup.ToList();
            
            // Check if files are in a dedicated game folder (heuristic: folder contains only PSX files)
            var allFilesInDir = Directory.GetFiles(dirGroup.Key);
            var isGameFolder = allFilesInDir.Length == filesInDir.Count || 
                               filesInDir.Count > 1; // Multiple PSX files suggest dedicated folder
            
            // Try to group by common title pattern
            var titleGroups = GroupByCommonTitle(filesInDir);
            
            foreach (var titleGroup in titleGroups)
            {
                var discs = titleGroup.Value
                    .Select(f => CreateDisc(f, titleGroup.Value))
                    .OrderBy(d => d.DiscNumber)
                    .ToList();
                
                var title = ExtractTitle(titleGroup.Key);
                var region = ExtractRegion(titleGroup.Key);
                var version = ExtractVersion(titleGroup.Key);
                
                yield return new PsxTitleGroup
                {
                    Title = title,
                    Region = region,
                    Version = version,
                    Discs = discs,
                    RootFolder = isGameFolder ? dirGroup.Key : null
                };
            }
        }
    }
    
    private Dictionary<string, List<string>> GroupByCommonTitle(List<string> files)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            
            // Remove disc indicators to get common title
            var commonTitle = DiscNumberPattern().Replace(fileName, "").Trim();
            
            if (!groups.ContainsKey(commonTitle))
            {
                groups[commonTitle] = new List<string>();
            }
            
            groups[commonTitle].Add(file);
        }
        
        return groups;
    }
    
    private PsxDisc CreateDisc(string filePath, List<string> relatedFiles)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        
        var discNumber = ExtractDiscNumber(fileName);
        var serial = ExtractSerial(fileName);
        
        PsxDiscFormat format;
        List<string> binFiles = new();
        
        if (CueExtensions.Contains(extension))
        {
            format = PsxDiscFormat.BinCue;
            binFiles = FindAssociatedBinFiles(filePath);
        }
        else if (ChdExtensions.Contains(extension))
        {
            format = PsxDiscFormat.Chd;
        }
        else if (PbpExtensions.Contains(extension))
        {
            format = PsxDiscFormat.Pbp;
        }
        else
        {
            // M3U files are handled separately, skip for now
            format = PsxDiscFormat.BinCue;
        }
        
        return new PsxDisc
        {
            DiscNumber = discNumber,
            Serial = serial,
            SourcePath = filePath,
            Format = format,
            BinFiles = binFiles
        };
    }
    
    private static int ExtractDiscNumber(string fileName)
    {
        var match = DiscNumberPattern().Match(fileName);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var discNum))
        {
            return discNum;
        }
        
        return 1; // Default to disc 1 for single-disc titles
    }
    
    private static string? ExtractSerial(string fileName)
    {
        var match = SerialPattern().Match(fileName);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return null;
    }
    
    private static string ExtractTitle(string fileName)
    {
        // Remove region, version, and serial patterns
        var title = RegionPattern().Replace(fileName, "");
        title = SerialPattern().Replace(title, "");
        title = VersionPattern().Replace(title, "");
        title = DiscNumberPattern().Replace(title, "");
        
        return title.Trim();
    }
    
    private static string? ExtractRegion(string fileName)
    {
        var match = RegionPattern().Match(fileName);
        if (match.Success)
        {
            var region = match.Groups[1].Value;
            // Common region mappings
            if (region.Contains("USA", StringComparison.OrdinalIgnoreCase) || 
                region.Contains("US", StringComparison.OrdinalIgnoreCase))
            {
                return "USA";
            }
            if (region.Contains("Europe", StringComparison.OrdinalIgnoreCase) || 
                region.Contains("EU", StringComparison.OrdinalIgnoreCase))
            {
                return "Europe";
            }
            if (region.Contains("Japan", StringComparison.OrdinalIgnoreCase) || 
                region.Contains("JP", StringComparison.OrdinalIgnoreCase))
            {
                return "Japan";
            }
            
            return region;
        }
        
        return null;
    }
    
    private static string? ExtractVersion(string fileName)
    {
        var match = VersionPattern().Match(fileName);
        if (match.Success)
        {
            return $"v{match.Groups[1].Value}";
        }

        return null;
    }
    
    private static List<string> FindAssociatedBinFiles(string cuePath)
    {
        var binFiles = new List<string>();
        var directory = Path.GetDirectoryName(cuePath);
        var baseName = Path.GetFileNameWithoutExtension(cuePath);
        
        if (directory == null)
        {
            return binFiles;
        }
        
        // Look for .bin files with same base name
        var potentialBins = Directory.GetFiles(directory, $"{baseName}*.bin", SearchOption.TopDirectoryOnly);
        binFiles.AddRange(potentialBins);
        
        // Also parse CUE file to find referenced BIN files
        try
        {
            var cueContent = File.ReadAllLines(cuePath);
            foreach (var line in cueContent)
            {
                if (line.Contains("FILE", StringComparison.OrdinalIgnoreCase) && 
                    line.Contains(".bin", StringComparison.OrdinalIgnoreCase))
                {
                    var binFileName = ExtractBinFileNameFromCueLine(line);
                    if (!string.IsNullOrEmpty(binFileName))
                    {
                        var binPath = Path.Combine(directory, binFileName);
                        if (File.Exists(binPath) && !binFiles.Contains(binPath))
                        {
                            binFiles.Add(binPath);
                        }
                    }
                }
            }
        }
        catch
        {
            // If we can't parse the CUE file, just use the heuristic above
        }
        
        return binFiles;
    }
    
    private static string? ExtractBinFileNameFromCueLine(string line)
    {
        var startQuote = line.IndexOf('"');
        if (startQuote == -1)
        {
            return null;
        }
        
        var endQuote = line.IndexOf('"', startQuote + 1);
        if (endQuote == -1)
        {
            return null;
        }
        
        return line.Substring(startQuote + 1, endQuote - startQuote - 1);
    }
}
