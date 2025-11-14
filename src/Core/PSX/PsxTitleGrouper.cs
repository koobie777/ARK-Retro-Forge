namespace ARK.Core.PSX;

/// <summary>
/// Groups PSX discs into multi-disc titles
/// </summary>
public class PsxTitleGrouper
{
    /// <summary>
    /// Group discs by title (based on serial prefix and title similarity)
    /// </summary>
    public static Dictionary<string, List<PsxDiscInfo>> GroupByTitle(List<PsxDiscInfo> discs)
    {
        var groups = new Dictionary<string, List<PsxDiscInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var disc in discs)
        {
            // Generate a grouping key based on title and serial prefix
            var groupKey = GenerateGroupKey(disc);
            
            if (!groups.ContainsKey(groupKey))
            {
                groups[groupKey] = new List<PsxDiscInfo>();
            }
            
            groups[groupKey].Add(disc);
        }

        return groups;
    }

    /// <summary>
    /// Generate a grouping key for a disc
    /// </summary>
    private static string GenerateGroupKey(PsxDiscInfo disc)
    {
        // For cheat and educational discs, use full title as key (treat as standalone)
        if (disc.IsCheatDisc || disc.IsEducationalDisc)
        {
            return $"standalone_{disc.Title ?? disc.FilePath}_{Guid.NewGuid()}";
        }

        // Use serial prefix if available (e.g., SLUS-00001 -> SLUS-00001)
        if (!string.IsNullOrWhiteSpace(disc.Serial))
        {
            return disc.Serial;
        }

        // Fall back to normalized title
        if (!string.IsNullOrWhiteSpace(disc.Title))
        {
            // Remove disc suffixes and normalize title for grouping
            var normalizedTitle = DiscSuffixNormalizer.RemoveDiscSuffix(disc.Title);
            normalizedTitle = normalizedTitle.Trim();
            return normalizedTitle;
        }

        // Last resort: use filename
        return Path.GetFileNameWithoutExtension(disc.FilePath);
    }

    /// <summary>
    /// Determine if a group represents a multi-disc title
    /// </summary>
    public static bool IsMultiDisc(List<PsxDiscInfo> group)
    {
        if (group.Count <= 1)
        {
            return false;
        }

        // Don't group cheat or educational discs as multi-disc
        if (group.Any(d => d.IsCheatDisc || d.IsEducationalDisc))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Get the canonical title for a group
    /// </summary>
    public static string GetCanonicalTitle(List<PsxDiscInfo> group)
    {
        // Prefer the first disc's title, with disc suffix removed
        var firstDisc = group.OrderBy(d => d.DiscNumber ?? 1).FirstOrDefault();
        
        if (firstDisc?.Title != null)
        {
            return DiscSuffixNormalizer.RemoveDiscSuffix(firstDisc.Title);
        }

        // Fall back to filename
        if (firstDisc != null)
        {
            var filename = Path.GetFileNameWithoutExtension(firstDisc.FilePath);
            return DiscSuffixNormalizer.RemoveDiscSuffix(filename);
        }

        return "Unknown Title";
    }
}
