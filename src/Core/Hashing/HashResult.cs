namespace ARK.Core.Hashing;

/// <summary>
/// Represents the result of a hash operation
/// </summary>
public record HashResult
{
    public required string FilePath { get; init; }
    public string? Crc32 { get; init; }
    public string? Md5 { get; init; }
    public string? Sha1 { get; init; }
    public long FileSize { get; init; }
    public DateTime ComputedAt { get; init; } = DateTime.UtcNow;
}
