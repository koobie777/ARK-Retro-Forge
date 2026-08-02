using ARK.Core.Scanning;

namespace ARK.Core.Units;

/// <summary>
/// Disc units — a CUE plus every BIN it references, grouped with its disc siblings.
/// <b>Interface satisfied, implementation deliberately stubbed.</b>
/// </summary>
/// <remarks>
/// <para>
/// v2 does cartridge systems first and discs second: the cartridge logic is the same problem
/// without the disc-structure landmines, and it carries over. This type exists now so that the
/// contract downstream code is written against is the real one. When the implementation lands it
/// is this class that changes, not <see cref="GameUnit"/> and not anything that consumes it.
/// </para>
/// <para>
/// It claims no files, so a disc directory scanned today produces cartridge units — one per file,
/// which is honest — rather than silently mis-grouped disc sets.
/// </para>
/// </remarks>
public sealed class DiscUnitResolver : IGameUnitResolver
{
    /// <inheritdoc />
    public GameUnitKind Kind => GameUnitKind.Disc;

    /// <summary>Claims nothing: multi-track grouping is not implemented in this phase.</summary>
    public bool CanResolve(FileEntry file) => false;

    /// <summary>Returns no units. Grouping CUE/BIN sets is a later phase.</summary>
    public IReadOnlyList<GameUnit> Resolve(DirectoryProfile directory, IReadOnlyList<FileEntry> files) =>
        Array.Empty<GameUnit>();
}
