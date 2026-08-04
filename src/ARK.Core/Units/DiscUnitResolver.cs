using ARK.Core.Naming;
using ARK.Core.Scanning;

namespace ARK.Core.Units;

/// <summary>
/// Resolves disc units: an archive holding a disc image, a bare ISO, or a loose CUE with the BINs
/// it names.
/// </summary>
/// <remarks>
/// <para>
/// <b>The three "multi" cases share no code path here.</b> A cue sheet with twelve TRACK entries
/// is one disc whose audio is split across twelve files — one unit. <c>(Disc 1)</c> and
/// <c>(Disc 2)</c> are two units belonging to one set, and are never merged. A single BIN or ISO
/// is one unit. Conflating any two of those is what produced v1's damage.
/// </para>
/// <para>
/// <b>No cue sheet is written, ever.</b> Redump DATs hash every constituent file including the
/// cue, so one that matches its DAT hash is provably correct. One that does not is reported, not
/// repaired — this resolver reads sheets and never emits them.
/// </para>
/// </remarks>
public sealed class DiscUnitResolver : IGameUnitResolver
{
    private const string IncompleteCode = "incomplete-disc-unit";
    private const string UnreadableCueCode = "unreadable-cue";
    private const string UnsupportedDescriptorCode = "unsupported-disc-descriptor";
    private const string UnparseableNameCode = "unparseable-name";

    // Deliberately excludes '.bin'. A BIN named by a cue is claimed through that cue, which is the
    // only membership evidence worth trusting. A bare BIN with no cue anywhere is far more likely a
    // Mega Drive ROM than an orphaned disc track, and claiming it here would file the entire Genesis
    // library as discs.
    private static readonly string[] DefaultImageExtensions = { ".iso", ".img" };
    private static readonly string[] DefaultUnsupportedDescriptors = { ".gdi", ".ccd", ".toc", ".m3u" };

    private readonly NameTokenizer _tokenizer;
    private readonly IArchiveInspector _inspector;
    private readonly IFileSystemReader _reader;
    private readonly HashSet<string> _imageExtensions;
    private readonly HashSet<string> _unsupportedDescriptors;

    /// <summary>Creates a disc resolver.</summary>
    /// <param name="tokenizer">Parses the unit's name.</param>
    /// <param name="inspector">Reads archive entry lists and opens the cue inside one.</param>
    /// <param name="reader">Opens loose cue sheets that are not inside an archive.</param>
    /// <param name="imageExtensions">Extensions that count as a single-file disc image.</param>
    /// <param name="unsupportedDescriptors">
    /// Descriptor formats recognized but not parsed. Reported as such rather than silently
    /// mis-resolved, because guessing at a layout is exactly the failure this phase exists to
    /// avoid.
    /// </param>
    public DiscUnitResolver(
        NameTokenizer tokenizer,
        IArchiveInspector inspector,
        IFileSystemReader reader,
        IEnumerable<string>? imageExtensions = null,
        IEnumerable<string>? unsupportedDescriptors = null)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(reader);

        _tokenizer = tokenizer;
        _inspector = inspector;
        _reader = reader;
        _imageExtensions = new HashSet<string>(imageExtensions ?? DefaultImageExtensions, StringComparer.OrdinalIgnoreCase);
        _unsupportedDescriptors = new HashSet<string>(
            unsupportedDescriptors ?? DefaultUnsupportedDescriptors, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public GameUnitKind Kind => GameUnitKind.Disc;

    /// <inheritdoc />
    public bool CanResolve(FileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return IsCue(file.Extension)
            || _unsupportedDescriptors.Contains(file.Extension)
            || _imageExtensions.Contains(file.Extension)
            || _inspector.Handles(file.Extension);
    }

    /// <inheritdoc />
    public IReadOnlyList<GameUnit> Resolve(DirectoryProfile directory, IReadOnlyList<FileEntry> files)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(files);

        var units = new List<GameUnit>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byName = files.ToDictionary(file => file.Name, StringComparer.OrdinalIgnoreCase);

        // Loose cue sheets first: each one claims the files it names, so a BIN can never be picked
        // up as a unit in its own right afterwards.
        foreach (var cue in files.Where(file => IsCue(file.Extension)))
        {
            units.Add(ResolveLooseCue(directory, cue, byName, claimed));
        }

        foreach (var file in files)
        {
            if (claimed.Contains(file.FullPath))
            {
                continue;
            }

            if (_unsupportedDescriptors.Contains(file.Extension))
            {
                claimed.Add(file.FullPath);
                units.Add(Unit(directory, file, [file], [],
                    new GameUnitIssue(UnitIssueKind.UnsupportedFormat, UnsupportedDescriptorCode,
                        $"'{file.Extension}' is a recognized disc descriptor that this resolver does not parse — reported rather than guessed at")));
                continue;
            }

            if (_inspector.Handles(file.Extension))
            {
                var archived = ResolveArchive(directory, file);
                if (archived is not null)
                {
                    claimed.Add(file.FullPath);
                    units.Add(archived);
                }

                continue;
            }

            if (_imageExtensions.Contains(file.Extension))
            {
                claimed.Add(file.FullPath);
                units.Add(Unit(directory, file, [file], []));
            }
        }

        return units;
    }

    /// <summary>
    /// A loose cue on disk, plus the files it names.
    /// </summary>
    private GameUnit ResolveLooseCue(
        DirectoryProfile directory,
        FileEntry cue,
        IReadOnlyDictionary<string, FileEntry> byName,
        HashSet<string> claimed)
    {
        claimed.Add(cue.FullPath);

        CueSheet sheet;
        try
        {
            using var stream = _reader.OpenRead(cue.FullPath);
            sheet = CueSheet.Parse(stream);
        }
        catch (IOException ex)
        {
            return Unit(directory, cue, [cue], [],
                new GameUnitIssue(UnitIssueKind.Anomaly, UnreadableCueCode, $"cue sheet could not be read: {ex.Message}"));
        }

        var members = new List<FileEntry> { cue };
        var missing = new List<string>();

        foreach (var declared in sheet.Files)
        {
            if (byName.TryGetValue(declared.Name, out var member))
            {
                members.Add(member);
                claimed.Add(member.FullPath);
            }
            else
            {
                missing.Add(declared.Name);
            }
        }

        return Unit(directory, cue, members, [], IncompleteIssue(missing, sheet));
    }

    /// <summary>
    /// An archive holding a disc image. The cue inside is the manifest, so it is read before the
    /// unit's membership is decided — never inferred from which entries look related.
    /// </summary>
    private GameUnit? ResolveArchive(DirectoryProfile directory, FileEntry file)
    {
        if (!_inspector.TryReadEntries(file.FullPath, out var entries, out _))
        {
            // Unreadable archives are the cartridge resolver's finding to report; leaving it
            // unclaimed keeps a single owner for that message.
            return null;
        }

        var cueEntry = entries.FirstOrDefault(entry => IsCue(ExtensionOf(entry.Name)));

        if (cueEntry is null)
        {
            var descriptor = entries.FirstOrDefault(entry => _unsupportedDescriptors.Contains(ExtensionOf(entry.Name)));
            if (descriptor is not null)
            {
                return Unit(directory, file, [file], entries,
                    new GameUnitIssue(UnitIssueKind.UnsupportedFormat, UnsupportedDescriptorCode,
                        $"archive holds '{descriptor.Name}', a disc descriptor this resolver does not parse"));
            }

            // A lone image inside an archive is still a disc, just one without a track sheet.
            var image = entries.SingleOrDefault(entry => _imageExtensions.Contains(ExtensionOf(entry.Name)));
            return entries.Count == 1 && image is not null
                ? Unit(directory, file, [file], entries)
                : null;
        }

        // Several cue sheets means several discs in one archive: ambiguous, and resolving it would
        // mean choosing one, which is a guess.
        if (entries.Count(entry => IsCue(ExtensionOf(entry.Name))) > 1)
        {
            return Unit(directory, file, [file], entries,
                new GameUnitIssue(UnitIssueKind.Anomaly, IncompleteCode,
                    "archive holds more than one cue sheet — that is more than one disc, and picking one would be a guess"));
        }

        CueSheet sheet;
        if (_inspector.TryOpenEntry(file.FullPath, cueEntry.Name, out var stream, out var error))
        {
            using (stream)
            {
                sheet = CueSheet.Parse(stream);
            }
        }
        else
        {
            return Unit(directory, file, [file], entries,
                new GameUnitIssue(UnitIssueKind.Anomaly, UnreadableCueCode, error ?? "cue sheet could not be read"));
        }

        var present = entries.Select(entry => entry.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = sheet.Files.Where(declared => !present.Contains(declared.Name)).Select(declared => declared.Name).ToList();

        // The archive is one unit whatever the track count: the whole disc travels together.
        return Unit(directory, file, [file], entries, IncompleteIssue(missing, sheet));
    }

    private static GameUnitIssue? IncompleteIssue(IReadOnlyList<string> missing, CueSheet sheet) =>
        missing.Count == 0
            ? null
            : new GameUnitIssue(UnitIssueKind.Anomaly, IncompleteCode,
                $"cue sheet names {sheet.Files.Count} file(s), {missing.Count} of which are absent: {string.Join(", ", missing.Take(4))}");

    private GameUnit Unit(
        DirectoryProfile directory,
        FileEntry primary,
        IReadOnlyList<FileEntry> files,
        IReadOnlyList<ArchiveEntry> contents,
        GameUnitIssue? issue = null)
    {
        var parsed = _tokenizer.Parse(primary.NameWithoutExtension);
        var issues = new List<GameUnitIssue>();

        if (issue is not null)
        {
            issues.Add(issue);
        }

        if (!parsed.IsTokenizable)
        {
            issues.Add(new GameUnitIssue(UnitIssueKind.Anomaly, UnparseableNameCode, $"name flagged {parsed.Flag}"));
        }

        return new GameUnit(
            GameUnitKind.Disc,
            primary.FullPath,
            files,
            parsed,
            directory.Name,
            directory.FormatQualifiers,
            contents,
            issues);
    }

    private static bool IsCue(string extension) => extension.Equals(".cue", StringComparison.OrdinalIgnoreCase);

    private static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[dot..] : string.Empty;
    }
}
