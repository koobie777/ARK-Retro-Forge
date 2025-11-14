using System.Text.RegularExpressions;

namespace ARK.Core.Systems.PSX;

/// <summary>
/// Parses PSX filenames to extract disc metadata
/// </summary>
public partial class PsxNameParser
{
    private readonly IPsxSerialResolver _serialResolver;
    private readonly IPsxContentClassifier _contentClassifier;
    
    // Matches: "Title (Region) [Serial] (Disc N of M).ext" or "(Disc N).ext"
    [GeneratedRegex(@"^(.+?)\s*\(([^)]+)\)\s*\[([^\]]+)\]\s*(?:\(Disc (\d+)(?: of (\d+))?\))?", RegexOptions.IgnoreCase)]
    private static partial Regex FullNamePattern();
    
    // Matches: "(Disc N of M)" or "(Disc N)" anywhere in the filename
    [GeneratedRegex(@"\(Disc (\d+)(?: of (\d+))?\)", RegexOptions.IgnoreCase)]
    private static partial Regex DiscPattern();
    
    // Matches: "Title (Region) [Serial]" - standard format without disc info
    [GeneratedRegex(@"^(.+?)\s*\(([^)]+)\)\s*\[([^\]]+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex StandardPattern();
    
    public PsxNameParser(IPsxSerialResolver? serialResolver = null, IPsxContentClassifier? contentClassifier = null)
    {
        _serialResolver = serialResolver ?? new PsxSerialResolver();
        _contentClassifier = contentClassifier ?? new PsxContentClassifier();
    }
    
    /// <summary>
    /// Parse a PSX filename to extract disc metadata
    /// </summary>
    public PsxDiscInfo Parse(string filePath)
    {
        var filename = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        
        string? title = null;
        string? region = null;
        string? serial = null;
        int? discNumber = null;
        int? discCount = null;
        string? warning = null;
        
        // Try to match full standard format with optional disc info
        var match = FullNamePattern().Match(nameWithoutExt);
        if (match.Success)
        {
            title = match.Groups[1].Value.Trim();
            region = match.Groups[2].Value.Trim();
            serial = match.Groups[3].Value.Trim();
            
            if (match.Groups[4].Success)
            {
                discNumber = int.Parse(match.Groups[4].Value);
            }
            
            if (match.Groups[5].Success)
            {
                discCount = int.Parse(match.Groups[5].Value);
            }
        }
        else
        {
            // Try standard pattern without disc info
            match = StandardPattern().Match(nameWithoutExt);
            if (match.Success)
            {
                title = match.Groups[1].Value.Trim();
                region = match.Groups[2].Value.Trim();
                serial = match.Groups[3].Value.Trim();
            }
            
            // Check for disc pattern anywhere in the name
            var discMatch = DiscPattern().Match(nameWithoutExt);
            if (discMatch.Success)
            {
                discNumber = int.Parse(discMatch.Groups[1].Value);
                if (discMatch.Groups[2].Success)
                {
                    discCount = int.Parse(discMatch.Groups[2].Value);
                }
            }
        }
        
        // If no serial found in structured format, try to extract from anywhere in filename
        if (string.IsNullOrWhiteSpace(serial))
        {
            _serialResolver.TryFromFilename(filename, out serial);
        }
        
        // If still no title, use the filename as title
        if (string.IsNullOrWhiteSpace(title))
        {
            title = nameWithoutExt;
            // Remove disc suffix from title if present
            var discMatch = DiscPattern().Match(title);
            if (discMatch.Success)
            {
                title = title[..discMatch.Index].Trim();
            }
        }
        
        // Classify content type
        var contentType = _contentClassifier.Classify(filename, serial);
        
        // Generate warnings
        if (string.IsNullOrWhiteSpace(serial))
        {
            if (contentType == PsxContentType.Cheat)
            {
                warning = "Cheat/utility disc; serial intentionally not enforced";
            }
            else if (contentType == PsxContentType.Educational)
            {
                warning = "Educational/Lightspan content; non-standard ID format";
            }
            else
            {
                warning = "Serial number not found";
            }
        }
        
        return new PsxDiscInfo
        {
            FilePath = filePath,
            Title = title,
            Region = region,
            Serial = serial,
            DiscNumber = discNumber,
            DiscCount = discCount,
            ContentType = contentType,
            Extension = extension,
            Warning = warning
        };
    }
}
