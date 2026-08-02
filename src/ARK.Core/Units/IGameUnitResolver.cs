using ARK.Core.Scanning;

namespace ARK.Core.Units;

/// <summary>
/// Groups the files of one directory into <see cref="GameUnit"/>s.
/// </summary>
/// <remarks>
/// Resolution is per-directory rather than per-file because a unit is not always one file: a disc
/// unit is a CUE plus every BIN it references, grouped with its disc siblings, and that grouping
/// is only visible with the whole listing in hand.
/// </remarks>
public interface IGameUnitResolver
{
    /// <summary>The kind of unit this resolver produces.</summary>
    GameUnitKind Kind { get; }

    /// <summary>Files this resolver claims from the directory's listing.</summary>
    bool CanResolve(FileEntry file);

    /// <summary>
    /// Groups <paramref name="files"/> into units. Files this resolver does not claim are
    /// ignored, never silently consumed.
    /// </summary>
    IReadOnlyList<GameUnit> Resolve(DirectoryProfile directory, IReadOnlyList<FileEntry> files);
}
