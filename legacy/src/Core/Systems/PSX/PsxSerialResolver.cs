using System.Text;
using System.Text.RegularExpressions;

namespace ARK.Core.Systems.PSX;

/// <summary>
/// Default implementation of PSX serial resolver
/// </summary>
public partial class PsxSerialResolver : IPsxSerialResolver
{
    private const int ProbeWindowBytes = 512 * 1024; // 512KB should be enough for SYSTEM.CNF

    // Standard PSX serial pattern: SLUS-01234, SCUS-94567, etc.
    [GeneratedRegex(@"\[([A-Z]{4}-\d{5})\]", RegexOptions.IgnoreCase)]
    private static partial Regex StandardSerialPattern();
    
    // Lightspan serial pattern: LSP-12345
    [GeneratedRegex(@"\[?(LSP-\d{5,6})\]?", RegexOptions.IgnoreCase)]
    private static partial Regex LightspanSerialPattern();

    // Serial markers embedded in SYSTEM.CNF (e.g., SLUS_012.34;1)
    [GeneratedRegex(@"([A-Z]{4})_(\d{3})\.(\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex DiscSerialPattern();
    
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
        serial = null;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var extension = Path.GetExtension(filePath);
        if (!extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            var bufferLength = (int)Math.Min(stream.Length, ProbeWindowBytes);
            if (bufferLength <= 0)
            {
                return false;
            }

            var buffer = new byte[bufferLength];
            var read = stream.Read(buffer, 0, bufferLength);
            if (read <= 0)
            {
                return false;
            }

            var ascii = Encoding.ASCII.GetString(buffer, 0, read);
            var match = DiscSerialPattern().Match(ascii);
            if (!match.Success)
            {
                return false;
            }

            var prefix = match.Groups[1].Value.ToUpperInvariant();
            var firstBlock = match.Groups[2].Value;
            var secondBlock = match.Groups[3].Value;
            serial = $"{prefix}-{firstBlock}{secondBlock}".Trim();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
