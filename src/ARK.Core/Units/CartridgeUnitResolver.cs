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

    private readonly NameTokenizer _tokenizer;
    private readonly IArchiveInspector _inspector;

    /// <summary>Creates a cartridge resolver.</summary>
    public CartridgeUnitResolver(NameTokenizer tokenizer, IArchiveInspector inspector)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(inspector);
        _tokenizer = tokenizer;
        _inspector = inspector;
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
        var anomalies = new List<GameUnitAnomaly>();
        IReadOnlyList<ArchiveEntry> contents = Array.Empty<ArchiveEntry>();

        if (_inspector.Handles(file.Extension))
        {
            if (_inspector.TryReadEntries(file.FullPath, out var entries, out var error))
            {
                contents = entries;
                if (entries.Count == 0)
                {
                    anomalies.Add(new GameUnitAnomaly(EmptyArchiveCode, "archive contains no files"));
                }
                else if (entries.Count > 1)
                {
                    anomalies.Add(new GameUnitAnomaly(
                        MultipleRomsCode,
                        $"archive contains {entries.Count} entries: {string.Join(", ", entries.Take(4).Select(entry => entry.Name))}"));
                }
            }
            else
            {
                anomalies.Add(new GameUnitAnomaly(UnreadableArchiveCode, error ?? "archive could not be read"));
            }
        }

        var parsed = _tokenizer.Parse(file.NameWithoutExtension);
        if (!parsed.IsTokenizable)
        {
            anomalies.Add(new GameUnitAnomaly(UnparseableNameCode, $"name flagged {parsed.Flag}"));
        }

        return new GameUnit(
            GameUnitKind.Cartridge,
            file.FullPath,
            new[] { file },
            parsed,
            directory.Name,
            directory.FormatQualifiers,
            contents,
            anomalies);
    }
}
