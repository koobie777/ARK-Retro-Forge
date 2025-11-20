using System.Text;

namespace ARK.Core.Systems.PSX;

/// <summary>
/// Formats PSX disc information into canonical filenames
/// </summary>
public class PsxNameFormatter
{
    /// <summary>
    /// Generate a canonical filename from PSX disc metadata
    /// </summary>
    /// <param name="discInfo">The disc metadata</param>
    /// <param name="restoreArticles">Whether trailing articles (", The") should be restored to the front of the title.</param>
    /// <param name="includeVersion">Whether to include version/revision information in the filename.</param>
    /// <returns>Formatted filename with extension</returns>
    public static string Format(PsxDiscInfo discInfo, bool restoreArticles = false, bool includeVersion = false)
    {
        var builder = new StringBuilder();
        var title = discInfo.Title?.Trim();
        if (!string.IsNullOrWhiteSpace(title))
        {
            title = CleanTitle(title, discInfo);
            builder.Append(restoreArticles ? RestoreArticle(title) : title);
        }

        if (!string.IsNullOrWhiteSpace(discInfo.Region))
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }
            builder.Append($"({discInfo.Region.Trim()})");
        }

        if (includeVersion && !string.IsNullOrWhiteSpace(discInfo.Version))
        {
            builder.Append($" ({discInfo.Version.Trim()})");
        }

        if (discInfo.IsMultiDisc && discInfo.DiscNumber.HasValue)
        {
            builder.Append($" (Disc {discInfo.DiscNumber.Value})");
        }

        if (discInfo.TrackNumber.HasValue)
        {
            builder.Append($" (Track {discInfo.TrackNumber.Value:00})");
        }

        if (!string.IsNullOrWhiteSpace(discInfo.Serial))
        {
            builder.Append($" [{discInfo.Serial.Trim()}]");
        }

        var baseName = builder.ToString().Trim();

        if (!string.IsNullOrWhiteSpace(discInfo.Extension))
        {
            baseName += discInfo.Extension;
        }

        return SanitizeFileName(baseName);
    }

    private static string CleanTitle(string title, PsxDiscInfo info)
    {
        title = title.Trim();

        // Remove repeated (Region) suffixes that match metadata
        string? regionSuffix = null;
        if (!string.IsNullOrWhiteSpace(info.Region))
        {
            regionSuffix = $"({info.Region.Trim()})";
            title = StripRepeatedSuffix(title, regionSuffix);
        }

        // Remove lingering Disc/Discs annotations from legacy filenames
        title = System.Text.RegularExpressions.Regex.Replace(
            title,
            @"\s*\(Discs?\s+\d+(?:\s*(?:of|-|\u2013)\s*\d+)?\)",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        // Remove (Track N)
        title = System.Text.RegularExpressions.Regex.Replace(title, @"\s*\(Track \d+\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove (Version)
        title = System.Text.RegularExpressions.Regex.Replace(title, @"\s*\((?:Rev|v|Ver\.?)\s*[\d.]+\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!string.IsNullOrWhiteSpace(regionSuffix))
        {
            title = StripRepeatedSuffix(title, regionSuffix!);
        }

        return title.Trim();
    }

    private static string StripRepeatedSuffix(string value, string suffix)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(suffix))
        {
            return value;
        }

        var trimmedSuffix = suffix.Trim();
        var comparison = StringComparison.OrdinalIgnoreCase;

        while (value.Length >= trimmedSuffix.Length && value.EndsWith(trimmedSuffix, comparison))
        {
            value = value[..^trimmedSuffix.Length].TrimEnd();
        }

        return value;
    }
    
    /// <summary>
    /// Normalize disc suffix from "(Disc N of M)" to "(Disc N)"
    /// </summary>
    /// <param name="filename">Filename to normalize</param>
    /// <returns>Normalized filename</returns>
    public static string NormalizeDiscSuffix(string filename)
    {
        var extension = Path.GetExtension(filename);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        
        // Replace "(Disc N of M)" with "(Disc N)"
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            nameWithoutExt,
            @"\(Disc (\d+) of \d+\)",
            "(Disc $1)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        
        return normalized + extension;
    }
    
    /// <summary>
    /// Sanitize a filename by removing invalid characters
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Trim();
    }

    private static string RestoreArticle(string title)
    {
        var match = System.Text.RegularExpressions.Regex.Match(title, @"^(?<core>.+),\s*(?<article>The|A|An)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return title;
        }

        var article = match.Groups["article"].Value;
        var core = match.Groups["core"].Value.Trim();
        return $"{article} {core}";
    }
}
