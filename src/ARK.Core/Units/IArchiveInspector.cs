namespace ARK.Core.Units;

/// <summary>
/// Reads an archive's entry list. Reading only — there is no extraction method here, and there
/// will not be one.
/// </summary>
/// <remarks>
/// SharpCompress's whole-archive extraction method is banned outright: it carries an unpatched
/// zip-slip path traversal (GHSA-6c8g-7p36-r338) that escalates to arbitrary file writes on TAR
/// via symlink chaining, and every release through 0.47.4 is affected so there is no version bump
/// to take. ARK does not need it — scanning reads the entry list and hashing streams one entry.
/// If extraction ever becomes a verb it is hand-rolled with per-entry path validation against the
/// target root. An architecture test enforces that the method is never referenced, including from
/// comments, which is why it is described here rather than named.
/// </remarks>
public interface IArchiveInspector
{
    /// <summary>True when this inspector handles the given extension.</summary>
    bool Handles(string extension);

    /// <summary>
    /// Lists entries without extracting. Returns <c>false</c> when the archive cannot be read,
    /// so a corrupt file becomes a reported anomaly rather than a thrown scan.
    /// </summary>
    bool TryReadEntries(string path, out IReadOnlyList<ArchiveEntry> entries, out string? error);

    /// <summary>
    /// Opens the archive's single entry for reading.
    /// </summary>
    /// <remarks>
    /// Deliberately restricted to archives holding exactly one entry. Hashing must never guess
    /// which entry is the game, and an archive holding more than one is already reported — either
    /// as an unsupported format or as an anomaly — before it reaches here.
    /// </remarks>
    /// <param name="path">Archive path.</param>
    /// <param name="stream">The entry's decompressed content stream. The caller disposes it.</param>
    /// <param name="size">Uncompressed length of the entry, or 0 when the format does not report it.</param>
    /// <param name="error">Why it could not be opened.</param>
    bool TryOpenSingleEntry(string path, out Stream stream, out long size, out string? error);

    /// <summary>
    /// Opens one named entry for reading.
    /// </summary>
    /// <remarks>
    /// Needed to read a cue sheet out of an archive without extracting anything: the sheet is the
    /// manifest that says which other entries belong to the same disc, so it has to be read before
    /// the unit's membership is known. Still reading only — nothing is written anywhere.
    /// </remarks>
    /// <param name="path">Archive path.</param>
    /// <param name="entryName">Entry to open, matched case-insensitively.</param>
    /// <param name="stream">The entry's decompressed content stream. The caller disposes it.</param>
    /// <param name="error">Why it could not be opened.</param>
    bool TryOpenEntry(string path, string entryName, out Stream stream, out string? error);
}
