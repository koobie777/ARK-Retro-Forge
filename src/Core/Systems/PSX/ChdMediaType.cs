namespace ARK.Core.Systems.PSX;

/// <summary>
/// Represents the media type for CHD conversion
/// </summary>
public enum ChdMediaType
{
    /// <summary>
    /// CD-ROM media (PSX, PS1, Sega CD, etc.)
    /// </summary>
    CD,
    
    /// <summary>
    /// DVD media (PS2, PSP, Xbox, etc.)
    /// </summary>
    DVD,
    
    /// <summary>
    /// Unknown or unsupported media type
    /// </summary>
    Unknown
}

/// <summary>
/// Helper for determining CHD media type from file context
/// </summary>
public static class ChdMediaTypeHelper
{
    /// <summary>
    /// Determine CHD media type from file extension and system context
    /// </summary>
    /// <param name="extension">File extension (e.g., ".bin", ".cue", ".iso")</param>
    /// <param name="systemContext">System context (e.g., "PSX", "PS2", "PSP")</param>
    /// <returns>The appropriate media type for CHD conversion</returns>
    public static ChdMediaType DetermineFromExtensionOrContext(string? extension, string? systemContext = null)
    {
        // Normalize inputs
        var ext = extension?.ToLowerInvariant()?.TrimStart('.');
        var context = systemContext?.ToUpperInvariant();
        
        // PSX/PS1 context - always CD
        if (context == "PSX" || context == "PS1")
        {
            return ChdMediaType.CD;
        }
        
        // TODO: PS2 context - DVD
        // if (context == "PS2")
        // {
        //     return ChdMediaType.DVD;
        // }
        
        // TODO: PSP context - DVD (UMD is DVD-based)
        // if (context == "PSP")
        // {
        //     return ChdMediaType.DVD;
        // }
        
        // Infer from extension
        return ext switch
        {
            // BIN/CUE are typically CD format (PSX, Sega CD, etc.)
            "bin" or "cue" => ChdMediaType.CD,
            
            // ISO could be either CD or DVD - default to CD for PSX
            // TODO: Add DVD detection for larger ISOs or PS2/PSP context
            "iso" => ChdMediaType.CD,
            
            // CHD already compressed - check context if available
            "chd" => context == "PS2" || context == "PSP" ? ChdMediaType.DVD : ChdMediaType.CD,
            
            // Unknown
            _ => ChdMediaType.Unknown
        };
    }
    
    /// <summary>
    /// Get the chdman command for the media type
    /// </summary>
    /// <param name="mediaType">The media type</param>
    /// <returns>The chdman subcommand (e.g., "createcd", "createdvd")</returns>
    public static string GetChdmanCommand(ChdMediaType mediaType)
    {
        return mediaType switch
        {
            ChdMediaType.CD => "createcd",
            ChdMediaType.DVD => "createdvd",
            _ => throw new ArgumentException($"Unsupported media type: {mediaType}", nameof(mediaType))
        };
    }
}
