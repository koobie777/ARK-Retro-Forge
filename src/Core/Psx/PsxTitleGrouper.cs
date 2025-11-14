using System.Text.RegularExpressions;

namespace ARK.Core.Psx;

/// <summary>
/// Groups PSX files into logical titles with disc information.
/// </summary>
public class PsxTitleGrouper
{
    private static readonly Regex DiscNumberRegex = new(@"\(Disc\s*(\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SerialRegex = new(@"([A-Z]{4}[-_]?\d{5})", RegexOptions.Compiled);

    /// <summary>
    /// Scans a directory and groups PSX files into title groups.
    /// </summary>
    public List<PsxTitleGroup> GroupTitles(string rootPath, bool recursive)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var titleGroups = new Dictionary<string, PsxTitleGroup>(StringComparer.OrdinalIgnoreCase);

        // Find all relevant PSX files
        var cueFiles = Directory.GetFiles(rootPath, "*.cue", searchOption);
        var chdFiles = Directory.GetFiles(rootPath, "*.chd", searchOption);
        var m3uFiles = Directory.GetFiles(rootPath, "*.m3u", searchOption);

        // Group by game directory (parent folder for multi-disc, or file location for single-disc)
        foreach (var cueFile in cueFiles)
        {
            ProcessDiscFile(cueFile, titleGroups, true);
        }

        foreach (var chdFile in chdFiles)
        {
            ProcessDiscFile(chdFile, titleGroups, false);
        }

        foreach (var m3uFile in m3uFiles)
        {
            ProcessPlaylistFile(m3uFile, titleGroups);
        }

        return titleGroups.Values.OrderBy(g => g.Title).ToList();
    }

    private void ProcessDiscFile(string filePath, Dictionary<string, PsxTitleGroup> titleGroups, bool isCue)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;

        // Extract disc number if present
        var discNumberMatch = DiscNumberRegex.Match(fileName);
        var discNumber = discNumberMatch.Success ? int.Parse(discNumberMatch.Groups[1].Value) : 1;

        // Extract serial if present
        var serialMatch = SerialRegex.Match(fileName);
        var serial = serialMatch.Success ? serialMatch.Groups[1].Value.Replace("_", "-") : null;

        // Determine the base title name (remove disc markers, serials, etc.)
        var title = CleanTitleName(fileName);

        // Use directory as game key for grouping
        var gameKey = directory;

        if (!titleGroups.TryGetValue(gameKey, out var group))
        {
            group = new PsxTitleGroup
            {
                Title = title,
                Region = ExtractRegion(fileName),
                Version = ExtractVersion(fileName),
                GameDirectory = directory
            };
            titleGroups[gameKey] = group;
        }

        // Find or create disc entry
        var disc = group.Discs.FirstOrDefault(d => d.DiscNumber == discNumber);
        if (disc == null)
        {
            disc = new PsxDisc { DiscNumber = discNumber };
            group.Discs.Add(disc);
        }

        if (serial != null)
        {
            disc.Serial = serial;
        }

        if (isCue)
        {
            disc.CuePath = filePath;
            // Find associated BIN files
            disc.BinPaths = FindBinFilesForCue(filePath);
        }
        else
        {
            disc.ChdPath = filePath;
        }

        // Sort discs by number
        group.Discs = group.Discs.OrderBy(d => d.DiscNumber).ToList();
    }

    private void ProcessPlaylistFile(string m3uPath, Dictionary<string, PsxTitleGroup> titleGroups)
    {
        var directory = Path.GetDirectoryName(m3uPath) ?? string.Empty;

        // Try to find the matching title group
        if (titleGroups.TryGetValue(directory, out var group))
        {
            group.PlaylistPath = m3uPath;
        }
        else
        {
            // Create a new group for this playlist
            var fileName = Path.GetFileNameWithoutExtension(m3uPath);
            var title = CleanTitleName(fileName);

            var newGroup = new PsxTitleGroup
            {
                Title = title,
                Region = ExtractRegion(fileName),
                Version = ExtractVersion(fileName),
                GameDirectory = directory,
                PlaylistPath = m3uPath
            };
            titleGroups[directory] = newGroup;
        }
    }

    private List<string> FindBinFilesForCue(string cuePath)
    {
        var binFiles = new List<string>();
        var cueDirectory = Path.GetDirectoryName(cuePath) ?? string.Empty;

        try
        {
            var cueContent = File.ReadAllText(cuePath);
            var fileMatches = Regex.Matches(cueContent, @"FILE\s+""([^""]+)""\s+BINARY", RegexOptions.IgnoreCase);

            foreach (Match match in fileMatches)
            {
                var binFile = match.Groups[1].Value;
                var binPath = Path.Combine(cueDirectory, binFile);
                if (File.Exists(binPath))
                {
                    binFiles.Add(binPath);
                }
            }
        }
        catch
        {
            // If we can't parse CUE, just look for BIN files with the same base name
            var baseName = Path.GetFileNameWithoutExtension(cuePath);
            var binPath = Path.Combine(cueDirectory, baseName + ".bin");
            if (File.Exists(binPath))
            {
                binFiles.Add(binPath);
            }
        }

        return binFiles;
    }

    private string CleanTitleName(string fileName)
    {
        // Remove common patterns: (Disc N), [SLUS-12345], (USA), etc.
        var cleaned = fileName;

        // Remove disc markers
        cleaned = DiscNumberRegex.Replace(cleaned, "");

        // Remove serials in brackets
        cleaned = Regex.Replace(cleaned, @"\[[A-Z]{4}[-_]?\d{5}\]", "", RegexOptions.IgnoreCase);

        // Remove region/version markers
        cleaned = Regex.Replace(cleaned, @"\([^)]*\)", "");
        cleaned = Regex.Replace(cleaned, @"\[[^\]]*\]", "");

        // Clean up extra spaces and trim
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    private string ExtractRegion(string fileName)
    {
        // Look for region markers like (USA), (Europe), (Japan)
        var regionMatch = Regex.Match(fileName, @"\((USA|US|Europe|EU|Japan|JP|World)\)", RegexOptions.IgnoreCase);
        if (regionMatch.Success)
        {
            var region = regionMatch.Groups[1].Value.ToUpperInvariant();
            return region switch
            {
                "US" => "USA",
                "EU" => "Europe",
                "JP" => "Japan",
                _ => region
            };
        }
        return "Unknown";
    }

    private string? ExtractVersion(string fileName)
    {
        // Look for version markers like [v1.0], [v1.1], etc.
        var versionMatch = Regex.Match(fileName, @"\[v(\d+\.\d+)\]", RegexOptions.IgnoreCase);
        if (versionMatch.Success)
        {
            return versionMatch.Groups[1].Value;
        }

        return null;
    }
}
