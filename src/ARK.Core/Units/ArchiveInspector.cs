using System.Diagnostics.CodeAnalysis;
using ARK.Core.Scanning;
using SharpCompress.Archives;

namespace ARK.Core.Units;

/// <summary>
/// Reads archive entry lists via SharpCompress. Entries only: the stream is opened, the table of
/// contents is enumerated, and nothing is written anywhere.
/// </summary>
public sealed class ArchiveInspector : IArchiveInspector
{
    private readonly IFileSystemReader _reader;
    private readonly HashSet<string> _extensions;

    /// <summary>Creates an inspector for the supplied archive extensions.</summary>
    public ArchiveInspector(IFileSystemReader reader, IEnumerable<string>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _extensions = new HashSet<string>(
            extensions ?? new[] { ".zip", ".7z", ".rar" },
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool Handles(string extension) => _extensions.Contains(extension);

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A corrupt or unsupported archive is reported as an anomaly, never allowed to abort a scan.")]
    public bool TryReadEntries(string path, out IReadOnlyList<ArchiveEntry> entries, out string? error)
    {
        try
        {
            using var stream = _reader.OpenRead(path);
            using var archive = ArchiveFactory.Open(stream);

            entries = archive.Entries
                .Where(entry => !entry.IsDirectory)
                .Select(entry => new ArchiveEntry(entry.Key ?? string.Empty, entry.Size))
                .ToArray();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            entries = Array.Empty<ArchiveEntry>();
            error = ex.Message;
            return false;
        }
    }
}
