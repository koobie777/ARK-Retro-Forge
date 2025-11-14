using System.Text.RegularExpressions;

namespace ARK.Core.PSX;

/// <summary>
/// Resolves PlayStation serial numbers from filenames and CUE files
/// </summary>
public partial class PsxSerialResolver
{
    // Match serial patterns in filenames: [SLUS-00001], [SCUS-94163], etc.
    [GeneratedRegex(@"\[([A-Z]{4}-\d{5})\]", RegexOptions.IgnoreCase)]
    private static partial Regex FilenameSerialPattern();

    // Match serial in CUE file CATALOG line: CATALOG 0000000000001
    [GeneratedRegex(@"CATALOG\s+(\d{13})", RegexOptions.IgnoreCase)]
    private static partial Regex CueCatalogPattern();

    // Match common PSX serial prefixes
    [GeneratedRegex(@"^(SLUS|SCUS|SLPS|SCPS|SLES|SCES|LSP)-\d{5}$", RegexOptions.IgnoreCase)]
    private static partial Regex ValidSerialPattern();

    /// <summary>
    /// Extract serial from filename
    /// </summary>
    public static string? ExtractSerialFromFilename(string filename)
    {
        var match = FilenameSerialPattern().Match(filename);
        if (match.Success)
        {
            var serial = match.Groups[1].Value.ToUpperInvariant();
            if (ValidSerialPattern().IsMatch(serial))
            {
                return serial;
            }
        }
        return null;
    }

    /// <summary>
    /// Attempt to extract serial from CUE file
    /// </summary>
    public static async Task<string?> ExtractSerialFromCueAsync(string cuePath)
    {
        if (!File.Exists(cuePath))
        {
            return null;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(cuePath);
            
            foreach (var line in lines)
            {
                // Look for CATALOG line (some PSX CUEs have this)
                var catalogMatch = CueCatalogPattern().Match(line);
                if (catalogMatch.Success)
                {
                    // This is a simplified approach; real serial extraction from CUE
                    // would require parsing the BIN file's system area
                    // For now, we just note that a catalog exists
                    continue;
                }
            }
        }
        catch
        {
            // Ignore CUE parsing errors
        }

        return null;
    }

    /// <summary>
    /// Validate a serial number
    /// </summary>
    public static bool IsValidSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        return ValidSerialPattern().IsMatch(serial);
    }

    /// <summary>
    /// Resolve serial from multiple sources (filename first, then CUE probe)
    /// </summary>
    public static async Task<(string? serial, string? source)> ResolveSerialAsync(string filePath)
    {
        // Try filename first
        var filenameSerial = ExtractSerialFromFilename(filePath);
        if (!string.IsNullOrWhiteSpace(filenameSerial))
        {
            return (filenameSerial, "filename");
        }

        // If this is a CUE file, try to extract from it
        if (Path.GetExtension(filePath).Equals(".cue", StringComparison.OrdinalIgnoreCase))
        {
            var cueSerial = await ExtractSerialFromCueAsync(filePath);
            if (!string.IsNullOrWhiteSpace(cueSerial))
            {
                return (cueSerial, "cue");
            }
        }

        // Try to find associated CUE file for BIN files
        if (Path.GetExtension(filePath).Equals(".bin", StringComparison.OrdinalIgnoreCase))
        {
            var cuePath = Path.ChangeExtension(filePath, ".cue");
            if (File.Exists(cuePath))
            {
                var cueSerial = await ExtractSerialFromCueAsync(cuePath);
                if (!string.IsNullOrWhiteSpace(cueSerial))
                {
                    return (cueSerial, "cue");
                }
            }
        }

        return (null, null);
    }
}
