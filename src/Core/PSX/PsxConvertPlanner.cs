namespace ARK.Core.PSX;

/// <summary>
/// Plans PSX BIN/CUE to CHD conversion operations
/// </summary>
public class PsxConvertPlanner
{
    private readonly CheatHandlingMode _cheatMode;

    public PsxConvertPlanner(CheatHandlingMode cheatMode = CheatHandlingMode.Standalone)
    {
        _cheatMode = cheatMode;
    }

    /// <summary>
    /// Scan directory for CUE files and generate conversion plan
    /// </summary>
    public async Task<List<PsxConvertOperation>> PlanConversionsAsync(string rootPath, bool recursive)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        
        var cueFiles = Directory.GetFiles(rootPath, "*.cue", searchOption).ToList();

        var operations = new List<PsxConvertOperation>();

        foreach (var cueFile in cueFiles)
        {
            var operation = await PlanConversionForCueAsync(cueFile);
            if (operation != null)
            {
                operations.Add(operation);
            }
        }

        return operations;
    }

    /// <summary>
    /// Plan conversion for a single CUE file
    /// </summary>
    private async Task<PsxConvertOperation?> PlanConversionForCueAsync(string cuePath)
    {
        // Parse associated BIN files from CUE
        var binFiles = await ParseBinFilesFromCueAsync(cuePath);
        
        if (binFiles.Count == 0)
        {
            // No BIN files found - skip with warning
            return null;
        }

        // Verify all BIN files exist
        var missingBins = binFiles.Where(b => !File.Exists(b)).ToList();
        if (missingBins.Any())
        {
            return new PsxConvertOperation
            {
                SourceCuePath = cuePath,
                SourceBinPaths = binFiles,
                DestinationChdPath = string.Empty,
                Warning = $"Missing BIN files: {string.Join(", ", missingBins.Select(Path.GetFileName))}"
            };
        }

        // Parse disc info
        var filename = Path.GetFileName(cuePath);
        var title = ExtractTitle(filename);
        var discNumber = DiscSuffixNormalizer.ParseDiscNumber(filename);
        var (serial, _) = await PsxSerialResolver.ResolveSerialAsync(cuePath);
        var (isCheat, isEducational) = PsxDiscClassifier.ClassifyDisc(title, serial);

        // Skip cheat discs if mode is Omit
        if (isCheat && _cheatMode == CheatHandlingMode.Omit)
        {
            return null;
        }

        // Generate CHD filename
        var directory = Path.GetDirectoryName(cuePath) ?? string.Empty;
        var chdFileName = Path.ChangeExtension(Path.GetFileName(cuePath), ".chd");
        var chdPath = Path.Combine(directory, chdFileName);

        string? warning = null;
        if (string.IsNullOrWhiteSpace(serial))
        {
            warning = "Serial number not found";
        }

        return new PsxConvertOperation
        {
            SourceCuePath = cuePath,
            SourceBinPaths = binFiles,
            DestinationChdPath = chdPath,
            Title = title,
            DiscNumber = discNumber,
            Serial = serial,
            Warning = warning
        };
    }

    /// <summary>
    /// Parse BIN file references from a CUE file
    /// </summary>
    private async Task<List<string>> ParseBinFilesFromCueAsync(string cuePath)
    {
        var binFiles = new List<string>();
        var directory = Path.GetDirectoryName(cuePath) ?? string.Empty;

        try
        {
            var lines = await File.ReadAllLinesAsync(cuePath);
            
            foreach (var line in lines)
            {
                // Look for FILE "filename.bin" BINARY lines
                var match = System.Text.RegularExpressions.Regex.Match(line, @"FILE\s+""([^""]+)""\s+BINARY", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var binFileName = match.Groups[1].Value;
                    var binPath = Path.Combine(directory, binFileName);
                    binFiles.Add(binPath);
                }
            }
        }
        catch
        {
            // Ignore CUE parsing errors
        }

        return binFiles;
    }

    /// <summary>
    /// Extract title from filename
    /// </summary>
    private string ExtractTitle(string filename)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        
        // Remove serial tag if present
        nameWithoutExt = System.Text.RegularExpressions.Regex.Replace(nameWithoutExt, @"\[[A-Z]{4}-\d{5}\]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        // Remove region tag if present
        nameWithoutExt = System.Text.RegularExpressions.Regex.Replace(nameWithoutExt, @"\((USA|Europe|Japan|World)\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        // Remove disc suffix
        nameWithoutExt = DiscSuffixNormalizer.RemoveDiscSuffix(nameWithoutExt);
        
        return nameWithoutExt.Trim();
    }
}
