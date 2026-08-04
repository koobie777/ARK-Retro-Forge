using System.Diagnostics;
using ARK.Core.Dat;
using ARK.Core.Hashing;
using ARK.Core.Naming;
using ARK.Core.Scanning;
using ARK.Core.Units;

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
    private readonly TokenVocabulary _vocabulary;

    /// <summary>Creates a verification service.</summary>
    /// <param name="hasher">Computes ROM hashes.</param>
    /// <param name="cache">Hash cache, keyed path + size + mtime.</param>
    /// <param name="inProgress">Detects files still being written.</param>
    /// <param name="vocabulary">
    /// Groups disc units into multi-disc sets, so the report can say a set is incomplete rather
    /// than only that one disc failed.
    /// </param>
    public VerificationService(
        RomHasher hasher,
        HashCache cache,
        InProgressDetector inProgress,
        TokenVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(inProgress);
        ArgumentNullException.ThrowIfNull(vocabulary);
        _hasher = hasher;
        _cache = cache;
        _inProgress = inProgress;
        _vocabulary = vocabulary;
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

            var expected = SelectEntry(scanned, unit);
            var qualifier = unit.FormatQualifiers.Count > 0 ? unit.FormatQualifiers[0] : null;

            // A disc is several files with several DAT hashes. Judging it on one would call a disc
            // good after reading a fraction of it.
            if (IsMultiFile(unit))
            {
                VerifiedUnit verdict;
                try
                {
                    verdict = VerifyMultiFile(unit, file, datName, scanned.Entries, ref hashed, ref bytes, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    break;
                }

                results.Add(verdict);
                continue;
            }

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

        var report = new VerificationReport(
            scan.Root, results, hashed, _cache.Hits, stopwatch.Elapsed, bytes, cancelled);

        // A set is only as complete as its worst disc, so the verdict is derived here rather than
        // left for each caller to recompute from the per-unit states.
        return report with
        {
            DiscSets = DiscSetCompleteness.Evaluate(
                DiscSetGrouper.Group(scan.Units.Select(unit => unit.Unit), _vocabulary), report),
        };
    }

    /// <summary>
    /// Chooses which of a game's catalog entries the single file being hashed is supposed to be.
    /// </summary>
    /// <remarks>
    /// A game can carry several ROM entries under one name, and the first indexed is not
    /// necessarily the one on disk. The real drive holds a PlayStation archive containing a lone
    /// <c>.bin</c> and no cue sheet: its DAT game declares both, and comparing the BIN's hash
    /// against the cue's entry would report corruption on a healthy file — the exact false alarm
    /// the five-state model exists to prevent. Matched on extension, which is the only distinction
    /// available without reading the DAT's sizes.
    /// </remarks>
    private static CatalogEntry? SelectEntry(ScannedUnit scanned, GameUnit unit)
    {
        var entries = scanned.Entries;
        if (entries.Count <= 1)
        {
            return scanned.Match;
        }

        // What is actually going to be hashed: the archive's single entry, or the bare ROM itself.
        var subject = unit.Contents.Count == 1 ? unit.Contents[0].Name : unit.Files[0].Name;
        var extension = System.IO.Path.GetExtension(subject);

        return entries.FirstOrDefault(entry =>
            string.Equals(System.IO.Path.GetExtension(entry.RomName), extension, StringComparison.OrdinalIgnoreCase))
            ?? scanned.Match;
    }

    /// <summary>
    /// A unit whose ROM content spans more than one file — an archived disc image, or a loose cue
    /// with its tracks.
    /// </summary>
    private static bool IsMultiFile(GameUnit unit) =>
        unit.Kind == GameUnitKind.Disc && (unit.Files.Count > 1 || unit.Contents.Count > 1);

    /// <summary>
    /// Verifies every constituent of a disc unit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Verified requires all of them.</b> One track matching proves nothing about the disc, and
    /// a partial pass reported as success is how a broken game gets filed as complete.
    /// </para>
    /// <para>
    /// The cue sheet is verified like any other constituent, because Redump hashes it too. It is
    /// never rewritten to make it match — a mismatching cue is reported and left exactly as it is.
    /// </para>
    /// </remarks>
    private VerifiedUnit VerifyMultiFile(
        GameUnit unit,
        FileEntry primary,
        string? datName,
        IReadOnlyList<CatalogEntry> entries,
        ref int hashed,
        ref long bytes,
        CancellationToken cancellationToken)
    {
        var parts = PartsOf(unit);
        var byRomName = entries
            .GroupBy(entry => entry.RomName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var matched = 0;
        var failures = new List<string>();
        var unmatched = new List<string>();

        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Cached per constituent, keyed on the containing file's own size and mtime. A disc set
            // is hundreds of gigabytes; re-hashing it on every run would make Phase 5's "a second
            // pass hashes approximately nothing" untrue for exactly the collections where it
            // matters most.
            var (key, size, modified) = part.InArchive
                ? ($"{primary.FullPath}|{part.Name}", primary.Size, primary.ModifiedUtc)
                : (part.File!.FullPath, part.File.Size, part.File.ModifiedUtc);

            var hash = _cache.TryGet(key, size, modified);

            if (hash is null)
            {
                var result = part.InArchive
                    ? _hasher.ComputeEntry(primary, part.Name, part.Size, HashPurpose.Verification, cancellationToken)
                    : _hasher.Compute(part.File!, HashPurpose.Verification, normalize: false, cancellationToken);

                if (result.Status == RomReadStatus.ChangedDuringRead)
                {
                    return new VerifiedUnit(
                        primary.FullPath, primary.Name, unit.SetFolder, datName,
                        VerificationState.InProgress,
                        InProgressDetector.Explain(InProgressSignal.ChangedDuringRead),
                        Signal: InProgressSignal.ChangedDuringRead);
                }

                if (!result.Succeeded)
                {
                    failures.Add($"{part.Name}: {result.Error}");
                    continue;
                }

                hash = result.Hash!;
                _cache.Store(key, size, modified, hash);
                hashed++;
                bytes += hash.Size;
            }

            if (!byRomName.TryGetValue(part.Name, out var entry))
            {
                unmatched.Add(part.Name);
                continue;
            }

            if (entry.Crc32 is { Length: > 0 } expected
                && string.Equals(expected, hash.Crc32, StringComparison.OrdinalIgnoreCase))
            {
                matched++;
            }
            else
            {
                failures.Add($"{part.Name}: CRC32 {hash.Crc32} != {entry.Crc32?.ToLowerInvariant() ?? "(none declared)"}");
            }
        }

        if (entries.Count == 0)
        {
            return new VerifiedUnit(
                primary.FullPath, primary.Name, unit.SetFolder, datName,
                VerificationState.Unrecognized,
                datName is null
                    ? "no DAT was resolved for this directory"
                    : "no entry in the resolved DAT matches this name");
        }

        if (failures.Count == 0 && unmatched.Count == 0 && matched == parts.Count)
        {
            return new VerifiedUnit(
                primary.FullPath, primary.Name, unit.SetFolder, datName,
                VerificationState.Verified,
                $"all {parts.Count} constituent file(s) match the DAT entry");
        }

        // Every constituent the DAT does not name is a file this disc should not contain — the
        // unit is not the release it claims to be, whatever the rest of it hashed to.
        var detail = new List<string>();
        if (failures.Count > 0)
        {
            detail.Add($"{failures.Count} of {parts.Count} file(s) failed: {string.Join("; ", failures.Take(3))}");
        }

        if (unmatched.Count > 0)
        {
            detail.Add($"{unmatched.Count} file(s) absent from the DAT entry: {string.Join(", ", unmatched.Take(3))}");
        }

        return new VerifiedUnit(
            primary.FullPath, primary.Name, unit.SetFolder, datName,
            VerificationState.Mismatched, string.Join(" — ", detail));
    }

    /// <summary>One constituent of a multi-file unit: an archive entry, or a file on disk.</summary>
    private readonly record struct UnitPart(string Name, long Size, bool InArchive, FileEntry? File);

    private static IReadOnlyList<UnitPart> PartsOf(GameUnit unit) =>
        unit.Contents.Count > 1
            ? unit.Contents.Select(entry => new UnitPart(entry.Name, entry.Size, true, null)).ToArray()
            : unit.Files.Select(file => new UnitPart(file.Name, file.Size, false, file)).ToArray();

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
