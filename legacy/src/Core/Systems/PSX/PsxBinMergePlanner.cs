using System.Text;
using System.Text.RegularExpressions;
using ARK.Core.Database;

namespace ARK.Core.Systems.PSX;

/// <summary>
/// Represents a merge operation for PSX multi-track BIN sets.
/// </summary>
public record PsxBinMergeOperation
{
    public required string CuePath { get; init; }
    public required IReadOnlyList<PsxBinTrackSource> TrackSources { get; init; }
    public required string DestinationBinPath { get; init; }
    public required string DestinationCuePath { get; init; }
    public required string Title { get; init; }
    public required PsxDiscInfo DiscInfo { get; init; }
    public bool AlreadyMerged { get; init; }
    public bool IsBlocked { get; init; }
    public string? BlockReason { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Represents metadata for each BIN segment referenced by a CUE sheet.
/// </summary>
public record PsxBinTrackSource
{
    public required int Sequence { get; init; }
    public required int TrackNumber { get; init; }
    public required string TrackType { get; init; }
    public required string CueFileName { get; init; }
    public required string AbsolutePath { get; init; }
}

/// <summary>
/// Plans merge operations for PSX multi-BIN layouts.
/// </summary>
    public class PsxBinMergePlanner
{
    private readonly PsxNameParser _parser;

    public PsxBinMergePlanner(PsxNameParser? parser = null)
    {
        _parser = parser ?? new PsxNameParser();
    }

    /// <summary>
    /// Plan multi-track BIN merges starting at the specified root.
    /// </summary>
    /// <param name="rootPath">Root directory to scan for CUE files.</param>
    /// <param name="recursive">Whether to search recursively in subdirectories.</param>
    /// <param name="outputDirectory">Optional directory where merged files should be placed. If null, outputs to same directory as source.</param>
    /// <param name="onProgress">Optional callback to report progress (current file being processed).</param>
    /// <param name="romRepository">Optional repository to fetch cached metadata.</param>
    /// <param name="flatten">Whether to flatten the output to the root directory (or outputDirectory) using canonical naming.</param>
    public async Task<List<PsxBinMergeOperation>> PlanMergesAsync(
        string rootPath, 
        bool recursive = false, 
        string? outputDirectory = null, 
        Action<string>? onProgress = null,
        RomRepository? romRepository = null,
        bool flatten = false)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var cueFiles = Directory.GetFiles(rootPath, "*.cue", searchOption);
        var operations = new List<PsxBinMergeOperation>();

        foreach (var cueFile in cueFiles)
        {
            onProgress?.Invoke(cueFile);
            var cueSheet = CueSheetParser.Parse(cueFile);
            if (cueSheet.Files.Count <= 1)
            {
                continue;
            }

            if (cueSheet.Files.Any(f => f.Tracks.Count != 1))
            {
                // Unsupported scenario - we only handle files with a single track each.
                continue;
            }

            var directory = Path.GetDirectoryName(cueFile) ?? string.Empty;
            
            PsxDiscInfo discInfo;
            if (romRepository != null)
            {
                var cached = await romRepository.GetByPathAsync(cueFile);
                if (cached != null && !string.IsNullOrWhiteSpace(cached.Title))
                {
                    discInfo = new PsxDiscInfo
                    {
                        FilePath = cueFile,
                        Title = cached.Title,
                        Region = cached.Region,
                        Serial = cached.Serial,
                        DiscNumber = cached.Disc_Number,
                        DiscCount = cached.Disc_Count,
                        Extension = ".cue"
                    };
                }
                else
                {
                    discInfo = _parser.Parse(cueFile);
                }
            }
            else
            {
                discInfo = _parser.Parse(cueFile);
            }

            var mergeBaseName = BuildMergeBaseName(cueFile);
            var outputDir = outputDirectory ?? directory;

            if (flatten)
            {
                mergeBaseName = BuildSmartMergeName(discInfo);
                outputDir = outputDirectory ?? rootPath;
            }

            var destinationBinPath = Path.Combine(outputDir, mergeBaseName + ".bin");
            var destinationCuePath = Path.Combine(outputDir, mergeBaseName + ".cue");

            var trackSources = cueSheet.Files.Select((fileEntry, index) =>
            {
                var cueFilePath = Path.Combine(directory, fileEntry.FileName);
                
                // Try to resolve the file if it doesn't exist exactly as specified
                if (!File.Exists(cueFilePath))
                {
                    var resolvedPath = ResolveTrackPath(directory, fileEntry.FileName);
                    if (resolvedPath != null)
                    {
                        cueFilePath = resolvedPath;
                    }
                }

                return new PsxBinTrackSource
                {
                    Sequence = index,
                    TrackNumber = fileEntry.Tracks[0].Number,
                    TrackType = fileEntry.Tracks[0].Type,
                    CueFileName = fileEntry.FileName,
                    AbsolutePath = cueFilePath
                };
            }).ToList();

            var missingTracks = trackSources.Where(t => !File.Exists(t.AbsolutePath)).ToList();
            var notes = new List<string>();
            var blocked = false;
            string? blockReason = null;

            if (missingTracks.Count > 0)
            {
                blocked = true;
                blockReason = $"Missing track BIN(s): {string.Join(", ", missingTracks.Select(t => Path.GetFileName(t.AbsolutePath)))}";
            }

            var alreadyMerged = File.Exists(destinationBinPath) && trackSources.Count == 1;

            if (discInfo.DiscCount.HasValue && discInfo.DiscCount.Value > 1)
            {
                notes.Add($"Multi-disc set (Disc {discInfo.DiscNumber ?? 1}/{discInfo.DiscCount})");
            }

            if (alreadyMerged)
            {
                notes.Add("Merged BIN already present");
            }

            // User requested to remove serial warnings from merge op as it's not relevant to the merge process itself
            // if (!string.IsNullOrWhiteSpace(discInfo.Warning))
            // {
            //     notes.Add(discInfo.Warning);
            // }

            operations.Add(new PsxBinMergeOperation
            {
                CuePath = cueFile,
                TrackSources = trackSources,
                DestinationBinPath = destinationBinPath,
                DestinationCuePath = destinationCuePath,
                Title = discInfo.Title ?? Path.GetFileNameWithoutExtension(cueFile),
                AlreadyMerged = alreadyMerged,
                DiscInfo = discInfo,
                IsBlocked = blocked,
                BlockReason = blockReason,
                Notes = notes
            });
        }

        return operations;
    }

    private static string BuildMergeBaseName(string cuePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(cuePath);
        var sanitized = Sanitize(fileName);
        return string.IsNullOrWhiteSpace(sanitized) ? "merged" : sanitized;
    }

    private static string BuildSmartMergeName(PsxDiscInfo info)
    {
        var sb = new StringBuilder();
        sb.Append(info.Title ?? "Unknown");
        
        if (!string.IsNullOrWhiteSpace(info.Region))
        {
            sb.Append($" ({info.Region})");
        }
        
        if (info.DiscNumber.HasValue && (info.DiscCount > 1 || info.DiscNumber > 1))
        {
            sb.Append($" (Disc {info.DiscNumber})");
        }
        
        return Sanitize(sb.ToString());
    }

    private static string Sanitize(string input)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }
        return builder.ToString().Trim();
    }

    private static string? ResolveTrackPath(string directory, string fileName)
    {
        // 1. Check if file exists exactly (already done by caller, but safe to repeat)
        var exactPath = Path.Combine(directory, fileName);
        if (File.Exists(exactPath))
        {
            return exactPath;
        }

        // 2. Try to handle (Track 1) vs (Track 01) mismatch
        // Pattern: Look for " (Track X)" or " (Track XX)"
        var match = Regex.Match(fileName, @"\(Track (\d+)\)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var trackNumStr = match.Groups[1].Value;
            if (int.TryParse(trackNumStr, out var trackNum))
            {
                // If we have "1", try "01". If we have "01", try "1".
                var altNumStr = trackNumStr.Length == 1 ? trackNum.ToString("D2") : trackNum.ToString("D1");
                var altFileName = fileName.Replace(match.Value, $"(Track {altNumStr})", StringComparison.OrdinalIgnoreCase);
                var altPath = Path.Combine(directory, altFileName);
                
                if (File.Exists(altPath))
                {
                    return altPath;
                }
            }
        }

        return null;
    }
}
