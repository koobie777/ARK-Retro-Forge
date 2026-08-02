using System.Globalization;
using ARK.Core.Scanning;

namespace ARK.Tests;

/// <summary>One row of the reference-drive fixture.</summary>
internal sealed record DriveRow(
    string Directory,
    int Files,
    string DominantExtension,
    int DominantPercent,
    int ConformantPercent,
    string Expected,
    string Note);

/// <summary>
/// Expands <c>corpus/reference-drive.tsv</c> into directory listings.
/// </summary>
/// <remarks>
/// Conformant file names are drawn from the real 9,363-name corpus, so naming conformance is
/// measured against genuine No-Intro naming rather than something shaped to pass. Non-conformant
/// names are generated and deliberately carry no region token.
/// </remarks>
internal static class ReferenceDrive
{
    private const string Root = @"D:\Games";

    public static IReadOnlyList<DriveRow> Rows() =>
        TestFixtures.ReadCorpus("reference-drive.tsv")
            .Select(line => line.Split('\t'))
            .Select(f => new DriveRow(
                f[0],
                int.Parse(f[1], CultureInfo.InvariantCulture),
                f[2],
                int.Parse(f[3], CultureInfo.InvariantCulture),
                int.Parse(f[4], CultureInfo.InvariantCulture),
                f[5],
                f.Length > 6 ? f[6] : string.Empty))
            .ToArray();

    public static IReadOnlyList<DirectoryListing> Listings()
    {
        var conformantNames = TestFixtures.ReadCorpus("real-names.txt");
        var cursor = 0;

        return Rows().Select(row =>
        {
            var files = new List<FileEntry>(row.Files);
            var dominantCount = (int)Math.Round(row.Files * row.DominantPercent / 100.0, MidpointRounding.AwayFromZero);
            var conformantCount = (int)Math.Round(row.Files * row.ConformantPercent / 100.0, MidpointRounding.AwayFromZero);
            var directory = Path.Combine(Root, row.Directory);

            for (var i = 0; i < row.Files; i++)
            {
                // A filler extension keeps the dominant share honest without ever colliding with it.
                var extension = i < dominantCount ? row.DominantExtension : FillerFor(row.DominantExtension);
                var name = i < conformantCount
                    ? conformantNames[cursor++ % conformantNames.Count]
                    : $"{Sanitize(row.Directory)}_asset_{i:D5}";

                files.Add(new FileEntry(
                    Path.Combine(directory, name + extension),
                    name + extension,
                    extension,
                    1024 + i,
                    DateTimeOffset.UnixEpoch));
            }

            return new DirectoryListing(directory, LeafOf(row.Directory), files);
        }).ToArray();
    }

    public static string LeafOf(string relative)
    {
        var slash = relative.LastIndexOf('\\');
        return slash < 0 ? relative : relative[(slash + 1)..];
    }

    private static string FillerFor(string dominant) =>
        dominant.Equals(".txt", StringComparison.OrdinalIgnoreCase) ? ".bin" : ".txt";

    private static string Sanitize(string value) =>
        value.Replace('\\', '_').Replace(' ', '_').Replace('(', '_').Replace(')', '_');
}

/// <summary>
/// A filesystem that exists only in memory. Everything above <see cref="IFileSystemReader"/> is
/// pure, so the whole classifier runs against a 21,095-file drive with no disk involved.
/// </summary>
internal sealed class InMemoryFileSystemReader : IFileSystemReader
{
    private readonly IReadOnlyList<DirectoryListing> _listings;
    private readonly Dictionary<string, byte[]> _contents;

    public InMemoryFileSystemReader(IReadOnlyList<DirectoryListing> listings, Dictionary<string, byte[]>? contents = null)
    {
        _listings = listings;
        _contents = contents ?? new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Set by the scan; asserts that scanning never opens a file to classify a directory.</summary>
    public List<string> Opened { get; } = new();

    public IEnumerable<DirectoryListing> EnumerateDirectories(string root) =>
        _listings.Where(listing => listing.FullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));

    public Stream OpenRead(string path)
    {
        Opened.Add(path);
        return _contents.TryGetValue(path, out var bytes)
            ? new MemoryStream(bytes, writable: false)
            : throw new FileNotFoundException(path);
    }
}
