using System.Text.RegularExpressions;
using System.Text;
using ARK.Core.Database;

namespace ARK.Core.Systems.PSX;

/// <summary>
/// Represents a PSX rename operation with diagnostic information
/// </summary>
public record PsxRenameOperation
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required PsxDiscInfo DiscInfo { get; init; }
    public bool IsAlreadyNamed { get; init; }
    public string? Warning { get; init; }
    public string? NewContent { get; init; }
}

/// <summary>
/// Plans rename operations for PSX files
/// </summary>
public class PsxRenamePlanner
{
    private readonly PsxNameParser _parser;
    
    public PsxRenamePlanner(PsxNameParser? parser = null)
    {
        _parser = parser ?? new PsxNameParser();
    }
    
    /// <summary>
    /// Plan rename operations for PSX files in a directory
    /// </summary>
    private static readonly Regex TrailingParenthetical = new(@"\s*\((?<inner>[^()]+)\)\s*$", RegexOptions.Compiled);
    private static readonly HashSet<string> LanguageTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "EN","ES","JA","DE","FR","IT","PT","PTBR","BR","NL","NO","SV","SE","FI","DA",
        "ZH","KO","RU","PL","CS","HU","AR","TR","EL","HE","ID","KO","MS","TH","VI",
        "ESLA","LA","ESMX","BG","HR","SL","SK","RO"
    };

    public async Task<List<PsxRenameOperation>> PlanRenamesAsync(
        string rootPath, 
        bool recursive = false, 
        bool restoreArticles = false, 
        bool stripLanguageTags = true,
        bool includeVersion = false,
        bool handleMultiDisc = true,
        bool handleMultiTrack = true,
        RomRepository? romRepository = null)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var psxExtensions = new[] { ".bin", ".cue", ".chd", ".pbp", ".iso" };
        
        var items = new List<DiscoveredItem>();
        var handledFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var effectiveInfos = new Dictionary<string, PsxDiscInfo>(StringComparer.OrdinalIgnoreCase);
        var plannedDestinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan CUE files first to identify sets (if enabled)
        if (handleMultiTrack)
        {
            var cueFiles = Directory.GetFiles(rootPath, "*.cue", searchOption);
            foreach (var cueFile in cueFiles)
            {
                var info = await GetDiscInfoAsync(cueFile, romRepository);
                var related = new List<string>();
                
                try 
                {
                    var cueSheet = CueSheetParser.Parse(cueFile);
                    var dir = Path.GetDirectoryName(cueFile) ?? string.Empty;
                    foreach (var file in cueSheet.Files)
                    {
                        var binPath = Path.Combine(dir, file.FileName);
                        related.Add(binPath);
                        handledFiles.Add(Path.GetFullPath(binPath));
                    }
                }
                catch 
                {
                    // If CUE parse fails, treat as standalone
                }

                items.Add(new DiscoveredItem { Info = info, RelatedFiles = related, IsCue = true });
            }
        }

        // 2. Scan other files
        foreach (var ext in psxExtensions)
        {
            if (ext.Equals(".cue", StringComparison.OrdinalIgnoreCase) && handleMultiTrack)
            {
                continue; // Already handled
            }

            var files = Directory.GetFiles(rootPath, $"*{ext}", searchOption);
            foreach (var file in files)
            {
                if (handledFiles.Contains(Path.GetFullPath(file)))
                {
                    continue;
                }

                var info = await GetDiscInfoAsync(file, romRepository);
                
                items.Add(new DiscoveredItem { Info = info, IsCue = ext.Equals(".cue", StringComparison.OrdinalIgnoreCase) });
            }
        }

        // Demote numbered single-disc files (often multi-track) to track sets to avoid false multi-disc grouping
        DemoteSingleDiscNumbersToTracks(items);
        ClearFalseTrackMarkers(items);
        
        // 3. Group by normalized title/region/extension to detect multi-disc sets (if enabled)
        var discAssignments = new Dictionary<string, (int DiscNumber, int DiscCount)>(StringComparer.OrdinalIgnoreCase);

        if (handleMultiDisc)
        {
            // Only use disc-level candidates (track 1 or non-track files) when inferring disc counts
            var multiDiscGroups = items
                .Where(i => !string.IsNullOrWhiteSpace(i.Info.Title))
                .Where(i => (i.Info.TrackNumber is null or 1) && i.Info.TrackCount.GetValueOrDefault() <= 1 && !i.Info.IsAudioTrack)
                .GroupBy(i => BuildGroupKey(i.Info))
                .Select(g => g.OrderBy(i => i.Info.FilePath, StringComparer.OrdinalIgnoreCase).ToList())
                .ToList();

            foreach (var group in multiDiscGroups)
            {
                var serials = group.Select(x => x.Info.Serial).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var maxDiscCount = group.Max(x => x.Info.DiscCount ?? 0);
                var inferredDiscNumbers = group.Where(g => g.Info.DiscNumber.HasValue).Select(g => g.Info.DiscNumber!.Value).Distinct().ToList();

                // If metadata says single-disc AND serials match AND no real multi-disc metadata, treat as track set
                if (maxDiscCount <= 1 && serials.Count <= 1 && inferredDiscNumbers.Count <= 1 && group.All(g => (g.Info.DiscCount ?? 0) <= 1))
                {
                    continue;
                }

                if (group.Count <= 1 && group.All(g => !g.Info.DiscNumber.HasValue))
                {
                    continue;
                }

                var count = group.Count;

                // If serials are distinct but disc numbers are missing/duplicated, assign disc numbers deterministically by serial
                var hasSerialConflict = serials.Count > 1 && inferredDiscNumbers.Count <= 1;
                if (hasSerialConflict)
                {
                    var ordered = group
                        .OrderBy(g => g.Info.Serial ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(g => g.Info.FilePath, StringComparer.OrdinalIgnoreCase)
                        .Select((g, idx) => (g, Disc: idx + 1))
                        .ToList();

                    foreach (var entry in ordered)
                    {
                        discAssignments[entry.g.Info.FilePath] = (entry.Disc, count);
                    }
                }
                else
                {
                    for (var i = 0; i < count; i++)
                    {
                        var discNumber = group[i].Info.DiscNumber ?? (i + 1);
                        discAssignments[group[i].Info.FilePath] = (discNumber, count);
                    }
                }
            }
        }
        
        // 4. Create operations
        var operations = new List<PsxRenameOperation>();
        
        foreach (var item in items)
        {
            var discInfo = item.Info;
            if (discAssignments.TryGetValue(discInfo.FilePath, out var assignment))
            {
                discInfo = discInfo with
                {
                    DiscNumber = discInfo.DiscNumber ?? assignment.DiscNumber,
                    DiscCount = discInfo.DiscCount ?? assignment.DiscCount
                };
            }

            // If this is a known multi-disc title, treat "Track 01" tokens as the data disc (no track suffix)
            if (discInfo.TrackNumber == 1 && discInfo.DiscCount.GetValueOrDefault() > 1)
            {
                discInfo = discInfo with { TrackNumber = null, TrackCount = null, IsAudioTrack = false };
            }
            
            var effectiveDiscInfo = stripLanguageTags
                ? discInfo with { Title = StripLanguageTags(discInfo.Title) }
                : discInfo;
            effectiveInfos[discInfo.FilePath] = effectiveDiscInfo;

            // Calculate canonical base name (without extension)
            var baseName = PsxNameFormatter.Format(effectiveDiscInfo with { Extension = null }, restoreArticles, includeVersion);
            var directory = Path.GetDirectoryName(discInfo.FilePath) ?? string.Empty;

            if (item.IsCue)
            {
                // Plan CUE rename
                var destCue = Path.Combine(directory, baseName + ".cue");
                var cueOp = CreateOp(discInfo.FilePath, destCue, effectiveDiscInfo, discInfo.Warning);
                
                // Plan BIN renames and CUE content update
                var binRenames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var trackOps = new List<PsxRenameOperation>();
                
                // We need to know the track structure to name BINs correctly
                // Re-parse CUE to be safe
                try 
                {
                    var cueSheet = CueSheetParser.Parse(discInfo.FilePath);
                    var trackCount = cueSheet.Files.Count;
                    foreach (var fileEntry in cueSheet.Files)
                    {
                        var oldBinPath = Path.Combine(directory, fileEntry.FileName);
                        if (!File.Exists(oldBinPath))
                        {
                            continue; // Skip missing files
                        }

                        string newBinName;
                        var firstTrack = fileEntry.Tracks.FirstOrDefault()?.Number ?? 1;
                        var trackInfo = effectiveDiscInfo with 
                        { 
                            TrackNumber = trackCount > 1 ? firstTrack : (int?)null, 
                            TrackCount = trackCount > 1 ? trackCount : null,
                            Extension = Path.GetExtension(fileEntry.FileName) 
                        };
                        
                        if (cueSheet.Files.Count == 1)
                        {
                            // Single file CUE: Use base name (no track suffix unless it's actually a track file?)
                            // Actually, if it's a single BIN, we usually don't want (Track 01).
                            // But PsxNameFormatter WILL add (Track 01) if TrackNumber is set.
                            // So we should set TrackNumber to null for single-file layout?
                            // But wait, if we have Game.cue and Game.bin, we want Game.bin.
                            // If we have Game.cue and Game (Track 1).bin, we want Game (Track 1).bin?
                            // Standard Redump is Game (Track 1).bin usually for multi-track.
                            // For single track, it's Game.bin.
                            
                            trackInfo = trackInfo with { TrackNumber = null };
                        }
                        
                        newBinName = PsxNameFormatter.Format(trackInfo, restoreArticles, includeVersion);

                        var destBinPath = Path.Combine(directory, newBinName);
                        binRenames[fileEntry.FileName] = newBinName;
                        
                        trackOps.Add(CreateOp(oldBinPath, destBinPath, trackInfo, null));
                        plannedDestinations[oldBinPath] = destBinPath;
                    }

                    // Generate new CUE content
                    var newContent = UpdateCueContent(discInfo.FilePath, binRenames);
                    cueOp = cueOp with { NewContent = newContent };
                    
                    operations.Add(cueOp);
                    operations.AddRange(trackOps);
                    plannedDestinations[discInfo.FilePath] = destCue;
                }
                catch
                {
                    // Fallback if CUE parse fails: just rename CUE, ignore BINs
                    operations.Add(cueOp);
                }
            }
            else
            {
                // Plan single file rename
                var destPath = Path.Combine(directory, baseName + discInfo.Extension);
                operations.Add(CreateOp(discInfo.FilePath, destPath, effectiveDiscInfo, discInfo.Warning));
                plannedDestinations[discInfo.FilePath] = destPath;
            }
        }

        // 5. Create minimal CUE files for detected multi-track BIN sets that lack cues
        if (handleMultiTrack)
        {
            var groupedTracks = items
                .Where(i => !i.IsCue && effectiveInfos.TryGetValue(i.Info.FilePath, out var info) && info.TrackNumber.HasValue)
                .GroupBy(i => BuildTrackGroupKey(effectiveInfos[i.Info.FilePath], stripLanguageTags))
                .Where(g => g.Count() > 1);

            foreach (var group in groupedTracks)
            {
                var sampleInfo = effectiveInfos[group.First().Info.FilePath];
                var directory = Path.GetDirectoryName(sampleInfo.FilePath) ?? string.Empty;

                // Skip if a CUE already exists in this directory (leave it alone)
                if (Directory.GetFiles(directory, "*.cue").Any())
                {
                    continue;
                }

                var cueInfo = sampleInfo with
                {
                    TrackNumber = null,
                    TrackCount = group.Count(),
                    Extension = ".cue",
                    FilePath = Path.Combine(directory, "placeholder.cue") // temp path to satisfy record
                };

                var cueBaseName = PsxNameFormatter.Format(cueInfo with { Extension = null, DiscNumber = sampleInfo.DiscNumber }, restoreArticles, includeVersion);
                var cuePath = Path.Combine(directory, cueBaseName + ".cue");

                if (File.Exists(cuePath))
                {
                    continue;
                }

                var trackEntries = group
                    .Select(item =>
                    {
                        var info = effectiveInfos[item.Info.FilePath];
                        var dest = plannedDestinations.TryGetValue(info.FilePath, out var mapped) ? mapped : info.FilePath;
                        return (info, dest);
                    })
                    .OrderBy(x => x.info.TrackNumber ?? int.MaxValue)
                    .ThenBy(x => x.dest, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var cueContent = BuildCueContent(trackEntries);

                operations.Add(new PsxRenameOperation
                {
                    SourcePath = cuePath,
                    DestinationPath = cuePath,
                    DiscInfo = cueInfo with { FilePath = cuePath },
                    IsAlreadyNamed = false,
                    NewContent = cueContent
                });
            }
        }
        
        return operations;
    }

    private PsxRenameOperation CreateOp(string source, string dest, PsxDiscInfo info, string? warning)
    {
        dest = SanitizeFormattedPath(dest);
        var isAlreadyNamed = string.Equals(Path.GetFileName(source), Path.GetFileName(dest), StringComparison.OrdinalIgnoreCase); // Case-insensitive check for Windows
        
        // Check for conflicts (unless renaming case only)
        if (File.Exists(dest) && !string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
        {
            warning = warning != null ? $"{warning}; Destination file already exists" : "Destination file already exists";
        }

        return new PsxRenameOperation
        {
            SourcePath = source,
            DestinationPath = dest,
            DiscInfo = info,
            IsAlreadyNamed = isAlreadyNamed,
            Warning = warning
        };
    }

    private async Task<PsxDiscInfo> GetDiscInfoAsync(string file, RomRepository? romRepository)
    {
        // Always run through the parser so BIN/CHD -> DAT -> filename precedence and warning plumbing stay consistent,
        // then hydrate any missing metadata from the cache to avoid losing disc counts from previous scans.
        var parsed = _parser.Parse(file);

        if (romRepository == null)
        {
            return parsed;
        }

        var cached = await romRepository.GetByPathAsync(file);
        if (cached == null)
        {
            return parsed;
        }

        var discNumber = parsed.DiscNumber ?? cached.Disc_Number;
        var discCount = parsed.DiscCount ?? cached.Disc_Count;

        if (!discNumber.HasValue)
        {
            var discMatch = Regex.Match(Path.GetFileNameWithoutExtension(file), @"\(Disc (\d+)(?: of (\d+))?\)", RegexOptions.IgnoreCase);
            if (discMatch.Success)
            {
                discNumber = int.Parse(discMatch.Groups[1].Value);
                if (discMatch.Groups[2].Success)
                {
                    discCount = discCount ?? int.Parse(discMatch.Groups[2].Value);
                }
            }
        }

        return parsed with
        {
            Title = parsed.Title ?? cached.Title,
            Region = parsed.Region ?? cached.Region,
            Serial = parsed.Serial ?? cached.Serial,
            Version = parsed.Version ?? cached.Version,
            DiscNumber = discNumber,
            DiscCount = discCount
        };
    }

    private string UpdateCueContent(string cuePath, Dictionary<string, string> binRenames)
    {
        var lines = File.ReadAllLines(cuePath);
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, "FILE \"(.+?)\"");
                if (match.Success)
                {
                    var original = match.Groups[1].Value;
                    if (binRenames.TryGetValue(original, out var newName))
                    {
                        sb.AppendLine(line.Replace($"\"{original}\"", $"\"{newName}\""));
                        continue;
                    }
                }
            }
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    private class DiscoveredItem
    {
        public required PsxDiscInfo Info { get; init; }
        public List<string> RelatedFiles { get; init; } = new();
        public bool IsCue { get; init; }
    }

    private static void DemoteSingleDiscNumbersToTracks(List<DiscoveredItem> items)
    {
        var indexed = items
            .Select((item, idx) => (item, idx))
            .Where(x => x.item.Info.DiscNumber.HasValue && (x.item.Info.DiscCount ?? 1) <= 1 && !string.IsNullOrWhiteSpace(x.item.Info.Title))
            .GroupBy(x => (
                Title: CleanTitleForGrouping(x.item.Info.Title ?? string.Empty),
                Region: x.item.Info.Region ?? string.Empty,
                Serial: x.item.Info.Serial ?? string.Empty),
                new GroupKeyComparer())
            .Where(g => g.Count() > 1);

        foreach (var group in indexed)
        {
            var distinctDiscNumbers = group.Select(g => g.item.Info.DiscNumber!.Value).Distinct().ToList();
            if (distinctDiscNumbers.Count > 1)
            {
                // Disc numbers disagree; treat as true multi-disc instead of demoting to tracks.
                continue;
            }

            var ordered = group.OrderBy(g => g.item.Info.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
            var trackCount = ordered.Count;
            for (var i = 0; i < trackCount; i++)
            {
                var entry = ordered[i];
                var info = entry.item.Info with
                {
                    DiscNumber = null,
                    TrackNumber = i + 1,
                    TrackCount = trackCount,
                    IsAudioTrack = i > 0,
                    DiscCount = 1
                };

                items[entry.idx] = new DiscoveredItem
                {
                    Info = info,
                    RelatedFiles = entry.item.RelatedFiles,
                    IsCue = entry.item.IsCue
                };
            }
        }
    }

    private static string CleanTitleForGrouping(string title)
        => title.Trim().ToUpperInvariant();

    private sealed class GroupKeyComparer : IEqualityComparer<(string Title, string Region, string Serial)>
    {
        public bool Equals((string Title, string Region, string Serial) x, (string Title, string Region, string Serial) y) =>
            string.Equals(x.Title, y.Title, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Region, y.Region, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Serial, y.Serial, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Title, string Region, string Serial) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Title),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Region),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Serial));
    }

    private static string BuildGroupKey(PsxDiscInfo disc)
    {
        var title = disc.Title?.Trim() ?? string.Empty;
        var region = disc.Region?.Trim() ?? string.Empty;
        var extension = disc.Extension?.Trim() ?? string.Empty;
        return string.Join('|',
            title.ToUpperInvariant(),
            region.ToUpperInvariant(),
            extension.ToUpperInvariant());
    }

    private static string? StripLanguageTags(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        var sanitized = title.TrimEnd();
        while (true)
        {
            var match = TrailingParenthetical.Match(sanitized);
            if (!match.Success)
            {
                break;
            }

            var inner = match.Groups["inner"].Value;
            if (!LooksLikeLanguageList(inner))
            {
                break;
            }

            sanitized = sanitized[..match.Index].TrimEnd();
        }

        return sanitized;
    }

    private static bool LooksLikeLanguageList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = value.Split(new[] { ',', '/', '|', '&' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        return tokens.All(token =>
        {
            var normalized = token.Trim().Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
            return LanguageTokens.Contains(normalized);
        });
    }

    private static string BuildTrackGroupKey(PsxDiscInfo info, bool stripLanguageTags)
    {
        var cleanTitle = stripLanguageTags ? StripLanguageTags(info.Title) : info.Title;
        return string.Join('|',
            (Path.GetDirectoryName(info.FilePath) ?? string.Empty).ToUpperInvariant(),
            (cleanTitle ?? string.Empty).Trim().ToUpperInvariant(),
            (info.Region ?? string.Empty).Trim().ToUpperInvariant(),
            (info.Serial ?? string.Empty).Trim().ToUpperInvariant(),
            (info.DiscNumber ?? 0).ToString());
    }

    private static void ClearFalseTrackMarkers(List<DiscoveredItem> items)
    {
        var indexed = items.Select((item, idx) => (item, idx))
            .Where(x => !x.item.IsCue && x.item.Info.TrackNumber.HasValue)
            .ToList();

        var groups = indexed
            .GroupBy(x => (
                Title: x.item.Info.Title?.Trim().ToUpperInvariant() ?? string.Empty,
                Region: x.item.Info.Region?.Trim().ToUpperInvariant() ?? string.Empty,
                Serial: x.item.Info.Serial?.Trim().ToUpperInvariant() ?? string.Empty,
                Disc: x.item.Info.DiscNumber ?? 1));

        foreach (var group in groups)
        {
            var nonCueCount = group.Count();
            if (nonCueCount == 1)
            {
                // Only one BIN/IMG for this disc/title/region/serial: treat as single-track data disc
                var entry = group.First();
                var cleaned = entry.item.Info with { TrackNumber = null, TrackCount = null, IsAudioTrack = false };
                items[entry.idx] = new DiscoveredItem
                {
                    Info = cleaned,
                    RelatedFiles = entry.item.RelatedFiles,
                    IsCue = entry.item.IsCue
                };
                continue;
            }

            // Multiple files for same disc/serial: pick the largest as data track and clear its track markers
            var orderedBySize = group
                .Select(g =>
                {
                    long size = 0;
                    try { size = new FileInfo(g.item.Info.FilePath).Length; } catch { }
                    return (g, size);
                })
                .OrderByDescending(x => x.size)
                .ThenBy(x => x.g.item.Info.TrackNumber ?? int.MaxValue)
                .ToList();

            if (orderedBySize.Count > 0)
            {
                var dataEntry = orderedBySize.First().g;
                var cleaned = dataEntry.item.Info with { TrackNumber = null, TrackCount = null, IsAudioTrack = false };
                items[dataEntry.idx] = new DiscoveredItem
                {
                    Info = cleaned,
                    RelatedFiles = dataEntry.item.RelatedFiles,
                    IsCue = dataEntry.item.IsCue
                };
            }
        }
    }

    private static string BuildCueContent(IEnumerable<(PsxDiscInfo info, string dest)> tracks)
    {
        var sb = new StringBuilder();
        foreach (var entry in tracks)
        {
            var trackNumber = entry.info.TrackNumber ?? 1;
            var trackType = trackNumber == 1 ? "MODE2/2352" : "AUDIO";
            sb.AppendLine($"FILE \"{Path.GetFileName(entry.dest)}\" BINARY");
            sb.AppendLine($"  TRACK {trackNumber:D2} {trackType}");
            sb.AppendLine("    INDEX 01 00:00:00");
        }
        return sb.ToString();
    }

    private static string SanitizeFormattedPath(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileName(path);
        var ext = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);

        // Normalize DISC -> Disc and collapse duplicates
        stem = Regex.Replace(stem, @"\((?:DISC|Disc)\s*(\d+)\)", "(Disc $1)", RegexOptions.IgnoreCase);
        stem = Regex.Replace(stem, @"\s*\(Disc (\d+)\)\s*\(Disc \1\)", " (Disc $1)", RegexOptions.IgnoreCase);

        // Normalize TRACK -> Track and collapse duplicates
        stem = Regex.Replace(stem, @"\((?:TRACK|Track)\s*(\d+)\)", "(Track $1)", RegexOptions.IgnoreCase);
        stem = Regex.Replace(stem, @"\s*\(Track (\d+)\)\s*\(Track \1\)", " (Track $1)", RegexOptions.IgnoreCase);

        // Trim extra whitespace
        stem = Regex.Replace(stem, @"\s{2,}", " ").Trim();

        return Path.Combine(directory, stem + ext);
    }
}
