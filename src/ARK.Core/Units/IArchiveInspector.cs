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
}
