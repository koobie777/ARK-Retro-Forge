namespace ARK.Cli.Infrastructure;

/// <summary>
/// Progress information for operations
/// </summary>
public record OpProgress
{
    public required string Phase { get; init; }
    public string? File { get; init; }
    public int Index { get; init; }
    public int Count { get; init; }
    public long BytesProcessed { get; init; }
    public long BytesTotal { get; init; }
    public double ThroughputMBps { get; init; }
    public TimeSpan? Eta { get; init; }

    public double PercentComplete => Count > 0 ? (Index * 100.0 / Count) : 0;
}
