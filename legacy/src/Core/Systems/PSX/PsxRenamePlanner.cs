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
                
                // Skip audio track BINs that are loose (orphan tracks?) 
                // or maybe we should rename them if they are truly orphans?
                // For now, keep existing logic: skip if it looks like a track but isn't in a CUE
                // Only if multi-track handling is enabled (otherwise treat as regular file)
                if (handleMultiTrack && info.IsAudioTrack)
                {
                    continue;
                }

                items.Add(new DiscoveredItem { Info = info, IsCue = ext.Equals(".cue", StringComparison.OrdinalIgnoreCase) });
            }
        }
        
        // 3. Group by normalized title/region/extension to detect multi-disc sets (if enabled)
        var discAssignments = new Dictionary<string, (int DiscNumber, int DiscCount)>(StringComparer.OrdinalIgnoreCase);
        
        if (handleMultiDisc)
        {
            var multiDiscGroups = items
                .Where(i => !string.IsNullOrWhiteSpace(i.Info.Title))
                .GroupBy(i => BuildGroupKey(i.Info))
                .Where(g => g.Count() > 1 || g.Any(i => i.Info.DiscNumber.HasValue))
                .Select(g => g.OrderBy(i => i.Info.FilePath, StringComparer.OrdinalIgnoreCase).ToList())
                .ToList();

            foreach (var group in multiDiscGroups)
            {
                var count = group.Count;
                for (var i = 0; i < count; i++)
                {
                    var discNumber = group[i].Info.DiscNumber ?? (i + 1);
                    discAssignments[group[i].Info.FilePath] = (discNumber, count);
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
            
            var effectiveDiscInfo = stripLanguageTags
                ? discInfo with { Title = StripLanguageTags(discInfo.Title) }
                : discInfo;

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
                    foreach (var fileEntry in cueSheet.Files)
                    {
                        var oldBinPath = Path.Combine(directory, fileEntry.FileName);
                        if (!File.Exists(oldBinPath))
                        {
                            continue; // Skip missing files
                        }

                        string newBinName;
                        var firstTrack = fileEntry.Tracks.FirstOrDefault()?.Number ?? 1;
                        var trackInfo = effectiveDiscInfo with { TrackNumber = firstTrack, Extension = Path.GetExtension(fileEntry.FileName) };
                        
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
                    }

                    // Generate new CUE content
                    var newContent = UpdateCueContent(discInfo.FilePath, binRenames);
                    cueOp = cueOp with { NewContent = newContent };
                    
                    operations.Add(cueOp);
                    operations.AddRange(trackOps);
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
            }
        }
        
        return operations;
    }

    private PsxRenameOperation CreateOp(string source, string dest, PsxDiscInfo info, string? warning)
    {
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
        if (romRepository != null)
        {
            var cached = await romRepository.GetByPathAsync(file);
            if (cached != null && !string.IsNullOrWhiteSpace(cached.Title))
            {
                var discNumber = cached.Disc_Number;
                var discCount = cached.Disc_Count;
                
                if (!discNumber.HasValue)
                {
                    var discMatch = Regex.Match(Path.GetFileNameWithoutExtension(file), @"\(Disc (\d+)(?: of (\d+))?\)", RegexOptions.IgnoreCase);
                    if (discMatch.Success)
                    {
                        discNumber = int.Parse(discMatch.Groups[1].Value);
                        if (discMatch.Groups[2].Success)
                        {
                            discCount = int.Parse(discMatch.Groups[2].Value);
                        }
                    }
                }

                return new PsxDiscInfo
                {
                    FilePath = file,
                    Title = cached.Title,
                    Region = cached.Region,
                    Serial = cached.Serial,
                    DiscNumber = discNumber,
                    DiscCount = discCount,
                    Extension = Path.GetExtension(file),
                    Version = cached.Version,
                    ContentType = _parser.Classify(file, cached.Serial)
                };
            }
        }
        return _parser.Parse(file);
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
}
