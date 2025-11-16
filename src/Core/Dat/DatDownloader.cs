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
}

public readonly record struct DatDownloadResult(string Path, bool IsDownloaded)
{
    public static DatDownloadResult Downloaded(string path) => new(path, true);
    public static DatDownloadResult Skipped(string path) => new(path, false);
}
