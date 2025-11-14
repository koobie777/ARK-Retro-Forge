namespace ARK.Core.Psx;

/// <summary>
/// Represents a grouped PSX title with its associated disc files and metadata.
/// </summary>
public class PsxTitleGroup
{
    public string Title { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string? Version { get; set; }
    public List<PsxDisc> Discs { get; set; } = new();
    public string? PlaylistPath { get; set; }
    public string GameDirectory { get; set; } = string.Empty;

    public int DiscCount => Discs.Count;
    public bool IsMultiDisc => DiscCount > 1;

    /// <summary>
    /// Gets all file paths associated with this title (discs + playlist).
    /// </summary>
    public IEnumerable<string> GetAllFilePaths()
    {
        foreach (var disc in Discs)
        {
            foreach (var file in disc.GetAllFiles())
            {
                yield return file;
            }
        }

        if (!string.IsNullOrEmpty(PlaylistPath))
        {
            yield return PlaylistPath;
        }
    }
}

/// <summary>
/// Represents a single disc in a PSX title.
/// </summary>
public class PsxDisc
{
    public int DiscNumber { get; set; }
    public string? Serial { get; set; }
    public string? CuePath { get; set; }
    public List<string> BinPaths { get; set; } = new();
    public string? ChdPath { get; set; }

    public bool HasBinCue => !string.IsNullOrEmpty(CuePath) && BinPaths.Any();
    public bool HasChd => !string.IsNullOrEmpty(ChdPath);

    /// <summary>
    /// Gets all file paths for this disc.
    /// </summary>
    public IEnumerable<string> GetAllFiles()
    {
        if (!string.IsNullOrEmpty(CuePath))
        {
            yield return CuePath;
        }

        foreach (var bin in BinPaths)
        {
            yield return bin;
        }

        if (!string.IsNullOrEmpty(ChdPath))
        {
            yield return ChdPath;
        }
    }
}
