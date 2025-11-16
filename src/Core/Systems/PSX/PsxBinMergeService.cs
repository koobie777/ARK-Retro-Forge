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

        Directory.CreateDirectory(Path.GetDirectoryName(operation.DestinationBinPath)!);

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
                await using var input = File.OpenRead(trackSource.AbsolutePath);
                await input.CopyToAsync(output, cancellationToken);
            }
        }

        if (File.Exists(operation.DestinationBinPath))
        {
            File.Delete(operation.DestinationBinPath);
        }

        File.Move(tempOutput, operation.DestinationBinPath);

        var cueContents = BuildCueContents(operation);
        await File.WriteAllTextAsync(operation.DestinationCuePath, cueContents, Encoding.UTF8, cancellationToken);

        if (deleteSources)
        {
            foreach (var trackSource in operation.TrackSources)
            {
                if (File.Exists(trackSource.AbsolutePath))
                {
                    File.Delete(trackSource.AbsolutePath);
                }
            }

            if (!string.Equals(operation.CuePath, operation.DestinationCuePath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(operation.CuePath))
            {
                File.Delete(operation.CuePath);
            }
        }
    }

    private static string BuildCueContents(PsxBinMergeOperation operation)
    {
        var builder = new StringBuilder();
        var binFileName = Path.GetFileName(operation.DestinationBinPath);
        builder.AppendLine($@"FILE ""{binFileName}"" BINARY");

        long cumulativeFrames = 0;

        foreach (var trackSource in operation.TrackSources.OrderBy(t => t.Sequence))
        {
            var fileInfo = new FileInfo(trackSource.AbsolutePath);
            var bytesPerFrame = GetBytesPerFrame(trackSource.TrackType);
            var frames = fileInfo.Length / bytesPerFrame;

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
}
