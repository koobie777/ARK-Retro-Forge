using System.Buffers;
using SharpCompress.Archives;

namespace ARK.Core.Archives;

public record ArchiveExtractionResult
{
    public required string ArchivePath { get; init; }
    public required string DestinationDirectory { get; init; }
    public bool Success { get; init; }
    public long EntriesExtracted { get; init; }
    public string? Error { get; init; }
}

public static class ArchiveExtractor
{
    private const int BufferSize = 1024 * 256;

    public static ArchiveExtractionResult Extract(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            var entries = 0;

            using var archive = ArchiveFactory.Open(archivePath);
            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rawEntryKey = string.IsNullOrWhiteSpace(entry.Key)
                    ? Path.GetFileName(archivePath)
                    : entry.Key;
                var entryKey = string.IsNullOrWhiteSpace(rawEntryKey) ? "entry.bin" : rawEntryKey!;
                var entryPath = Path.Combine(destinationDirectory, entryKey.Replace('/', Path.DirectorySeparatorChar));
                var entryDirectory = Path.GetDirectoryName(entryPath);
                if (!string.IsNullOrEmpty(entryDirectory))
                {
                    Directory.CreateDirectory(entryDirectory);
                }

                using var entryStream = entry.OpenEntryStream();
                using var outputStream = File.Open(entryPath, FileMode.Create, FileAccess.Write, FileShare.None);
                CopyStream(entryStream, outputStream, cancellationToken);
                entries++;
            }

            return new ArchiveExtractionResult
            {
                ArchivePath = archivePath,
                DestinationDirectory = destinationDirectory,
                Success = true,
                EntriesExtracted = entries
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ArchiveExtractionResult
            {
                ArchivePath = archivePath,
                DestinationDirectory = destinationDirectory,
                Success = false,
                EntriesExtracted = 0,
                Error = ex.Message
            };
        }
    }

    private static void CopyStream(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                destination.Write(buffer, 0, bytesRead);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static bool RequiresSubdirectory(string archivePath)
    {
        using var archive = ArchiveFactory.Open(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            var extension = Path.GetExtension(entry.Key ?? string.Empty);
            if (extension.Equals(".bin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
