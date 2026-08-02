using ARK.Core.Dat;
using ARK.Core.Units;

namespace ARK.Core.Scanning;

/// <summary>Which bucket a file landed in. Three, never two.</summary>
public enum ScanBucket
{
    /// <summary>In a ROM-set directory and its name matches a DAT entry.</summary>
    Identified = 0,

    /// <summary>In a ROM-set directory with no DAT match. Reported, never acted on.</summary>
    Candidate,

    /// <summary>Not ROM content, with a stated reason.</summary>
    Excluded,
}

/// <summary>A resolved unit and the DAT entry its name matched, if any.</summary>
/// <param name="Unit">The game unit.</param>
/// <param name="Match">
/// The matched catalog entry, or null. Matching is by name only — hash verification is a later
/// phase, and until then "identified" means "claims to be this release", not "is".
/// </param>
public sealed record ScannedUnit(GameUnit Unit, CatalogEntry? Match)
{
    /// <summary>Which bucket this unit's files belong to.</summary>
    public ScanBucket Bucket => Match is null ? ScanBucket.Candidate : ScanBucket.Identified;
}

/// <summary>A file that is not ROM content, and why.</summary>
/// <param name="File">The file.</param>
/// <param name="Reason">Stated reason, visible in the report.</param>
public sealed record ExcludedFile(FileEntry File, string Reason);

/// <summary>
/// The result of a scan: an inventory and nothing else. Scanning is read-only, produces no
/// <c>Plan</c>, and writes no journal — there is nothing here to undo.
/// </summary>
/// <param name="Root">The scanned root.</param>
/// <param name="Directories">Every directory profiled, admitted or not.</param>
/// <param name="Units">Every resolved unit with its match result.</param>
/// <param name="Excluded">Every file that is not ROM content, with a reason.</param>
/// <param name="Flagged">Files carrying in-flight-transfer signals.</param>
public sealed record ScanReport(
    string Root,
    IReadOnlyList<DirectoryProfile> Directories,
    IReadOnlyList<ScannedUnit> Units,
    IReadOnlyList<ExcludedFile> Excluded,
    IReadOnlyList<FileEntry> Flagged)
{
    /// <summary>Directories admitted as ROM sets.</summary>
    public IReadOnlyList<DirectoryProfile> RomSetDirectories => Directories.Where(d => d.IsRomSet).ToArray();

    /// <summary>Directories rejected, each carrying its reason.</summary>
    public IReadOnlyList<DirectoryProfile> ExcludedDirectories => Directories.Where(d => !d.IsRomSet).ToArray();

    /// <summary>Units whose name matched a DAT entry.</summary>
    public IReadOnlyList<ScannedUnit> Identified => Units.Where(u => u.Bucket == ScanBucket.Identified).ToArray();

    /// <summary>Units in a ROM set with no DAT match. Reported, never acted on.</summary>
    public IReadOnlyList<ScannedUnit> Candidates => Units.Where(u => u.Bucket == ScanBucket.Candidate).ToArray();

    /// <summary>Units carrying an anomaly ARK declined to resolve by guessing.</summary>
    public IReadOnlyList<ScannedUnit> Anomalies => Units.Where(u => u.Unit.HasAnomalies).ToArray();

    /// <summary>Every file seen, across every directory.</summary>
    public int TotalFiles => Directories.Sum(directory => directory.FileCount);

    /// <summary>Files in the identified bucket.</summary>
    public int IdentifiedFileCount => Identified.Sum(unit => unit.Unit.Files.Count);

    /// <summary>Files in the candidate bucket.</summary>
    public int CandidateFileCount => Candidates.Sum(unit => unit.Unit.Files.Count);

    /// <summary>Files in the excluded bucket.</summary>
    public int ExcludedFileCount => Excluded.Count;

    /// <summary>
    /// True when the three buckets account for every file seen. Nothing is silently dropped, so
    /// this must hold for any scan of any tree.
    /// </summary>
    public bool IsComplete => IdentifiedFileCount + CandidateFileCount + ExcludedFileCount == TotalFiles;
}
