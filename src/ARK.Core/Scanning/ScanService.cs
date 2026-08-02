using ARK.Core.Dat;
using ARK.Core.Units;

namespace ARK.Core.Scanning;

/// <summary>
/// Walks a tree, classifies each directory, resolves game units inside the ROM sets, and sorts
/// every file into one of three buckets.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-only.</b> This is the first phase that reads user files and it produces an inventory
/// and nothing else: no <c>Plan</c>, no journal, no writes. A full scan leaves the tree
/// byte-identical.
/// </para>
/// <para>
/// Every file is accounted for. Three buckets, never two — a file that is neither identified nor
/// excluded is a <i>candidate</i>, reported with its directory's evidence and acted on by
/// nothing.
/// </para>
/// </remarks>
public sealed class ScanService
{
    private readonly IFileSystemReader _reader;
    private readonly DirectoryProfiler _profiler;
    private readonly IReadOnlyList<IGameUnitResolver> _resolvers;
    private readonly ScanRules _rules;

    /// <summary>Creates a scan service.</summary>
    public ScanService(
        IFileSystemReader reader,
        DirectoryProfiler profiler,
        IEnumerable<IGameUnitResolver> resolvers,
        ScanRules rules)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(profiler);
        ArgumentNullException.ThrowIfNull(resolvers);
        ArgumentNullException.ThrowIfNull(rules);

        _reader = reader;
        _profiler = profiler;
        _resolvers = resolvers.ToArray();
        _rules = rules;
    }

    /// <summary>
    /// Scans <paramref name="root"/>. <paramref name="index"/> supplies name identification; pass
    /// null when no DAT is indexed, in which case every unit in a ROM set is a candidate — which
    /// is the honest answer, not a failure.
    /// </summary>
    public ScanReport Scan(string root, DatNameIndex? index = null)
    {
        var directories = new List<DirectoryProfile>();
        var units = new List<ScannedUnit>();
        var excluded = new List<ExcludedFile>();
        var flagged = new List<FileEntry>();

        foreach (var listing in _reader.EnumerateDirectories(root))
        {
            var profile = _profiler.Profile(listing);
            directories.Add(profile);
            flagged.AddRange(profile.IncompleteDownloads);

            if (!profile.IsRomSet)
            {
                var reason = profile.Explain();
                excluded.AddRange(listing.Files.Select(file => new ExcludedFile(file, reason)));
                continue;
            }

            // An in-flight transfer inside a ROM set is set aside rather than identified: its
            // name is already correct while its bytes are not, which is precisely the case that
            // must never be treated as a good file.
            var incomplete = new HashSet<string>(
                profile.IncompleteDownloads.Select(file => file.FullPath),
                StringComparer.OrdinalIgnoreCase);

            var resolvable = new List<FileEntry>();
            foreach (var file in listing.Files)
            {
                if (incomplete.Contains(file.FullPath))
                {
                    excluded.Add(new ExcludedFile(file, "incomplete download in progress"));
                }
                else
                {
                    resolvable.Add(file);
                }
            }

            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var resolver in _resolvers)
            {
                var pending = resolvable.Where(file => !claimed.Contains(file.FullPath)).ToArray();
                if (pending.Length == 0)
                {
                    break;
                }

                foreach (var unit in resolver.Resolve(profile, pending))
                {
                    foreach (var file in unit.Files)
                    {
                        claimed.Add(file.FullPath);
                    }

                    units.Add(new ScannedUnit(unit, index?.Find(unit.Name)));
                }
            }

            // Nothing is silently dropped: a file no resolver claimed is still accounted for.
            foreach (var file in resolvable.Where(file => !claimed.Contains(file.FullPath)))
            {
                excluded.Add(new ExcludedFile(file, "no resolver claimed this file"));
            }
        }

        return new ScanReport(root, directories, units, excluded, flagged);
    }

    /// <summary>The rules this service was built with, surfaced for the report header.</summary>
    public ScanRules Rules => _rules;
}
