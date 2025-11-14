using System.Text.RegularExpressions;

namespace ARK.Core.Psx;

/// <summary>
/// Provides standardized PSX file naming according to the agreed naming rules
/// </summary>
public partial class PsxNamingService
{
    [GeneratedRegex(@"^(.*),\s*(The|A|An)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingArticlePattern();
    
    /// <summary>
    /// Normalize a title by moving trailing articles to the front
    /// Example: "Legend of Dragoon, The" → "The Legend of Dragoon"
    /// </summary>
    public static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title;
        }
        
        var match = TrailingArticlePattern().Match(title.Trim());
        if (match.Success)
        {
            var baseTitle = match.Groups[1].Value.Trim();
            var article = match.Groups[2].Value;
            return $"{article} {baseTitle}";
        }
        
        return title.Trim();
    }
    
    /// <summary>
    /// Generate a standardized filename for a single-disc title
    /// Format: "Title (Region) [vX.Y?] [Serial?].ext"
    /// </summary>
    public static string GenerateSingleDiscName(PsxTitleGroup title, string extension)
    {
        var parts = new List<string> { NormalizeTitle(title.Title) };
        
        if (!string.IsNullOrWhiteSpace(title.Region))
        {
            parts.Add($"({title.Region})");
        }
        
        if (!string.IsNullOrWhiteSpace(title.Version))
        {
            parts.Add($"[{title.Version}]");
        }
        
        var serial = title.Discs.FirstOrDefault()?.Serial;
        if (!string.IsNullOrWhiteSpace(serial))
        {
            parts.Add($"[{serial}]");
        }
        
        return SanitizeFileName(string.Join(" ", parts) + extension);
    }
    
    /// <summary>
    /// Generate a standardized filename for a disc in a multi-disc title
    /// Format: "Title (Region) [vX.Y?] [DiscSerial?].ext"
    /// </summary>
    public static string GenerateMultiDiscName(PsxTitleGroup title, PsxDisc disc, string extension)
    {
        var parts = new List<string> { NormalizeTitle(title.Title) };
        
        if (!string.IsNullOrWhiteSpace(title.Region))
        {
            parts.Add($"({title.Region})");
        }
        
        if (!string.IsNullOrWhiteSpace(title.Version))
        {
            parts.Add($"[{title.Version}]");
        }
        
        if (!string.IsNullOrWhiteSpace(disc.Serial))
        {
            parts.Add($"[{disc.Serial}]");
        }
        
        return SanitizeFileName(string.Join(" ", parts) + extension);
    }
    
    /// <summary>
    /// Generate a standardized filename for a multi-disc playlist
    /// Format: "Title (Region) [vX.Y?].m3u" (no serials)
    /// </summary>
    public static string GeneratePlaylistName(PsxTitleGroup title)
    {
        var parts = new List<string> { NormalizeTitle(title.Title) };
        
        if (!string.IsNullOrWhiteSpace(title.Region))
        {
            parts.Add($"({title.Region})");
        }
        
        if (!string.IsNullOrWhiteSpace(title.Version))
        {
            parts.Add($"[{title.Version}]");
        }
        
        return SanitizeFileName(string.Join(" ", parts) + ".m3u");
    }
    
    /// <summary>
    /// Sanitize a filename by removing invalid characters
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Trim();
    }
}
