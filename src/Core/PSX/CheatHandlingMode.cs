namespace ARK.Core.PSX;

/// <summary>
/// Defines how cheat/utility discs should be handled during rename/convert
/// </summary>
public enum CheatHandlingMode
{
    /// <summary>
    /// Omit cheat discs entirely from operations
    /// </summary>
    Omit,
    
    /// <summary>
    /// Treat cheat discs as standalone titles (default, safest option)
    /// </summary>
    Standalone,
    
    /// <summary>
    /// Associate cheat discs with games (advanced, may cause confusion)
    /// </summary>
    AsDisc
}
