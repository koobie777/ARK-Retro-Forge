using System.Text.RegularExpressions;

namespace ARK.Core.PSX;

/// <summary>
/// Normalizes disc suffix patterns to canonical "(Disc N)" format
/// </summary>
public partial class DiscSuffixNormalizer
{
    // Match patterns like "(Disc 2 of 2)", "(Disc 2)", "(Disk 1)", etc.
    [GeneratedRegex(@"\(Dis[ck]\s+(\d+)(?:\s+of\s+\d+)?\)", RegexOptions.IgnoreCase)]
    private static partial Regex DiscSuffixPattern();
    
    // Match CD/DVD patterns like "(CD 1)", "(CD1)", etc.
    [GeneratedRegex(@"\((?:CD|DVD)\s*(\d+)(?:\s+of\s+\d+)?\)", RegexOptions.IgnoreCase)]
    private static partial Regex CdDvdPattern();

    /// <summary>
    /// Parse disc number from filename
    /// </summary>
    /// <param name="filename">Filename to parse</param>
    /// <returns>Disc number (1-based) or null if not found</returns>
    public static int? ParseDiscNumber(string filename)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        
        // Try disc pattern first
        var discMatch = DiscSuffixPattern().Match(nameWithoutExt);
        if (discMatch.Success && int.TryParse(discMatch.Groups[1].Value, out int discNum))
        {
            return discNum;
        }
        
        // Try CD/DVD pattern
        var cdMatch = CdDvdPattern().Match(nameWithoutExt);
        if (cdMatch.Success && int.TryParse(cdMatch.Groups[1].Value, out int cdNum))
        {
            return cdNum;
        }
        
        return null;
    }

    /// <summary>
    /// Normalize disc suffix to canonical "(Disc N)" format
    /// </summary>
    /// <param name="filename">Filename to normalize</param>
    /// <param name="discNumber">Disc number (1-based)</param>
    /// <returns>Normalized filename with canonical disc suffix</returns>
    public static string NormalizeDiscSuffix(string filename, int discNumber)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        var extension = Path.GetExtension(filename);
        
        // Remove any existing disc suffix patterns
        nameWithoutExt = DiscSuffixPattern().Replace(nameWithoutExt, "");
        nameWithoutExt = CdDvdPattern().Replace(nameWithoutExt, "");
        
        // Trim any trailing whitespace
        nameWithoutExt = nameWithoutExt.TrimEnd();
        
        // Add canonical disc suffix
        return $"{nameWithoutExt} (Disc {discNumber}){extension}";
    }

    /// <summary>
    /// Remove disc suffix from filename (for single-disc titles)
    /// </summary>
    /// <param name="filename">Filename to process</param>
    /// <returns>Filename without disc suffix</returns>
    public static string RemoveDiscSuffix(string filename)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        var extension = Path.GetExtension(filename);
        
        // Remove disc suffix patterns
        nameWithoutExt = DiscSuffixPattern().Replace(nameWithoutExt, "");
        nameWithoutExt = CdDvdPattern().Replace(nameWithoutExt, "");
        
        // Trim trailing whitespace
        nameWithoutExt = nameWithoutExt.TrimEnd();
        
        return nameWithoutExt + extension;
    }

    /// <summary>
    /// Check if filename has a disc suffix
    /// </summary>
    public static bool HasDiscSuffix(string filename)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        return DiscSuffixPattern().IsMatch(nameWithoutExt) || CdDvdPattern().IsMatch(nameWithoutExt);
    }
}
