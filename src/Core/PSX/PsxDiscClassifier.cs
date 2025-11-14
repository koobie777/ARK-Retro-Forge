using System.Text.RegularExpressions;

namespace ARK.Core.PSX;

/// <summary>
/// Classifies PlayStation discs (cheat, educational, mainline games)
/// </summary>
public partial class PsxDiscClassifier
{
    // Known cheat disc patterns
    private static readonly string[] CheatPatterns = 
    {
        "gameshark", "game shark",
        "xploder", "x-ploder",
        "action replay",
        "codebreaker", "code breaker",
        "pro action replay",
        "cheat", "cheats"
    };

    // Known educational/Lightspan patterns
    private static readonly string[] EducationalPatterns =
    {
        "lightspan",
        "adventures in learning",
        "click start",
        "leapfrog"
    };

    // Known Lightspan serial patterns (these often have anomalies)
    [GeneratedRegex(@"LSP-\d+", RegexOptions.IgnoreCase)]
    private static partial Regex LightspanSerialPattern();

    /// <summary>
    /// Check if a disc is a cheat/utility disc based on title
    /// </summary>
    public static bool IsCheatDisc(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var titleLower = title.ToLowerInvariant();
        return CheatPatterns.Any(pattern => titleLower.Contains(pattern));
    }

    /// <summary>
    /// Check if a disc is an educational disc based on title
    /// </summary>
    public static bool IsEducationalDisc(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var titleLower = title.ToLowerInvariant();
        return EducationalPatterns.Any(pattern => titleLower.Contains(pattern));
    }

    /// <summary>
    /// Check if a serial number is a Lightspan serial
    /// </summary>
    public static bool IsLightspanSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        return LightspanSerialPattern().IsMatch(serial);
    }

    /// <summary>
    /// Classify a disc based on title and serial
    /// </summary>
    public static (bool isCheat, bool isEducational) ClassifyDisc(string? title, string? serial)
    {
        bool isCheat = false;
        bool isEducational = false;

        if (!string.IsNullOrWhiteSpace(title))
        {
            isCheat = IsCheatDisc(title);
            isEducational = IsEducationalDisc(title);
        }

        // Lightspan serials override title-based classification
        if (IsLightspanSerial(serial))
        {
            isEducational = true;
            isCheat = false;
        }

        return (isCheat, isEducational);
    }
}
