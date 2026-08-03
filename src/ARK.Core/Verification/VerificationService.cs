using System.Diagnostics;
using ARK.Core.Dat;
using ARK.Core.Hashing;
using ARK.Core.Scanning;

namespace ARK.Core.Verification;

/// <summary>
/// Sorts a scanned set into the five verification states.
/// </summary>
/// <remarks>
/// <para>
/// Verification always hashes. Size and presence prove nothing: incomplete torrent transfers
/// pre-allocate, and pieces span file boundaries, so a deselected file adjacent to a selected one
/// receives partial data and ends up with the correct name, the correct size and the wrong
/// contents. Every cheap check passes it.
/// </para>
/// <para>
/// Read-only. Nothing is deleted, moved or quarantined.
/// </para>
/// </remarks>
public sealed class VerificationService
{
    private readonly RomHasher _hasher;
    private readonly HashCache _cache;
    private readonly InProgressDetector _inProgress;

    /// <summary>Creates a verification service.</summary>
    public VerificationService(RomHasher hasher, HashCache cache, InProgressDetector inProgress)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(inProgress);
        _hasher = hasher;
        _cache = cache;
        _inProgress = inProgress;
    }

    /// <summary>
    /// Verifies every unit in <paramref name="scan"/>.
    /// </summary>
    /// <remarks>
    /// Cancellation stops the run and returns what was completed. Cache entries are already
    /// committed by then, so nothing computed is lost.
    /// </remarks>
    public VerificationReport Verify(
        ScanReport scan,
        IProgress<VerificationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var stopwatch = Stopwatch.StartNew();
        var results = new List<VerifiedUnit>(scan.Units.Count);
        var hashed = 0;
        long bytes = 0;
        var cancelled = false;

        // Files scan already set aside are not ROM content and are reported as such, not hashed.
        foreach (var excluded in scan.Excluded)
        {
            results.Add(new VerifiedUnit(
                excluded.File.FullPath,
                excluded.File.Name,
                string.Empty,
                null,
                VerificationState.Excluded,
                excluded.Reason));
        }

        for (var i = 0; i < scan.Units.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var scanned = scan.Units[i];
            var unit = scanned.Unit;
            var file = unit.Files[0];

            progress?.Report(new VerificationProgress(i, scan.Units.Count, file.Name, bytes));

            var datName = ScopeFor(scan, unit.SetFolder);

            // In Progress is evaluated FIRST. A file mid-download fails hash comparison, and
            // reporting that as corruption is how a report stops being read.
            var signal = _inProgress.Inspect(file);
            if (signal != InProgressSignal.None)
            {
                results.Add(new VerifiedUnit(
                    file.FullPath, file.Name, unit.SetFolder, datName,
                    VerificationState.InProgress, InProgressDetector.Explain(signal), Signal: signal));
                continue;
            }

            var expected = scanned.Match;
            var qualifier = unit.FormatQualifiers.Count > 0 ? unit.FormatQualifiers[0] : null;

            var cached = _cache.TryGet(file.FullPath, file.Size, file.ModifiedUtc);
            RomHash? hash = cached;

            if (cached is null)
            {
                RomHashResult result;
                try
                {
                    result = _hasher.Compute(file, HashPurpose.Verification, normalize: false, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Stop cleanly rather than throwing away the run. Everything hashed so far is
                    // already committed to the cache.
                    cancelled = true;
                    break;
                }

                if (result.Status == RomReadStatus.ChangedDuringRead)
                {
                    // Proof, not a hint: the bytes moved under the read. Discard the hash and
                    // never cache it.
                    results.Add(new VerifiedUnit(
                        file.FullPath, file.Name, unit.SetFolder, datName,
                        VerificationState.InProgress,
                        InProgressDetector.Explain(InProgressSignal.ChangedDuringRead),
                        Signal: InProgressSignal.ChangedDuringRead));
                    continue;
                }

                if (!result.Succeeded)
                {
                    results.Add(Unreadable(file, unit.SetFolder, datName, result));
                    continue;
                }

                hash = result.Hash!;
                _cache.Store(file.FullPath, file.Size, file.ModifiedUtc, hash);
                hashed++;
                bytes += hash.Size;
            }

            results.Add(Judge(file, unit.SetFolder, datName, expected, hash!, qualifier));
        }

        stopwatch.Stop();
        progress?.Report(new VerificationProgress(scan.Units.Count, scan.Units.Count, string.Empty, bytes));

        return new VerificationReport(
            scan.Root, results, hashed, _cache.Hits, stopwatch.Elapsed, bytes, cancelled);
    }

    private static VerifiedUnit Judge(
        FileEntry file,
        string setFolder,
        string? datName,
        CatalogEntry? expected,
        RomHash hash,
        string? folderQualifier)
    {
        var contradiction = RomFormatDetector.Contradicts(hash.Format, folderQualifier)
            ? $"detected {hash.Format.Qualifier} ({hash.Format.Detail}) but the folder declares {folderQualifier}"
            : null;

        if (expected is null)
        {
            return new VerifiedUnit(
                file.FullPath, file.Name, setFolder, datName,
                VerificationState.Unrecognized,
                datName is null
                    ? "no DAT was resolved for this directory"
                    : "no entry in the resolved DAT matches this name",
                ActualCrc32: hash.Crc32,
                Format: hash.Format.Format,
                FormatQualifier: folderQualifier,
                FormatContradiction: contradiction,
                Normalized: hash.Normalized);
        }

        var matches = expected.Crc32 is { Length: > 0 }
            && string.Equals(expected.Crc32, hash.Crc32, StringComparison.OrdinalIgnoreCase);

        if (matches)
        {
            return new VerifiedUnit(
                file.FullPath, file.Name, setFolder, datName,
                VerificationState.Verified, "hash matches the DAT entry",
                expected.Crc32, hash.Crc32, hash.Format.Format, folderQualifier, contradiction,
                Normalized: hash.Normalized);
        }

        // Diagnosable, not unknown: this claims to be a specific release and the bytes disagree.
        var detail = expected.Crc32 is { Length: > 0 }
            ? $"name matches '{expected.GameName ?? expected.RomName}' but CRC32 {hash.Crc32} != {expected.Crc32.ToLowerInvariant()}"
            : $"name matches '{expected.GameName ?? expected.RomName}' but the DAT entry declares no CRC32 to compare";

        return new VerifiedUnit(
            file.FullPath, file.Name, setFolder, datName,
            VerificationState.Mismatched, detail,
            expected.Crc32, hash.Crc32, hash.Format.Format, folderQualifier, contradiction,
            Normalized: hash.Normalized);
    }

    private static VerifiedUnit Unreadable(FileEntry file, string setFolder, string? datName, RomHashResult result)
    {
        // An unreadable archive and a still-growing one are structurally identical — a truncated
        // write looks the same either way. The in-progress signals were already checked above and
        // did not fire, so this is reported as damage rather than as activity.
        var state = result.Status == RomReadStatus.NotSingleEntry
            ? VerificationState.Excluded
            : VerificationState.Unrecognized;

        var detail = result.Status == RomReadStatus.NotSingleEntry
            ? $"not a single-ROM archive: {result.Error}"
            : $"could not be read: {result.Error}";

        return new VerifiedUnit(file.FullPath, file.Name, setFolder, datName, state, detail);
    }

    private static string? ScopeFor(ScanReport scan, string setFolder) =>
        scan.Directories.FirstOrDefault(directory =>
            string.Equals(directory.Name, setFolder, StringComparison.OrdinalIgnoreCase))?.Scope?.DatName;
}
