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

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A corrupt or unsupported archive is reported, never allowed to abort a verification run.")]
    public bool TryOpenSingleEntry(string path, out Stream stream, out long size, out string? error)
    {
        Stream? file = null;
        IArchive? archive = null;

        try
        {
            file = _reader.OpenRead(path);
            archive = ArchiveFactory.Open(file);

            var entries = archive.Entries.Where(entry => !entry.IsDirectory).Take(2).ToArray();
            if (entries.Length != 1)
            {
                stream = Stream.Null;
                size = 0;
                error = entries.Length == 0 ? "archive contains no files" : "archive contains more than one entry";
                archive.Dispose();
                file.Dispose();
                return false;
            }

            size = entries[0].Size;

            // The entry stream borrows the archive and the file handle, so disposing it must
            // dispose both. Nothing is written anywhere along this path.
            stream = new EntryStream(entries[0].OpenEntryStream(), archive, file);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            archive?.Dispose();
            file?.Dispose();
            stream = Stream.Null;
            size = 0;
            error = ex.Message;
            return false;
        }
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A corrupt or unsupported archive is reported, never allowed to abort a scan.")]
    public bool TryOpenEntry(string path, string entryName, out Stream stream, out string? error)
    {
        Stream? file = null;
        IArchive? archive = null;

        try
        {
            file = _reader.OpenRead(path);
            archive = ArchiveFactory.Open(file);

            var entry = archive.Entries.FirstOrDefault(candidate =>
                !candidate.IsDirectory &&
                string.Equals(candidate.Key, entryName, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                stream = Stream.Null;
                error = $"entry '{entryName}' not found";
                archive.Dispose();
                file.Dispose();
                return false;
            }

            stream = new EntryStream(entry.OpenEntryStream(), archive, file);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            archive?.Dispose();
            file?.Dispose();
            stream = Stream.Null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Read-only view over one archive entry that owns the archive and file handles.</summary>
    private sealed class EntryStream : Stream
    {
        private readonly Stream _inner;
        private readonly IArchive _archive;
        private readonly Stream _file;

        public EntryStream(Stream inner, IArchive archive, Stream file)
        {
            _inner = inner;
            _archive = archive;
            _file = file;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _archive.Dispose();
                _file.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
