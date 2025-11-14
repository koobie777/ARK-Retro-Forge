namespace ARK.Core.Systems.PSX;

/// <summary>
/// Default implementation of PSX content classifier
/// </summary>
public class PsxContentClassifier : IPsxContentClassifier
{
    private static readonly HashSet<string> CheatDiscKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "xploder", "action replay", "gameshark", "game shark", 
        "gamepro action", "codebreaker", "cheat"
    };
    
    private static readonly HashSet<string> EducationalKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "lightspan", "tales 1", "tales i", "p.k.'s", "pk's",
        "science is elementary", "secret of googol", "math studio",
        "educational", "learning"
    };
    
    private static readonly HashSet<string> DemoKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "demo", "preview", "sampler", "kiosk"
    };
    
    public PsxContentType Classify(string filename, string? serial)
    {
        var lowerFilename = filename.ToLowerInvariant();
        
        // Check for Lightspan serial pattern (LSP-xxxxx)
        if (serial?.StartsWith("LSP-", StringComparison.OrdinalIgnoreCase) == true)
        {
            return PsxContentType.Educational;
        }
        
        // Check for cheat disc keywords
        foreach (var keyword in CheatDiscKeywords)
        {
            if (lowerFilename.Contains(keyword.ToLowerInvariant()))
            {
                return PsxContentType.Cheat;
            }
        }
        
        // Check for educational keywords
        foreach (var keyword in EducationalKeywords)
        {
            if (lowerFilename.Contains(keyword.ToLowerInvariant()))
            {
                return PsxContentType.Educational;
            }
        }
        
        // Check for demo keywords
        foreach (var keyword in DemoKeywords)
        {
            if (lowerFilename.Contains(keyword.ToLowerInvariant()))
            {
                return PsxContentType.Demo;
            }
        }
        
        return PsxContentType.Mainline;
    }
}
