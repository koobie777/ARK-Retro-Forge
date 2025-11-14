using System.Text.RegularExpressions;

namespace ARK.Core.Psx;

/// <summary>
/// Generates standardized filenames for PSX titles.
/// </summary>
public class PsxNamingService
{
    /// <summary>
    /// Generates a filename for a single-disc title.
    /// Format: "Title (Region) [vX.Y] [Serial]"
    /// </summary>
    public string GenerateSingleDiscName(PsxTitleGroup group)
    {
        var parts = new List<string>();

        // Normalize title (handle "Title, The" -> "The Title")
        var title = NormalizeArticle(group.Title);
        parts.Add(title);

        // Add region if known
        if (!string.IsNullOrEmpty(group.Region) && group.Region != "Unknown")
        {
            parts.Add($"({group.Region})");
        }

        // Add version if present
        if (!string.IsNullOrEmpty(group.Version))
        {
            parts.Add($"[v{group.Version}]");
        }

        // Add serial if present
        var serial = group.Discs.FirstOrDefault()?.Serial;
        if (!string.IsNullOrEmpty(serial))
        {
            parts.Add($"[{serial}]");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Generates a filename for a multi-disc game disc.
    /// Format: "Title (Region) [vX.Y] [DiscSerial] (Disc N)"
    /// </summary>
    public string GenerateMultiDiscName(PsxTitleGroup group, PsxDisc disc)
    {
        var parts = new List<string>();

        // Normalize title
        var title = NormalizeArticle(group.Title);
        parts.Add(title);

        // Add region if known
        if (!string.IsNullOrEmpty(group.Region) && group.Region != "Unknown")
        {
            parts.Add($"({group.Region})");
        }

        // Add version if present
        if (!string.IsNullOrEmpty(group.Version))
        {
            parts.Add($"[v{group.Version}]");
        }

        // Add disc-specific serial if present
        if (!string.IsNullOrEmpty(disc.Serial))
        {
            parts.Add($"[{disc.Serial}]");
        }

        // Add disc number
        parts.Add($"(Disc {disc.DiscNumber})");

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Generates a filename for a multi-disc playlist.
    /// Format: "Title (Region) [vX.Y].m3u"
    /// Note: No serials in playlist filename.
    /// </summary>
    public string GeneratePlaylistName(PsxTitleGroup group)
    {
        var parts = new List<string>();

        // Normalize title
        var title = NormalizeArticle(group.Title);
        parts.Add(title);

        // Add region if known
        if (!string.IsNullOrEmpty(group.Region) && group.Region != "Unknown")
        {
            parts.Add($"({group.Region})");
        }

        // Add version if present
        if (!string.IsNullOrEmpty(group.Version))
        {
            parts.Add($"[v{group.Version}]");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Normalizes article placement in title names.
    /// "Legend of Dragoon, The" -> "The Legend of Dragoon"
    /// </summary>
    private string NormalizeArticle(string title)
    {
        // Check if title ends with ", The", ", A", or ", An"
        var articleMatch = Regex.Match(title, @"^(.+),\s*(The|A|An)$", RegexOptions.IgnoreCase);
        if (articleMatch.Success)
        {
            var mainTitle = articleMatch.Groups[1].Value.Trim();
            var article = articleMatch.Groups[2].Value;
            return $"{article} {mainTitle}";
        }

        return title;
    }
}
