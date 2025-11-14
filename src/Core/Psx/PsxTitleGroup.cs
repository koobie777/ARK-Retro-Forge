namespace ARK.Core.Psx;

/// <summary>
/// Represents a logical PSX game title, which may contain one or more discs
/// </summary>
public record PsxTitleGroup
{
    /// <summary>
    /// Game title (normalized with articles moved to front)
    /// </summary>
    public required string Title { get; init; }
    
    /// <summary>
    /// Region (e.g., USA, Europe, Japan), null if unknown
    /// </summary>
    public string? Region { get; init; }
    
    /// <summary>
    /// Version (e.g., v1.0, v1.1), null if unknown
    /// </summary>
    public string? Version { get; init; }
    
    /// <summary>
    /// List of discs in this title (ordered by disc number)
    /// </summary>
    public required IReadOnlyList<PsxDisc> Discs { get; init; }
    
    /// <summary>
    /// Root folder containing the title (null if files are not in a dedicated folder)
    /// </summary>
    public string? RootFolder { get; init; }
    
    /// <summary>
    /// Whether this is a multi-disc title
    /// </summary>
    public bool IsMultiDisc => Discs.Count > 1;
    
    /// <summary>
    /// All serial numbers for this title
    /// </summary>
    public IEnumerable<string> Serials => Discs
        .Where(d => !string.IsNullOrEmpty(d.Serial))
        .Select(d => d.Serial!);
}
