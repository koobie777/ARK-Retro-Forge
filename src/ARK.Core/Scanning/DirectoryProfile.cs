namespace ARK.Core.Scanning;

/// <summary>Whether a directory holds a ROM set.</summary>
public enum DirectoryOutcome
{
    /// <summary>Both signals cleared their thresholds.</summary>
    RomSet = 0,

    /// <summary>Not ROM content. <see cref="DirectoryProfile.Reason"/> states why.</summary>
    Excluded,
}

/// <summary>Why a directory was excluded. Always accompanied by the measured numbers.</summary>
public enum ExclusionReason
{
    /// <summary>Not excluded.</summary>
    None = 0,

    /// <summary>No files directly inside it.</summary>
    Empty,

    /// <summary>Holds only subdirectories. Not a finding — the tree has to be walked through something.</summary>
    Container,

    /// <summary>Fewer files than <see cref="ScanRules.MinimumFileCount"/>; signals would be noise.</summary>
    TooFewFiles,

    /// <summary>Matched a configured excluded directory name.</summary>
    DirectoryNameRule,

    /// <summary>Mixed extensions — below the homogeneity threshold.</summary>
    BelowExtensionHomogeneity,

    /// <summary>
    /// Too few names carry a region token. This is what separates a cheat directory — 100%
    /// homogeneous, 0% conformant — from a ROM set.
    /// </summary>
    BelowNamingConformance,
}

/// <summary>
/// The measured signals for one directory and the verdict they produced. Both signals are always
/// reported even when the first one already decided, so a rejected directory can be argued with.
/// </summary>
/// <param name="Path">Absolute directory path.</param>
/// <param name="Name">Leaf directory name.</param>
/// <param name="FileCount">Files directly inside.</param>
/// <param name="DominantExtension">Most common extension, lower-cased.</param>
/// <param name="ExtensionHomogeneity">Share carrying <paramref name="DominantExtension"/>, 0..1.</param>
/// <param name="NamingConformance">Share whose name carries a recognized region token, 0..1.</param>
/// <param name="Outcome">The verdict.</param>
/// <param name="Reason">Why, when excluded.</param>
/// <param name="FormatQualifiers">Qualifiers read off the folder name, e.g. <c>BigEndian</c>.</param>
/// <param name="Warnings">Non-fatal notes, e.g. a known false positive.</param>
/// <param name="IncompleteDownloads">Files carrying an in-flight-transfer extension.</param>
public sealed record DirectoryProfile(
    string Path,
    string Name,
    int FileCount,
    string DominantExtension,
    double ExtensionHomogeneity,
    double NamingConformance,
    DirectoryOutcome Outcome,
    ExclusionReason Reason,
    IReadOnlyList<string> FormatQualifiers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<FileEntry> IncompleteDownloads)
{
    /// <summary>True when this directory holds a ROM set.</summary>
    public bool IsRomSet => Outcome == DirectoryOutcome.RomSet;

    /// <summary>Human-readable statement of the verdict, for the report.</summary>
    public string Explain() => Reason switch
    {
        ExclusionReason.None => $"ROM set — {ExtensionHomogeneity:P0} {DominantExtension}, {NamingConformance:P0} named",
        ExclusionReason.Empty => "empty directory",
        ExclusionReason.Container => "contains only subdirectories",
        ExclusionReason.TooFewFiles => $"only {FileCount} file(s)",
        ExclusionReason.DirectoryNameRule => "excluded by directory-name rule",
        ExclusionReason.BelowExtensionHomogeneity =>
            $"mixed extensions — {ExtensionHomogeneity:P0} {DominantExtension}",
        ExclusionReason.BelowNamingConformance =>
            $"names not conformant — {NamingConformance:P0} carry a region token",
        _ => Reason.ToString(),
    };
}
