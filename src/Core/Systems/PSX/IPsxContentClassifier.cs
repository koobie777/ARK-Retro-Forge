namespace ARK.Core.Systems.PSX;

/// <summary>
/// Service for classifying PSX content types (cheat, educational, mainline, etc.)
/// </summary>
public interface IPsxContentClassifier
{
    /// <summary>
    /// Classify a PSX title based on filename, serial, and other metadata
    /// </summary>
    /// <param name="filename">The filename to classify</param>
    /// <param name="serial">The PSX serial, if known</param>
    /// <returns>The classified content type</returns>
    PsxContentType Classify(string filename, string? serial);
}
