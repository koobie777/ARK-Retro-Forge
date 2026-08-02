using System.Globalization;
using System.Text.RegularExpressions;
using ARK.Core.Scanning;

namespace ARK.Tests;

/// <summary>The split the reference drive is expected to produce, read from the fixture's own header.</summary>
/// <param name="RomSetDirectories">Directories admitted as ROM sets.</param>
/// <param name="RomSetFiles">Files inside them.</param>
/// <param name="OtherDirectories">Directories with enough files to profile that were still excluded.</param>
/// <param name="OtherFiles">Files inside those.</param>
/// <param name="TooFewDirectories">Directories below the minimum file count.</param>
/// <param name="TooFewFiles">Files inside those.</param>
/// <param name="TotalFiles">Every file on the drive.</param>
internal sealed record DriveExpectations(
    int RomSetDirectories,
    int RomSetFiles,
    int OtherDirectories,
    int OtherFiles,
    int TooFewDirectories,
    int TooFewFiles,
    int TotalFiles);

/// <summary>
/// The real 22,050-file mixed-use emulation drive, from <c>corpus/reference-drive.txt</c>.
/// </summary>
/// <remarks>
/// <para>
/// A recursive listing of relative paths — genuine directory names, genuine file names, genuine
/// mess. This is the drive the two signals were derived from: ROM sets sitting alongside
/// emulators, firmware, cheat databases, shader caches, Android factory images and an unrelated
/// Python toolchain.
/// </para>
/// <para>
/// Only paths are listed, so size and timestamp are synthesized. Neither participates in
/// classification: the verdict comes from extension homogeneity and naming conformance alone.
/// </para>
/// </remarks>
internal static class ReferenceDrive
{
    private const string Root = @"D:\";
    private const string Fixture = "reference-drive.txt";

    private static readonly Regex ExpectationPattern = new(
        @"(?<count>[\d,]+)\s+(?<kind>ROM-set|other|too-few)\s+dirs\s*/\s*(?<files>[\d,]+)\s+files",
        RegexOptions.IgnoreCase);

    private static readonly Regex TotalPattern = new(@"Total\s+(?<total>[\d,]+)", RegexOptions.IgnoreCase);

    /// <summary>
    /// Reads the expected split out of the fixture header, so replacing the listing replaces the
    /// expectations with it rather than leaving numbers hard-coded in a test.
    /// </summary>
    public static DriveExpectations Expectations()
    {
        var header = string.Join(' ', RawLines().Where(line => line.StartsWith('#')));
        var found = ExpectationPattern.Matches(header)
            .ToDictionary(
                match => match.Groups["kind"].Value.ToUpperInvariant(),
                match => (Dirs: Number(match.Groups["count"].Value), Files: Number(match.Groups["files"].Value)));

        return new DriveExpectations(
            found["ROM-SET"].Dirs, found["ROM-SET"].Files,
            found["OTHER"].Dirs, found["OTHER"].Files,
            found["TOO-FEW"].Dirs, found["TOO-FEW"].Files,
            Number(TotalPattern.Match(header).Groups["total"].Value));
    }

    /// <summary>Expands the listing into one <see cref="DirectoryListing"/> per directory holding files.</summary>
    public static IReadOnlyList<DirectoryListing> Listings()
    {
        var byDirectory = new Dictionary<string, List<FileEntry>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var relative in Paths())
        {
            var separator = relative.LastIndexOf('\\');
            var directory = separator < 0 ? string.Empty : relative[..separator];
            var name = separator < 0 ? relative : relative[(separator + 1)..];

            if (!byDirectory.TryGetValue(directory, out var files))
            {
                files = new List<FileEntry>();
                byDirectory[directory] = files;
                order.Add(directory);
            }

            var dot = name.LastIndexOf('.');
            var extension = dot > 0 ? name[dot..].ToLowerInvariant() : string.Empty;

            files.Add(new FileEntry(
                Root + relative,
                name,
                extension,
                1024,
                DateTimeOffset.UnixEpoch));
        }

        return order
            .Select(directory => new DirectoryListing(Root + directory, LeafOf(directory), byDirectory[directory]))
            .ToArray();
    }

    /// <summary>Every relative file path on the drive.</summary>
    public static IReadOnlyList<string> Paths() =>
        RawLines().Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();

    /// <summary>Leaf directory name — what the classifier sees and what carries format qualifiers.</summary>
    public static string LeafOf(string relative)
    {
        var separator = relative.LastIndexOf('\\');
        return separator < 0 ? relative : relative[(separator + 1)..];
    }

    private static IEnumerable<string> RawLines() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "corpus", Fixture));

    private static int Number(string value) =>
        int.Parse(value.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
}

/// <summary>
/// A filesystem that exists only in memory. Everything above <see cref="IFileSystemReader"/> is
/// pure, so the whole classifier runs against the real 22,050-file drive with no disk involved.
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
