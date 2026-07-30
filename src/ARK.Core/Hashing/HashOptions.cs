namespace ARK.Core.Hashing;

/// <summary>
/// Options for hashing operations
/// </summary>
public record HashOptions
{
    public bool ComputeCrc32 { get; init; } = true;
    public bool ComputeMd5 { get; init; } = true;
    public bool ComputeSha1 { get; init; } = true;
    public int BufferSize { get; init; } = 81920; // 80 KB buffer for streaming
}
