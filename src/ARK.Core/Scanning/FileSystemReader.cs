using System.Diagnostics.CodeAnalysis;

namespace ARK.Core.Scanning;

/// <summary>
/// The real filesystem. This is the <b>only</b> production type that touches
/// <see cref="FileInfo"/> or <see cref="DirectoryInfo"/>, enforced by an architecture test, so
/// every other type in the scan pipeline is pure and testable without a disk.
/// </summary>
/// <remarks>
/// Read-only by construction: it exposes enumeration and <see cref="OpenRead"/> and nothing else.
/// A scan leaves the tree byte-identical and writes no journal, because there is no write path
/// here to reach.
/// </remarks>
public sealed class FileSystemReader : IFileSystemReader
{
    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A single unreadable directory must not abort a scan of the rest of the drive.")]
    public IEnumerable<DirectoryListing> EnumerateDirectories(string root)
    {
        var start = new DirectoryInfo(root);
        if (!start.Exists)
        {
            yield break;
        }

        var pending = new Stack<DirectoryInfo>();
        pending.Push(start);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            FileInfo[] files;
            DirectoryInfo[] children;
            try
            {
                files = current.GetFiles();
                children = current.GetDirectories();
            }
            catch (Exception)
            {
                // Permission denied, a vanished folder, a broken reparse point: skip it and keep going.
                continue;
            }

            foreach (var child in children)
            {
                pending.Push(child);
            }

            yield return new DirectoryListing(
                current.FullName,
                current.Name,
                files.Select(Describe).ToArray(),
                children.Length);
        }
    }

    /// <inheritdoc />
    public Stream OpenRead(string path) => File.OpenRead(path);

    /// <inheritdoc />
    public FileEntry? Describe(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? Describe(file) : null;
    }

    private static FileEntry Describe(FileInfo file) => new(
        file.FullName,
        file.Name,
        file.Extension.ToLowerInvariant(),
        SafeLength(file),
        SafeModified(file));

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Metadata for a file that vanished mid-scan is reported as zero, not fatal.")]
    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Metadata for a file that vanished mid-scan is reported as default, not fatal.")]
    private static DateTimeOffset SafeModified(FileInfo file)
    {
        try
        {
            return file.LastWriteTimeUtc;
        }
        catch (Exception)
        {
            return default;
        }
    }
}
