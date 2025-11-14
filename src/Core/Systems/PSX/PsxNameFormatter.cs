namespace ARK.Core.Systems.PSX;

/// <summary>
/// Formats PSX disc information into canonical filenames
/// </summary>
public class PsxNameFormatter
{
    /// <summary>
    /// Generate a canonical filename from PSX disc metadata
    /// </summary>
    /// <param name="discInfo">The disc metadata</param>
    /// <returns>Formatted filename with extension</returns>
    public static string Format(PsxDiscInfo discInfo)
    {
        var parts = new List<string>();
        
        // Add title
        if (!string.IsNullOrWhiteSpace(discInfo.Title))
        {
            parts.Add(discInfo.Title.Trim());
        }
        
        // Add region
        if (!string.IsNullOrWhiteSpace(discInfo.Region))
        {
            parts.Add($"({discInfo.Region.Trim()})");
        }
        
        // Add serial
        if (!string.IsNullOrWhiteSpace(discInfo.Serial))
        {
            parts.Add($"[{discInfo.Serial.Trim()}]");
        }
        
        var baseName = string.Join(" ", parts);
        
        // Add disc suffix for multi-disc titles
        // Single-disc titles should NOT have (Disc 1)
        if (discInfo.IsMultiDisc && discInfo.DiscNumber.HasValue)
        {
            baseName += $" (Disc {discInfo.DiscNumber.Value})";
        }
        
        // Add extension
        if (!string.IsNullOrWhiteSpace(discInfo.Extension))
        {
            baseName += discInfo.Extension;
        }
        
        return SanitizeFileName(baseName);
    }
    
    /// <summary>
    /// Normalize disc suffix from "(Disc N of M)" to "(Disc N)"
    /// </summary>
    /// <param name="filename">Filename to normalize</param>
    /// <returns>Normalized filename</returns>
    public static string NormalizeDiscSuffix(string filename)
    {
        var extension = Path.GetExtension(filename);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        
        // Replace "(Disc N of M)" with "(Disc N)"
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            nameWithoutExt,
            @"\(Disc (\d+) of \d+\)",
            "(Disc $1)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        
        return normalized + extension;
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
