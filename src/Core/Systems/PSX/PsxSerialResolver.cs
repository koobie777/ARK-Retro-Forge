using System.Text.RegularExpressions;

namespace ARK.Core.Systems.PSX;

/// <summary>
/// Default implementation of PSX serial resolver
/// </summary>
public partial class PsxSerialResolver : IPsxSerialResolver
{
    // Standard PSX serial pattern: SLUS-01234, SCUS-94567, etc.
    [GeneratedRegex(@"\[([A-Z]{4}-\d{5})\]", RegexOptions.IgnoreCase)]
    private static partial Regex StandardSerialPattern();
    
    // Lightspan serial pattern: LSP-12345
    [GeneratedRegex(@"\[?(LSP-\d{5,6})\]?", RegexOptions.IgnoreCase)]
    private static partial Regex LightspanSerialPattern();
    
    public bool TryFromFilename(string filename, out string? serial)
    {
        serial = null;
        
        // Try standard PSX serial first
        var match = StandardSerialPattern().Match(filename);
        if (match.Success)
        {
            serial = match.Groups[1].Value.ToUpperInvariant();
            return true;
        }
        
        // Try Lightspan serial
        match = LightspanSerialPattern().Match(filename);
        if (match.Success)
        {
            serial = match.Groups[1].Value.ToUpperInvariant();
            return true;
        }
        
        return false;
    }
    
    public bool TryFromDat(string title, string? region, out string? serial)
    {
        // TODO: Implement DAT-based serial resolution
        // This is a placeholder for future enhancement
        serial = null;
        return false;
    }
    
    public bool TryFromDiscProbe(string filePath, out string? serial)
    {
        // TODO: Implement disc probing for serial extraction
        // This would require reading the disc image and extracting SYSTEM.CNF or similar
        serial = null;
        return false;
    }
}
