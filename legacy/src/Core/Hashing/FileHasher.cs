using System.Security.Cryptography;

namespace ARK.Core.Hashing;

/// <summary>
/// Provides streaming hash computation for files
/// </summary>
public class FileHasher
{
    private readonly HashOptions _options;

    public FileHasher(HashOptions? options = null)
    {
        _options = options ?? new HashOptions();
    }

    /// <summary>
    /// Compute hashes for a file using streaming to minimize memory usage
    /// </summary>
    public async Task<HashResult> ComputeHashesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var fileInfo = new FileInfo(filePath);
        string? crc32 = null;
        string? md5 = null;
        string? sha1 = null;

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, _options.BufferSize, useAsync: true);

        using var crc32Hasher = _options.ComputeCrc32 ? new Crc32Hasher() : null;
        using var md5Hasher = _options.ComputeMd5 ? MD5.Create() : null;
        using var sha1Hasher = _options.ComputeSha1 ? SHA1.Create() : null;

        var buffer = new byte[_options.BufferSize];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, _options.BufferSize), cancellationToken)) > 0)
        {
            crc32Hasher?.Append(buffer.AsSpan(0, bytesRead));
            
            if (md5Hasher != null)
            {
                md5Hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
            }
            
            if (sha1Hasher != null)
            {
                sha1Hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
            }
        }

        if (crc32Hasher != null)
        {
            crc32 = Convert.ToHexString(crc32Hasher.GetHashAndReset()).ToLowerInvariant();
        }

        if (md5Hasher != null)
        {
            md5Hasher.TransformFinalBlock([], 0, 0);
            md5 = Convert.ToHexString(md5Hasher.Hash!).ToLowerInvariant();
        }

        if (sha1Hasher != null)
        {
            sha1Hasher.TransformFinalBlock([], 0, 0);
            sha1 = Convert.ToHexString(sha1Hasher.Hash!).ToLowerInvariant();
        }

        return new HashResult
        {
            FilePath = filePath,
            Crc32 = crc32,
            Md5 = md5,
            Sha1 = sha1,
            FileSize = fileInfo.Length
        };
    }
}
