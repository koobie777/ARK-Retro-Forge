namespace ARK.Core.Systems.PSX;

/// <summary>
/// Service for resolving PSX serial numbers from various sources
/// </summary>
public interface IPsxSerialResolver
{
    /// <summary>
    /// Attempt to extract a PSX serial from the filename
    /// </summary>
    /// <param name="filename">The filename to parse</param>
    /// <param name="serial">The extracted serial, if found</param>
    /// <returns>True if a serial was found</returns>
    bool TryFromFilename(string filename, out string? serial);
    
    /// <summary>
    /// Attempt to resolve a serial from DAT files (future implementation)
    /// </summary>
    /// <param name="title">Game title</param>
    /// <param name="region">Region code</param>
    /// <param name="serial">The resolved serial, if found</param>
    /// <returns>True if a serial was resolved</returns>
    bool TryFromDat(string title, string? region, out string? serial);
    
    /// <summary>
    /// Attempt to extract a serial by probing the disc image (future implementation)
    /// </summary>
    /// <param name="filePath">Path to the disc image</param>
    /// <param name="serial">The extracted serial, if found</param>
    /// <returns>True if a serial was found</returns>
    bool TryFromDiscProbe(string filePath, out string? serial);
}
