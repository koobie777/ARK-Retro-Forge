using System.Text.RegularExpressions;
using ARK.Core.Naming;

namespace ARK.Core.Scanning;

/// <summary>
/// Decides whether a directory holds a ROM set, from its listing alone.
/// </summary>
/// <remarks>
/// <para>
/// Folder names are no help. The reference drive spells the same kind of content
/// <c>No-Intro</c>, <c>SNES Roms</c>, <c>Minerva_Myrient</c>,
/// <c>Nintendo - Game Boy Advance</c>, <c>PS2 Downloads</c> and <c>Roms</c>. So the verdict comes
/// from two measured signals, and <b>both</b> are required:
/// </para>
/// <list type="bullet">
///   <item><description>Extension homogeneity ≥ 90% — the share carrying the most common extension.</description></item>
///   <item><description>Naming conformance ≥ 80% — the share whose name carries a recognized region token.</description></item>
/// </list>
/// <para>
/// Neither suffices alone, and each covers a case the other gets wrong. Cheat directories
/// (<c>.cht</c>, <c>.ps3savepatch</c>) score 100% homogeneity and 0% conformance, so homogeneity
/// alone swallows 2,378 files. <c>PSP\Roms</c> is 90 bare <c>.iso</c> files with no archive
/// wrapper, which conformance admits and an extension allowlist would reject.
/// </para>
/// <para>
/// Profiling reads listings only. No file is opened to decide what a directory is.
/// </para>
/// </remarks>
public sealed class DirectoryProfiler
{
    private static readonly Regex QualifierPattern = new(@"[(\[]([^)\]]+)[)\]]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly NameTokenizer _tokenizer;
    private readonly ScanRules _rules;
    private readonly HashSet<string> _knownQualifiers;

    /// <summary>Creates a profiler.</summary>
    /// <param name="tokenizer">Supplies the region-token test behind naming conformance.</param>
    /// <param name="rules">Thresholds and exclusion rules.</param>
    /// <param name="knownFormatQualifiers">
    /// Qualifier terms declared by the system definitions, so <c>GameCube - NKit RVZ</c> is
    /// recognized alongside the parenthesized <c>Nintendo 64 (BigEndian)</c> form.
    /// </param>
    public DirectoryProfiler(NameTokenizer tokenizer, ScanRules rules, IEnumerable<string>? knownFormatQualifiers = null)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(rules);
        _tokenizer = tokenizer;
        _rules = rules;
        _knownQualifiers = new HashSet<string>(knownFormatQualifiers ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Profiles one directory and reaches a verdict.</summary>
    public DirectoryProfile Profile(DirectoryListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        var qualifiers = ExtractQualifiers(listing.Name);
        var incomplete = listing.Files
            .Where(file => _rules.IncompleteDownloadExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var warnings = new List<string>();
        if (incomplete.Length > 0)
        {
            warnings.Add($"{incomplete.Length} file(s) carry an incomplete-download extension");
        }

        DirectoryProfile Verdict(
            DirectoryOutcome outcome,
            ExclusionReason reason,
            string dominantExtension = "",
            double homogeneity = 0,
            double conformance = 0) =>
            new(listing.FullPath, listing.Name, listing.Files.Count, dominantExtension,
                homogeneity, conformance, outcome, reason, qualifiers, warnings, incomplete);

        // Checked first, and against every segment of the path rather than just the leaf, so an
        // excluded directory takes its whole subtree with it. Leaf-only matching would exclude
        // `.ark-quarantine` itself — which holds no files — while still admitting the quarantined
        // ROM sets nested beneath it, whose leaf names look exactly like the live sets they came
        // from. Reporting the rule also beats reporting "contains only subdirectories", which is
        // true of the quarantine root and tells the user nothing about why it was skipped.
        var excludedSegment = MatchExcludedSegment(listing.FullPath);
        if (excludedSegment is not null)
        {
            warnings.Add($"inside '{excludedSegment}', which is excluded by directory-name rule");
            return Verdict(DirectoryOutcome.Excluded, ExclusionReason.DirectoryNameRule);
        }

        if (listing.Files.Count == 0)
        {
            // A directory holding only subdirectories is how a tree is organized, not a finding.
            return Verdict(
                DirectoryOutcome.Excluded,
                listing.SubdirectoryCount > 0 ? ExclusionReason.Container : ExclusionReason.Empty);
        }

        // Both signals are measured before any verdict, so a rejected directory reports the
        // number it failed on AND the one it would have passed.
        var dominant = listing.Files
            .GroupBy(file => file.Extension, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .First();

        var extension = dominant.Key.Length == 0 ? "(none)" : dominant.Key;
        var homogeneity = (double)dominant.Count() / listing.Files.Count;

        var conformant = listing.Files.Count(file => _tokenizer.Parse(file.NameWithoutExtension).IsTokenizable);
        var conformance = (double)conformant / listing.Files.Count;

        if (listing.Files.Count < _rules.MinimumFileCount)
        {
            return Verdict(DirectoryOutcome.Excluded, ExclusionReason.TooFewFiles, extension, homogeneity, conformance);
        }

        if (homogeneity < _rules.ExtensionHomogeneityThreshold)
        {
            return Verdict(DirectoryOutcome.Excluded, ExclusionReason.BelowExtensionHomogeneity, extension, homogeneity, conformance);
        }

        if (conformance < _rules.NamingConformanceThreshold)
        {
            return Verdict(DirectoryOutcome.Excluded, ExclusionReason.BelowNamingConformance, extension, homogeneity, conformance);
        }

        if (_rules.KnownFalsePositiveDirectories.Contains(listing.Name, StringComparer.OrdinalIgnoreCase))
        {
            // Passes both signals and still is not games. Admitted, but never silently.
            warnings.Add("known false positive — conformant naming does not prove game content");
        }

        return Verdict(DirectoryOutcome.RomSet, ExclusionReason.None, extension, homogeneity, conformance);
    }

    private string? MatchExcludedSegment(string path) => path
        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault(segment => _rules.ExcludedDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase));

    private IReadOnlyList<string> ExtractQualifiers(string directoryName)
    {
        var qualifiers = new List<string>();

        foreach (Match match in QualifierPattern.Matches(directoryName))
        {
            qualifiers.Add(match.Groups[1].Value.Trim());
        }

        // "Nintendo - GameCube - NKit RVZ" carries its qualifier as a dash-separated segment
        // rather than a parenthetical, so segments are checked against the declared vocabulary.
        foreach (var segment in QualifierPattern.Replace(directoryName, string.Empty).Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (_knownQualifiers.Contains(segment) && !qualifiers.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                qualifiers.Add(segment);
            }
        }

        return qualifiers;
    }
}
