namespace ARK.Core.Systems.PSX;

/// <summary>
/// Classifies PSX content types for diagnostic and handling purposes
/// </summary>
public enum PsxContentType
{
    /// <summary>
    /// Standard mainline PSX game
    /// </summary>
    Mainline,
    
    /// <summary>
    /// Cheat disc (e.g., Action Replay, GameShark, Xploder)
    /// </summary>
    Cheat,
    
    /// <summary>
    /// Educational disc (e.g., Lightspan series, educational titles)
    /// </summary>
    Educational,
    
    /// <summary>
    /// Demo or promotional disc
    /// </summary>
    Demo,
    
    /// <summary>
    /// Unknown or unclassified content
    /// </summary>
    Unknown
}
