using System.IO.Compression;

namespace ARK.Core.Dat;

public sealed class DatDownloader
{
    private readonly HttpClient _httpClient;

    public DatDownloader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<DatDownloadResult> DownloadAsync(DatSourceDefinition source, string destinationDirectory, bool force = false, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        var targetFileName = BuildFileName(source);
        var destinationPath = Path.Combine(destinationDirectory, targetFileName);

        if (!force && File.Exists(destinationPath))
        {
            return DatDownloadResult.Skipped(destinationPath);
        }

        var tempPath = Path.Combine(destinationDirectory, $"{targetFileName}.{Guid.NewGuid():N}.download");
        SafeDelete(tempPath);

        try
        {
            using var response = await _httpClient.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var destinationStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                await contentStream.CopyToAsync(destinationStream, cancellationToken);
            }

            WaitForFileRelease(tempPath);

            if (IsZipArchive(tempPath))
            {
                destinationPath = ExtractZipArchive(tempPath, destinationDirectory);
            }
            else
            {
                SafeMove(tempPath, destinationPath, overwrite: true);
            }
        }
        catch
        {
            SafeDelete(tempPath);

            throw;
        }

        return DatDownloadResult.Downloaded(destinationPath);
    }

    private static string BuildFileName(DatSourceDefinition source)
    {
        var sanitizedName = Sanitize(source.Name);
        var extension = Path.GetExtension(new Uri(source.Url).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".dat";
        }

        return $"{sanitizedName}{extension}";
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "dat";
        }

        var sanitized = value;
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(ch, '_');
        }

        return sanitized.Trim();
    }

    private static bool IsZipArchive(string path)
    {
        Span<byte> header = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        if (stream.Read(header) < 4)
        {
            return false;
        }

        return header[0] == 0x50 && header[1] == 0x4B && (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07)
               && (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08);
    }

    private static string ExtractZipArchive(string zipPath, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = SelectZipEntry(archive) ?? throw new InvalidOperationException($"Zip archive '{zipPath}' does not contain any files.");

        var sanitizedName = Sanitize(entry.Name);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(sanitizedName)))
        {
            sanitizedName += ".dat";
        }

        var extractedPath = Path.Combine(destinationDirectory, sanitizedName);
        try
        {
            entry.ExtractToFile(extractedPath, overwrite: true);
            WaitForFileRelease(extractedPath);
        }
        catch
        {
            SafeDelete(extractedPath);
            throw;
        }
        finally
        {
            SafeDelete(zipPath);
        }

        return extractedPath;
    }

    private static ZipArchiveEntry? SelectZipEntry(ZipArchive archive)
    {
        ZipArchiveEntry? candidate = null;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (candidate is null)
            {
                candidate = entry;
                continue;
            }

            var isDat = entry.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
            var candidateIsDat = candidate.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

            if (isDat && !candidateIsDat)
            {
                candidate = entry;
                continue;
            }

            if (isDat == candidateIsDat &&
                string.Compare(entry.Name, candidate.Name, StringComparison.Ordinal) < 0)
            {
                candidate = entry;
            }
        }

        return candidate;
    }
    private static void SafeDelete(string path, int attempts = 5, int delayMs = 200)
    {
        if (!File.Exists(path))
        {
            return;
        }

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                System.Threading.Thread.Sleep(delayMs);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                System.Threading.Thread.Sleep(delayMs);
            }
        }
    }

    private static void SafeMove(string source, string destination, bool overwrite)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                WaitForFileRelease(source);
                if (overwrite && File.Exists(destination))
                {
                    WaitForFileRelease(destination);
                    File.Delete(destination);
                }

                File.Move(source, destination);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                System.Threading.Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                System.Threading.Thread.Sleep(200);
            }
        }

        if (overwrite && File.Exists(destination))
        {
            File.Delete(destination);
        }

        File.Move(source, destination);
    }

    private static void WaitForFileRelease(string path, int attempts = 30, int delayMs = 300)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return;
                }
            }
            catch (IOException) when (attempt < attempts)
            {
                System.Threading.Thread.Sleep(delayMs);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                System.Threading.Thread.Sleep(delayMs);
            }
        }
    }
}

public readonly record struct DatDownloadResult(string Path, bool IsDownloaded)
{
    public static DatDownloadResult Downloaded(string path) => new(path, true);
    public static DatDownloadResult Skipped(string path) => new(path, false);
}
