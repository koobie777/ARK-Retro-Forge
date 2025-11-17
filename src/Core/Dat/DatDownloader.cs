using System.IO.Compression;
using System.Linq;

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

        using var response = await _httpClient.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var destinationStream = File.Create(destinationPath))
        await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            await contentStream.CopyToAsync(destinationStream, cancellationToken);
        }

        if (IsZipArchive(destinationPath))
        {
            destinationPath = ExtractZipArchive(destinationPath, destinationDirectory);
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
        if (stream.Read(header) != 4)
        {
            return false;
        }

        return header[0] == 0x50 && header[1] == 0x4B && (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07)
               && (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08);
    }

    private static string ExtractZipArchive(string zipPath, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .OrderByDescending(e => e.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            .ThenBy(e => e.Name)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Zip archive '{zipPath}' does not contain any files.");

        var sanitizedName = Sanitize(entry.Name);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(sanitizedName)))
        {
            sanitizedName += ".dat";
        }

        var extractedPath = Path.Combine(destinationDirectory, sanitizedName);
        entry.ExtractToFile(extractedPath, overwrite: true);
        File.Delete(zipPath);
        return extractedPath;
    }
}

public readonly record struct DatDownloadResult(string Path, bool IsDownloaded)
{
    public static DatDownloadResult Downloaded(string path) => new(path, true);
    public static DatDownloadResult Skipped(string path) => new(path, false);
}
