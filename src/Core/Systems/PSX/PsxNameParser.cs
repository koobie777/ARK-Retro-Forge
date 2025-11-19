using System.Text.RegularExpressions;
using ARK.Core.Dat;

namespace ARK.Core.Systems.PSX;

/// <summary>
/// Parses PSX filenames to extract disc metadata
/// </summary>
public partial class PsxNameParser
{
    private readonly IPsxSerialResolver _serialResolver;
    private readonly IPsxContentClassifier _contentClassifier;
    private readonly DatMetadataIndex _datMetadata;
    
    // Matches: "Title (Region) [Serial] (Disc N of M).ext" or "(Disc N).ext"
    [GeneratedRegex(@"^(.+?)\s*\(([^)]+)\)\s*\[([^\]]+)\]\s*(?:\(Disc (\d+)(?: of (\d+))?\))?", RegexOptions.IgnoreCase)]
    private static partial Regex FullNamePattern();
    
    // Matches: "(Disc N of M)" or "(Disc N)" anywhere in the filename
    [GeneratedRegex(@"\(Disc (\d+)(?: of (\d+))?\)", RegexOptions.IgnoreCase)]
    private static partial Regex DiscPattern();
    
    // Matches: "Title (Region) [Serial]" - standard format without disc info
    [GeneratedRegex(@"^(.+?)\s*\(([^)]+)\)\s*\[([^\]]+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex StandardPattern();

    // Matches: "Title (Region)" - simple format without serial
    [GeneratedRegex(@"^(.+?)\s*\(([^)]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex SimplePattern();
    
    // Matches: "(Track N)" or "(Track NN)" anywhere in the filename
    [GeneratedRegex(@"\(Track (\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex TrackPattern();

    // Matches: "(Rev N)", "(v1.0)", "(Ver 1.0)" anywhere in the filename
    [GeneratedRegex(@"\((?:Rev|v|Ver\.?)\s*[\d.]+\)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();
    
    public PsxNameParser(
        IPsxSerialResolver? serialResolver = null,
        IPsxContentClassifier? contentClassifier = null,
        DatMetadataIndex? datMetadata = null)
    {
        _serialResolver = serialResolver ?? new PsxSerialResolver();
        _contentClassifier = contentClassifier ?? new PsxContentClassifier();
        _datMetadata = datMetadata ?? DatMetadataCache.ForSystem("psx");
    }
    
    public PsxContentType Classify(string filePath, string? serial)
    {
        var filename = Path.GetFileName(filePath);
        return _contentClassifier.Classify(filename, serial);
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
        string? version = null;
        int? discNumber = null;
        int? discCount = null;
        string? warning = null;
        var serialCandidates = new List<PsxSerialCandidate>();

        // Extract version early
        var versionMatch = VersionPattern().Match(nameWithoutExt);
        if (versionMatch.Success)
        {
            version = versionMatch.Value.Trim('(', ')');
        }

        // 1. Internal Probe (Highest Priority for Identity)
        // Try to read the serial directly from the disc header
        var probed = _serialResolver.TryFromDiscProbe(filePath, out serial);
        if (!probed && extension.Equals(".cue", StringComparison.OrdinalIgnoreCase))
        {
            var dataTrack = TryResolveDataTrackFromCue(filePath);
            if (!string.IsNullOrWhiteSpace(dataTrack))
            {
                _serialResolver.TryFromDiscProbe(dataTrack, out serial);
            }
        }

        // 2. DAT Lookup (Identity -> Metadata)
        // If we found a serial, use it to fetch the official Title/Region from the DAT
        if (!string.IsNullOrWhiteSpace(serial) && _datMetadata.TryGetBySerial(serial, out var metadata))
        {
            title = metadata.Title;
            region = metadata.Region;
            discCount = metadata.DiscCount;
            
            foreach (var candidateSerial in metadata.Serials)
            {
                serialCandidates.Add(new PsxSerialCandidate(metadata.Title, metadata.Region, candidateSerial, metadata.DiscCount));
            }
        }

        // 3. Filename Parsing (Fallback & Supplement)
        // We always need to parse the filename to get the Disc Number (which isn't in the header)
        // and to fallback for Title/Region if the Probe/DAT failed.
        
        var match = FullNamePattern().Match(nameWithoutExt);
        if (match.Success)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                title = match.Groups[1].Value.Trim();
            }
            if (string.IsNullOrWhiteSpace(region))
            {
                var candidateRegion = match.Groups[2].Value.Trim();
                if (!VersionPattern().IsMatch($"({candidateRegion})"))
                {
                    region = candidateRegion;
                }
                else
                {
                    version = version ?? candidateRegion;
                }
            }
            if (string.IsNullOrWhiteSpace(serial))
            {
                serial = match.Groups[3].Value.Trim(); // Only if probe failed
            }
            
            if (match.Groups[4].Success)
            {
                discNumber = int.Parse(match.Groups[4].Value);
            }
            if (match.Groups[5].Success)
            {
                discCount = discCount ?? int.Parse(match.Groups[5].Value);
            }
        }
        else
        {
            match = StandardPattern().Match(nameWithoutExt);
            if (match.Success)
            {
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = match.Groups[1].Value.Trim();
                }
                if (string.IsNullOrWhiteSpace(region))
                {
                    var candidateRegion = match.Groups[2].Value.Trim();
                    if (!VersionPattern().IsMatch($"({candidateRegion})"))
                    {
                        region = candidateRegion;
                    }
                    else
                    {
                        version = version ?? candidateRegion;
                    }
                }
                if (string.IsNullOrWhiteSpace(serial))
                {
                    serial = match.Groups[3].Value.Trim();
                }
            }
            
            var discMatch = DiscPattern().Match(nameWithoutExt);
            if (discMatch.Success)
            {
                discNumber = int.Parse(discMatch.Groups[1].Value);
                if (discMatch.Groups[2].Success)
                {
                    discCount = discCount ?? int.Parse(discMatch.Groups[2].Value);
                }
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                var simpleMatch = SimplePattern().Match(nameWithoutExt);
                if (simpleMatch.Success)
                {
                    var candidateRegion = simpleMatch.Groups[2].Value.Trim();
                    // Avoid matching (Disc 1) or (Track 1) as region, or language lists (En,Fr), or pure numbers (1), or versions
                    if (!candidateRegion.StartsWith("Disc ", StringComparison.OrdinalIgnoreCase) && 
                        !candidateRegion.StartsWith("Track ", StringComparison.OrdinalIgnoreCase) &&
                        !VersionPattern().IsMatch($"({candidateRegion})") &&
                        !candidateRegion.Contains(',') &&
                        !int.TryParse(candidateRegion, out _))
                    {
                        title = simpleMatch.Groups[1].Value.Trim();
                        region = candidateRegion;
                    }
                }
            }
        }

        // 4. Last Resort Serial Extraction
        // If probe failed AND filename regex failed, try loose serial extraction from filename
        if (string.IsNullOrWhiteSpace(serial))
        {
            _serialResolver.TryFromFilename(filename, out serial);
        }

        // 4b. DAT Lookup for Filename-Derived Serial
        // If we found a serial via filename (Probe failed), we should still try to look it up
        // to get the official Title/Region/DiscCount from the DAT.
        if (!string.IsNullOrWhiteSpace(serial) && _datMetadata.TryGetBySerial(serial, out var filenameMetadata))
        {
            // Only overwrite if we don't have a title yet, or if we want to canonicalize
            if (string.IsNullOrWhiteSpace(title))
            {
                title = filenameMetadata.Title;
                region = filenameMetadata.Region;
                discCount = filenameMetadata.DiscCount;
            }
            
            foreach (var candidateSerial in filenameMetadata.Serials)
            {
                serialCandidates.Add(new PsxSerialCandidate(filenameMetadata.Title, filenameMetadata.Region, candidateSerial, filenameMetadata.DiscCount));
            }
        }

        // 5. Last Resort Title Cleanup
        if (string.IsNullOrWhiteSpace(title))
        {
            title = nameWithoutExt;
            var discMatch = DiscPattern().Match(title);
            if (discMatch.Success)
            {
                title = title[..discMatch.Index].Trim();
            }
            
            var trackMatchTemp = TrackPattern().Match(title);
            if (trackMatchTemp.Success)
            {
                title = title[..trackMatchTemp.Index].Trim();
            }

            var versionMatchTemp = VersionPattern().Match(title);
            if (versionMatchTemp.Success)
            {
                title = title[..versionMatchTemp.Index].Trim();
            }
            
            if (!string.IsNullOrWhiteSpace(serial))
            {
                title = title.Replace($"[{serial}]", "").Trim();
            }
        }

        // 6. DAT Enrichment (Title -> Metadata)
        // If we still have no serial (Probe failed, Filename failed), try to look up by Title
        if (string.IsNullOrWhiteSpace(serial) && !string.IsNullOrWhiteSpace(title))
        {
            DatTitleMetadata? titleMetadata = null;
            if (_datMetadata.TryGet(title, region, out titleMetadata) || _datMetadata.TryGet(title, null, out titleMetadata))
            {
                if (titleMetadata != null)
                {
                    serial = titleMetadata.Serials.FirstOrDefault();
                    discCount = discCount ?? titleMetadata.DiscCount;
                    
                    foreach (var candidateSerial in titleMetadata.Serials)
                    {
                        serialCandidates.Add(new PsxSerialCandidate(titleMetadata.Title, titleMetadata.Region, candidateSerial, titleMetadata.DiscCount));
                    }
                }
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
            isAudioTrack = trackNumber > 1;
            
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && extension.Equals(".bin", StringComparison.OrdinalIgnoreCase))
            {
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
            Version = version,
            DiscNumber = discNumber,
            DiscCount = discCount,
            ContentType = contentType,
            Extension = extension,
            Warning = warning,
            TrackNumber = trackNumber,
            TrackCount = trackCount,
            IsAudioTrack = isAudioTrack,
            CueFilePath = cueFilePath,
            SerialCandidates = serialCandidates
        };
    }

    private static string? TryResolveDataTrackFromCue(string cuePath)
    {
        try
        {
            var sheet = CueSheetParser.Parse(cuePath);
            var directory = Path.GetDirectoryName(cuePath) ?? string.Empty;

            foreach (var file in sheet.Files)
            {
                var firstTrack = file.Tracks.FirstOrDefault();
                if (firstTrack == null)
                {
                    continue;
                }

                var isDataTrack = firstTrack.Type.Contains("MODE", StringComparison.OrdinalIgnoreCase) ||
                                  firstTrack.Type.Contains("DATA", StringComparison.OrdinalIgnoreCase);
                if (!isDataTrack)
                {
                    continue;
                }

                var candidate = Path.Combine(directory, file.FileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // Ignore malformed CUE sheets
        }

        return null;
    }
}
