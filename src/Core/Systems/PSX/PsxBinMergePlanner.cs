using System.Text;

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
    public bool AlreadyMerged { get; init; }
    public string? Warning { get; init; }
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
    public List<PsxBinMergeOperation> PlanMerges(string rootPath, bool recursive = false)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var cueFiles = Directory.GetFiles(rootPath, "*.cue", searchOption);
        var operations = new List<PsxBinMergeOperation>();

        foreach (var cueFile in cueFiles)
        {
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
            var discInfo = _parser.Parse(cueFile);
            var mergeBaseName = BuildMergeBaseName(cueFile);

            var destinationBinPath = Path.Combine(directory, mergeBaseName + ".bin");
            var destinationCuePath = Path.Combine(directory, mergeBaseName + ".cue");

            var trackSources = cueSheet.Files.Select((fileEntry, index) =>
            {
                var cueFilePath = Path.Combine(directory, fileEntry.FileName);
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
            if (missingTracks.Count > 0)
            {
                operations.Add(new PsxBinMergeOperation
                {
                    CuePath = cueFile,
                    TrackSources = trackSources,
                    DestinationBinPath = destinationBinPath,
                    DestinationCuePath = destinationCuePath,
                    Title = Path.GetFileNameWithoutExtension(cueFile),
                    AlreadyMerged = false,
                    Warning = $"Missing track BIN(s): {string.Join(", ", missingTracks.Select(t => Path.GetFileName(t.AbsolutePath)))}"
                });
                continue;
            }

            var alreadyMerged = File.Exists(destinationBinPath) && trackSources.Count == 1;

            operations.Add(new PsxBinMergeOperation
            {
                CuePath = cueFile,
                TrackSources = trackSources,
                DestinationBinPath = destinationBinPath,
                DestinationCuePath = destinationCuePath,
                Title = discInfo.Title ?? Path.GetFileNameWithoutExtension(cueFile),
                AlreadyMerged = alreadyMerged,
                Warning = discInfo.Warning
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
}
