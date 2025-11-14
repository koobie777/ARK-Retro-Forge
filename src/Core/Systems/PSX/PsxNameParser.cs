using System.Text.RegularExpressions;

namespace ARK.Core.Systems.PSX;

/// <summary>
/// Parses PSX filenames to extract disc metadata
/// </summary>
public partial class PsxNameParser
{
    private readonly IPsxSerialResolver _serialResolver;
    private readonly IPsxContentClassifier _contentClassifier;
    
    // Matches: "Title (Region) [Serial] (Disc N of M).ext" or "(Disc N).ext"
    [GeneratedRegex(@"^(.+?)\s*\(([^)]+)\)\s*\[([^\]]+)\]\s*(?:\(Disc (\d+)(?: of (\d+))?\))?", RegexOptions.IgnoreCase)]
    private static partial Regex FullNamePattern();
    
    // Matches: "(Disc N of M)" or "(Disc N)" anywhere in the filename
    [GeneratedRegex(@"\(Disc (\d+)(?: of (\d+))?\)", RegexOptions.IgnoreCase)]
    private static partial Regex DiscPattern();
    
    // Matches: "Title (Region) [Serial]" - standard format without disc info
    [GeneratedRegex(@"^(.+?)\s*\(([^)]+)\)\s*\[([^\]]+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex StandardPattern();
    
    // Matches: "(Track N)" or "(Track NN)" anywhere in the filename
    [GeneratedRegex(@"\(Track (\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex TrackPattern();
    
    public PsxNameParser(IPsxSerialResolver? serialResolver = null, IPsxContentClassifier? contentClassifier = null)
    {
        _serialResolver = serialResolver ?? new PsxSerialResolver();
        _contentClassifier = contentClassifier ?? new PsxContentClassifier();
    }
    
    /// <summary>
    /// Parse a PSX filename to extract disc metadata
    /// </summary>
    public PsxDiscInfo Parse(string filePath)
    {
        var filename = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        
        string? title = null;
        string? region = null;
        string? serial = null;
        int? discNumber = null;
        int? discCount = null;
        string? warning = null;
        
        // Try to match full standard format with optional disc info
        var match = FullNamePattern().Match(nameWithoutExt);
        if (match.Success)
        {
            title = match.Groups[1].Value.Trim();
            region = match.Groups[2].Value.Trim();
            serial = match.Groups[3].Value.Trim();
            
            if (match.Groups[4].Success)
            {
                discNumber = int.Parse(match.Groups[4].Value);
            }
            
            if (match.Groups[5].Success)
            {
                discCount = int.Parse(match.Groups[5].Value);
            }
        }
        else
        {
            // Try standard pattern without disc info
            match = StandardPattern().Match(nameWithoutExt);
            if (match.Success)
            {
                title = match.Groups[1].Value.Trim();
                region = match.Groups[2].Value.Trim();
                serial = match.Groups[3].Value.Trim();
            }
            
            // Check for disc pattern anywhere in the name
            var discMatch = DiscPattern().Match(nameWithoutExt);
            if (discMatch.Success)
            {
                discNumber = int.Parse(discMatch.Groups[1].Value);
                if (discMatch.Groups[2].Success)
                {
                    discCount = int.Parse(discMatch.Groups[2].Value);
                }
            }
        }
        
        // If no serial found in structured format, try to extract from anywhere in filename
        if (string.IsNullOrWhiteSpace(serial))
        {
            _serialResolver.TryFromFilename(filename, out serial);
        }
        
        // If still no title, use the filename as title
        if (string.IsNullOrWhiteSpace(title))
        {
            title = nameWithoutExt;
            // Remove disc suffix from title if present
            var discMatch = DiscPattern().Match(title);
            if (discMatch.Success)
            {
                title = title[..discMatch.Index].Trim();
            }
            // Remove track suffix from title if present
            var trackMatchTemp = TrackPattern().Match(title);
            if (trackMatchTemp.Success)
            {
                title = title[..trackMatchTemp.Index].Trim();
            }
            // Remove serial brackets from title if present
            if (!string.IsNullOrWhiteSpace(serial))
            {
                title = title.Replace($"[{serial}]", "").Trim();
            }
        }
        
        // Detect multi-track layout
        int? trackNumber = null;
        int? trackCount = null;
        bool isAudioTrack = false;
        string? cueFilePath = null;
        
        var trackMatch = TrackPattern().Match(nameWithoutExt);
        if (trackMatch.Success)
        {
            trackNumber = int.Parse(trackMatch.Groups[1].Value);
            // Track 01 is typically the data track, Track 02+ are audio tracks
            isAudioTrack = trackNumber > 1;
            
            // Try to find associated CUE file
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && extension.Equals(".bin", StringComparison.OrdinalIgnoreCase))
            {
                // Look for a CUE file with similar name (without track suffix)
                var baseName = nameWithoutExt[..trackMatch.Index].Trim();
                var potentialCue = Path.Combine(directory, baseName + ".cue");
                if (File.Exists(potentialCue))
                {
                    cueFilePath = potentialCue;
                }
            }
        }
        
        // Classify content type
        var contentType = _contentClassifier.Classify(filename, serial);
        
        // Generate warnings
        if (string.IsNullOrWhiteSpace(serial))
        {
            if (contentType == PsxContentType.Cheat)
            {
                warning = "Cheat/utility disc; serial intentionally not enforced";
            }
            else if (contentType == PsxContentType.Educational)
            {
                warning = "Educational/Lightspan content; non-standard ID format";
            }
            else
            {
                warning = "Serial number not found";
            }
        }
        
        // Additional warning for multi-track audio files
        if (isAudioTrack && string.IsNullOrWhiteSpace(serial))
        {
            warning = warning != null ? $"{warning}; Audio track from multi-track disc" : "Audio track from multi-track disc";
        }
        
        return new PsxDiscInfo
        {
            FilePath = filePath,
            Title = title,
            Region = region,
            Serial = serial,
            DiscNumber = discNumber,
            DiscCount = discCount,
            ContentType = contentType,
            Extension = extension,
            Warning = warning,
            TrackNumber = trackNumber,
            TrackCount = trackCount,
            IsAudioTrack = isAudioTrack,
            CueFilePath = cueFilePath
        };
    }
}
