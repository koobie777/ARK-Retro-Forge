namespace ARK.Core.Scanning;

/// <summary>
/// Read-only access to the filesystem. Scanning never writes, so this interface offers no way
/// to: the type system carries the read-only guarantee rather than a convention doing it.
/// </summary>
/// <remarks>
/// Everything above this interface works on <see cref="DirectoryListing"/> and
/// <see cref="FileEntry"/> values, which is what lets the whole classifier be tested against a
/// 22,050-entry reference drive with no disk involved.
/// </remarks>
public interface IFileSystemReader
{
    /// <summary>
    /// Lists <paramref name="root"/> and every directory beneath it, one entry each. Directories
    /// that cannot be read are skipped rather than throwing — an unreadable folder on a mixed
    /// drive must not abort a scan of the other 22,000 files.
    /// </summary>
    IEnumerable<DirectoryListing> EnumerateDirectories(string root);

    /// <summary>Opens a file for reading. Used for archive entry lists, never for extraction.</summary>
    Stream OpenRead(string path);

    /// <summary>
    /// Current metadata for one file, or null when it is gone.
    /// </summary>
    /// <remarks>
    /// Needed to re-read a file's timestamp after hashing it. A file whose mtime moved while it
    /// was being read was being written during the read, and its hash describes bytes that no
    /// longer exist.
    /// </remarks>
    FileEntry? Describe(string path);
}
