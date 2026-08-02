using ARK.Core.Naming;
using ARK.Core.Scanning;

namespace ARK.Core.Units;

/// <summary>
/// Resolves cartridge units: one archive containing one ROM, or one bare ROM file.
/// </summary>
/// <remarks>
/// <para>
/// Archives are the normal case, not the exception — the reference corpus is 9,928 of 9,928
/// <c>.zip</c>. The parseable name is the <b>archive's</b>; identity hashing later reads the ROM
/// inside, because zip compression is not deterministic and hashing the archive itself can never
/// match a DAT.
/// </para>
/// <para>
/// An archive holding more than one ROM is an anomaly. It is reported, never resolved by picking
/// whichever entry looks most like the game.
/// </para>
/// </remarks>
public sealed class CartridgeUnitResolver : IGameUnitResolver
{
    private const string MultipleRomsCode = "multiple-roms-in-archive";
    private const string UnreadableArchiveCode = "unreadable-archive";
    private const string EmptyArchiveCode = "empty-archive";
    private const string UnparseableNameCode = "unparseable-name";
    private const string DiscImageCode = "disc-image";
    private const string PackagedContentCode = "packaged-content";

    private static readonly string[] DefaultDiscDescriptors = { ".cue", ".gdi", ".ccd", ".toc", ".m3u" };

    private readonly NameTokenizer _tokenizer;
    private readonly IArchiveInspector _inspector;
    private readonly HashSet<string> _discDescriptors;

    /// <summary>Creates a cartridge resolver.</summary>
    /// <param name="tokenizer">Parses the archive's name.</param>
    /// <param name="inspector">Reads archive entry lists without extracting.</param>
    /// <param name="discDescriptorExtensions">
    /// Extensions that mark an archive as a disc image rather than a cartridge — a track sheet
    /// naming the data files beside it. Their presence is what separates "this is a disc, and the
    /// disc resolver is stubbed" from "this archive holds two unrelated games".
    /// </param>
    public CartridgeUnitResolver(
        NameTokenizer tokenizer,
        IArchiveInspector inspector,
        IEnumerable<string>? discDescriptorExtensions = null)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(inspector);
        _tokenizer = tokenizer;
        _inspector = inspector;
        _discDescriptors = new HashSet<string>(
            discDescriptorExtensions is { } supplied && supplied.Any() ? supplied : DefaultDiscDescriptors,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public GameUnitKind Kind => GameUnitKind.Cartridge;

    /// <inheritdoc />
    public bool CanResolve(FileEntry file) => file is not null;

    /// <inheritdoc />
    public IReadOnlyList<GameUnit> Resolve(DirectoryProfile directory, IReadOnlyList<FileEntry> files)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(files);

        return files.Where(CanResolve).Select(file => ResolveOne(directory, file)).ToArray();
    }

    private GameUnit ResolveOne(DirectoryProfile directory, FileEntry file)
    {
        var issues = new List<GameUnitIssue>();
        IReadOnlyList<ArchiveEntry> contents = Array.Empty<ArchiveEntry>();

        if (_inspector.Handles(file.Extension))
        {
            if (_inspector.TryReadEntries(file.FullPath, out var entries, out var error))
            {
                contents = entries;
                if (entries.Count == 0)
                {
                    issues.Add(new GameUnitIssue(UnitIssueKind.Anomaly, EmptyArchiveCode, "archive contains no files"));
                }
                else if (entries.Count > 1)
                {
                    issues.Add(Classify(entries));
                }
            }
            else
            {
                // A true finding: this caught five corrupt or truncated downloads on the
                // reference drive, which is exactly the value a scan should deliver.
                issues.Add(new GameUnitIssue(UnitIssueKind.Anomaly, UnreadableArchiveCode, error ?? "archive could not be read"));
            }
        }

        var parsed = _tokenizer.Parse(file.NameWithoutExtension);
        if (!parsed.IsTokenizable)
        {
            issues.Add(new GameUnitIssue(UnitIssueKind.Anomaly, UnparseableNameCode, $"name flagged {parsed.Flag}"));
        }

        return new GameUnit(
            GameUnitKind.Cartridge,
            file.FullPath,
            new[] { file },
            parsed,
            directory.Name,
            directory.FormatQualifiers,
            contents,
            issues);
    }

    // More than one entry does not mean more than one game, and treating it that way is what
    // buried the real findings. This resolver's actual claim is narrow — "an archive holding one
    // ROM" — so anything else is only an anomaly when there is positive evidence of two distinct
    // games. Otherwise it is a structure no resolver handles yet.
    private GameUnitIssue Classify(IReadOnlyList<ArchiveEntry> entries)
    {
        var descriptors = entries
            .Where(entry => _discDescriptors.Contains(ExtensionOf(entry.Name)))
            .ToArray();

        if (descriptors.Length == 1)
        {
            return new GameUnitIssue(
                UnitIssueKind.UnsupportedFormat,
                DiscImageCode,
                $"disc image ({descriptors[0].Name} + {entries.Count - 1} track file(s)) — the disc resolver is not implemented yet");
        }

        if (descriptors.Length > 1)
        {
            // Several track sheets: more than one disc in one archive. Ambiguous, so it is
            // reported rather than resolved by picking one.
            return new GameUnitIssue(UnitIssueKind.Anomaly, MultipleRomsCode, Describe(entries));
        }

        // Entries nested under directories are a packaged layout — a PSN content package carries
        // its files under a title-ID folder. One distributable item, several files, no resolver.
        if (entries.Any(entry => entry.Name.Contains('/', StringComparison.Ordinal)
            || entry.Name.Contains('\\', StringComparison.Ordinal)))
        {
            return new GameUnitIssue(
                UnitIssueKind.UnsupportedFormat,
                PackagedContentCode,
                $"packaged content ({entries.Count} files under a directory layout) — no resolver handles this shape yet");
        }

        // Flat, unnested, several files: genuinely more than one thing side by side.
        return new GameUnitIssue(UnitIssueKind.Anomaly, MultipleRomsCode, Describe(entries));
    }

    private static string Describe(IReadOnlyList<ArchiveEntry> entries) =>
        $"archive contains {entries.Count} entries: {string.Join(", ", entries.Take(4).Select(entry => entry.Name))}";

    private static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[dot..] : string.Empty;
    }
}
