using System.Text.RegularExpressions;

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
    private static readonly Regex TrailingParenthetical = new(@"\s*\((?<inner>[^()]+)\)\s*$", RegexOptions.Compiled);
    private static readonly HashSet<string> LanguageTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "EN","ES","JA","DE","FR","IT","PT","PTBR","BR","NL","NO","SV","SE","FI","DA",
        "ZH","KO","RU","PL","CS","HU","AR","TR","EL","HE","ID","KO","MS","TH","VI",
        "ESLA","LA","ESMX","BG","HR","SL","SK","RO"
    };

    public List<PsxRenameOperation> PlanRenames(string rootPath, bool recursive = false, bool restoreArticles = false, bool stripLanguageTags = true)
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
        
        // Group by normalized title/region/extension to detect multi-disc sets even if region is missing
        var multiDiscGroups = allDiscs
            .Where(d => !string.IsNullOrWhiteSpace(d.Title))
            .GroupBy(d => BuildGroupKey(d))
            .Where(g => g.Count() > 1 || g.Any(d => d.DiscNumber.HasValue))
            .Select(g => g.OrderBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase).ToList())
            .ToList();

        var discAssignments = new Dictionary<string, (int DiscNumber, int DiscCount)>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in multiDiscGroups)
        {
            var count = group.Count;
            for (var i = 0; i < count; i++)
            {
                var discNumber = group[i].DiscNumber ?? (i + 1);
                discAssignments[group[i].FilePath] = (discNumber, count);
            }
        }
        
        // Second pass: create operations with corrected disc count
        var operations = new List<PsxRenameOperation>();
        
        foreach (var discInfo in allDiscs)
        {
            // If this title/region/extension combo has multiple entries, set DiscCount
            var correctedDiscInfo = discInfo;
            if (discAssignments.TryGetValue(discInfo.FilePath, out var assignment))
            {
                correctedDiscInfo = correctedDiscInfo with
                {
                    DiscNumber = correctedDiscInfo.DiscNumber ?? assignment.DiscNumber,
                    DiscCount = correctedDiscInfo.DiscCount ?? assignment.DiscCount
                };
            }
            
            var operation = PlanRename(correctedDiscInfo, restoreArticles, stripLanguageTags);
            operations.Add(operation);
        }
        
        return operations;
    }

    private static string BuildGroupKey(PsxDiscInfo disc)
    {
        var title = disc.Title?.Trim() ?? string.Empty;
        var region = disc.Region?.Trim() ?? string.Empty;
        var extension = disc.Extension?.Trim() ?? string.Empty;
        return string.Join('|',
            title.ToUpperInvariant(),
            region.ToUpperInvariant(),
            extension.ToUpperInvariant());
    }
    
    /// <summary>
    /// Plan a rename operation for a single file (or PsxDiscInfo with pre-computed DiscCount)
    /// </summary>
    private PsxRenameOperation PlanRename(PsxDiscInfo discInfo, bool restoreArticles, bool stripLanguageTags)
    {
        var currentFileName = Path.GetFileName(discInfo.FilePath);
        var directory = Path.GetDirectoryName(discInfo.FilePath) ?? string.Empty;

        var effectiveDiscInfo = stripLanguageTags
            ? discInfo with { Title = StripLanguageTags(discInfo.Title) }
            : discInfo;
        
        // Generate canonical name
        var canonicalName = PsxNameFormatter.Format(effectiveDiscInfo, restoreArticles);
        
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
            DiscInfo = effectiveDiscInfo,
            IsAlreadyNamed = isAlreadyNamed,
            Warning = warning
        };
    }

    private static string? StripLanguageTags(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        var sanitized = title.TrimEnd();
        while (true)
        {
            var match = TrailingParenthetical.Match(sanitized);
            if (!match.Success)
            {
                break;
            }

            var inner = match.Groups["inner"].Value;
            if (!LooksLikeLanguageList(inner))
            {
                break;
            }

            sanitized = sanitized[..match.Index].TrimEnd();
        }

        return sanitized;
    }

    private static bool LooksLikeLanguageList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = value.Split(new[] { ',', '/', '|', '&' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        return tokens.All(token =>
        {
            var normalized = token.Trim().Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
            return LanguageTokens.Contains(normalized);
        });
    }
}
