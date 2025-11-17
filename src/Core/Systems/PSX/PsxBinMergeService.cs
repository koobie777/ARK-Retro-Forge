using System.Text;

namespace ARK.Core.Systems.PSX;

/// <summary>
/// Executes BIN merge plans and rewrites CUE sheets.
/// </summary>
public class PsxBinMergeService
{
    private const int BytesPerSector = 2352;

    public async Task MergeAsync(PsxBinMergeOperation operation, bool deleteSources, CancellationToken cancellationToken = default)
    {
        if (operation.TrackSources.Count <= 1)
        {
            return;
        }

        var destinationDirectory = Path.GetDirectoryName(operation.DestinationBinPath)
            ?? Path.GetDirectoryName(operation.CuePath)
            ?? string.Empty;
        IReadOnlyDictionary<string, long>? trackLengths = null;

        if (deleteSources)
        {
            trackLengths = operation.TrackSources.ToDictionary(
                t => t.AbsolutePath,
                t => new FileInfo(t.AbsolutePath).Length,
                StringComparer.OrdinalIgnoreCase);
        }

        Directory.CreateDirectory(destinationDirectory);

        var tempOutput = operation.DestinationBinPath + ".tmp";
        if (File.Exists(tempOutput))
        {
            File.Delete(tempOutput);
        }

        await using (var output = File.Open(tempOutput, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var trackSource in operation.TrackSources.OrderBy(t => t.Sequence))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using (var input = File.OpenRead(trackSource.AbsolutePath))
                {
                    await input.CopyToAsync(output, cancellationToken);
                }

                if (deleteSources && DeleteFileIfExists(trackSource.AbsolutePath))
                {
                    PruneEmptyParentDirectories(trackSource.AbsolutePath, destinationDirectory);
                }
            }
        }

        File.Move(tempOutput, operation.DestinationBinPath, overwrite: true);

        var cueContents = BuildCueContents(operation, trackLengths);
        await File.WriteAllTextAsync(operation.DestinationCuePath, cueContents, Encoding.UTF8, cancellationToken);

        if (deleteSources &&
            !string.Equals(operation.CuePath, operation.DestinationCuePath, StringComparison.OrdinalIgnoreCase) &&
            DeleteFileIfExists(operation.CuePath))
        {
            PruneEmptyParentDirectories(operation.CuePath, destinationDirectory);
        }
    }

    private static string BuildCueContents(PsxBinMergeOperation operation, IReadOnlyDictionary<string, long>? trackLengths)
    {
        var builder = new StringBuilder();
        var binFileName = Path.GetFileName(operation.DestinationBinPath);
        builder.AppendLine($@"FILE ""{binFileName}"" BINARY");

        long cumulativeFrames = 0;

        foreach (var trackSource in operation.TrackSources.OrderBy(t => t.Sequence))
        {
            var bytes = trackLengths != null && trackLengths.TryGetValue(trackSource.AbsolutePath, out var length)
                ? length
                : new FileInfo(trackSource.AbsolutePath).Length;
            var bytesPerFrame = GetBytesPerFrame(trackSource.TrackType);
            var frames = bytes / bytesPerFrame;

            builder.AppendLine($"  TRACK {trackSource.TrackNumber:D2} {trackSource.TrackType}");
            builder.AppendLine($"    INDEX 01 {FormatFrames(cumulativeFrames)}");

            cumulativeFrames += frames;
        }

        return builder.ToString();
    }

    private static int GetBytesPerFrame(string trackType)
    {
        if (trackType.Contains("2048", StringComparison.OrdinalIgnoreCase))
        {
            return 2048;
        }

        if (trackType.Contains("2336", StringComparison.OrdinalIgnoreCase))
        {
            return 2336;
        }

        return BytesPerSector;
    }

    private static string FormatFrames(long frames)
    {
        var minutes = frames / 4500;
        var remainder = frames % 4500;
        var seconds = remainder / 75;
        var frame = remainder % 75;
        return $"{minutes:D2}:{seconds:D2}:{frame:D2}";
    }

    private static bool DeleteFileIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private static void PruneEmptyParentDirectories(string deletedPath, string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(deletedPath) || string.IsNullOrWhiteSpace(rootDirectory))
        {
            return;
        }

        var rootFullPath = Path.GetFullPath(rootDirectory);
        var current = Path.GetDirectoryName(deletedPath);

        while (!string.IsNullOrWhiteSpace(current))
        {
            var currentFullPath = Path.GetFullPath(current);
            var relative = Path.GetRelativePath(rootFullPath, currentFullPath);

            if (relative is "." || relative.StartsWith("..", StringComparison.Ordinal))
            {
                break;
            }

            if (!Directory.Exists(currentFullPath))
            {
                current = Path.GetDirectoryName(currentFullPath);
                continue;
            }

            if (Directory.EnumerateFileSystemEntries(currentFullPath).Any())
            {
                break;
            }

            try
            {
                Directory.Delete(currentFullPath);
            }
            catch
            {
                break;
            }

            current = Path.GetDirectoryName(currentFullPath);
        }
    }
}
