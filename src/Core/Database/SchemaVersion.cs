namespace ARK.Core.Database;

/// <summary>
/// Manages database schema versioning
/// </summary>
public class SchemaVersion
{
    public int Version { get; set; }
    public required string Description { get; set; }
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
}
