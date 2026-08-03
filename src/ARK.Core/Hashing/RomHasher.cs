using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using ARK.Core.Scanning;
using ARK.Core.Units;

namespace ARK.Core.Hashing;

/// <summary>
/// Hashes the ROM a file represents — the entry inside the archive, not the archive itself.
/// </summary>
/// <remarks>
/// <para>
/// The reference collection is 100% <c>.zip</c> and DAT hashes describe the ROM inside. Zip
/// compression is not deterministic, so hashing the archive can never match a DAT no matter how
/// correct the file is. The entry is streamed and hashed; nothing is ever extracted.
/// </para>
/// <para>
/// The file's timestamp is captured before the read and re-checked after. A file that changed
/// while it was being read was being written during the read, and the hash describes bytes that
/// no longer exist — that result is discarded rather than reported as a mismatch.
/// </para>
/// </remarks>
public sealed class RomHasher
{
    private const int BufferSize = 1 << 20;

    private readonly IFileSystemReader _reader;
    private readonly IArchiveInspector _inspector;

    /// <summary>Creates a hasher.</summary>
    public RomHasher(IFileSystemReader reader, IArchiveInspector inspector)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(inspector);
        _reader = reader;
        _inspector = inspector;
    }

    /// <summary>
    /// Hashes one file's ROM content.
    /// </summary>
    /// <param name="file">The file to hash.</param>
    /// <param name="purpose">How much hashing is enough — see <see cref="HashPurpose"/>.</param>
    /// <param name="normalize">
    /// Whether to normalize the bytes in memory before hashing. Left off by default: matching the
    /// DAT variant the file already is costs nothing, and this is the fallback for when no variant
    /// matches. Even when on, it rewrites nothing on disk.
    /// </param>
    /// <param name="cancellationToken">Cancels a long read.</param>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "One unreadable file is a reported finding, never allowed to abort a run of thousands.")]
    public RomHashResult Compute(
        FileEntry file,
        HashPurpose purpose = HashPurpose.Verification,
        bool normalize = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var before = _reader.Describe(file.FullPath);
        if (before is null)
        {
            return RomHashResult.Failed(RomReadStatus.Unreadable, "file no longer exists");
        }

        Stream? stream = null;
        try
        {
            long length;
            if (_inspector.Handles(file.Extension))
            {
                if (!_inspector.TryOpenSingleEntry(file.FullPath, out stream, out length, out var error))
                {
                    var status = error is not null && error.Contains("entr", StringComparison.OrdinalIgnoreCase)
                        ? RomReadStatus.NotSingleEntry
                        : RomReadStatus.Unreadable;
                    return RomHashResult.Failed(status, error ?? "archive could not be opened");
                }
            }
            else
            {
                stream = _reader.OpenRead(file.FullPath);
                length = before.Size;
            }

            var hash = Hash(stream, length, purpose, normalize, cancellationToken);

            // Re-read metadata: if it moved, something was writing this file as we read it.
            var after = _reader.Describe(file.FullPath);
            if (after is null || after.ModifiedUtc != before.ModifiedUtc || after.Size != before.Size)
            {
                return RomHashResult.Failed(
                    RomReadStatus.ChangedDuringRead,
                    "file changed while being read — it is being written to");
            }

            return new RomHashResult(RomReadStatus.Ok, hash, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RomHashResult.Failed(RomReadStatus.Unreadable, ex.Message);
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private static RomHash Hash(
        Stream stream,
        long length,
        HashPurpose purpose,
        bool normalize,
        CancellationToken cancellationToken)
    {
        using var crc = new Crc32Hasher();
        using var sha1 = purpose == HashPurpose.Identification ? SHA1.Create() : null;

        var buffer = new byte[BufferSize];

        // Read the first chunk, then decide everything from it plus the known length. Both the
        // magic bytes and the SNES size rule are available before a single byte is hashed, so the
        // stream is read exactly once.
        var pending = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var probe = Math.Min(pending, RomFormatDetector.ProbeSize);
        var detection = RomFormatDetector.Detect(buffer.AsSpan(0, probe), length > 0 ? length : pending);

        var skip = normalize ? RomTransform.LeadingBytesToSkip(detection.Format) : 0;
        var rewrite = normalize && RomTransform.RewritesBytes(detection.Format);
        var normalized = skip > 0 || rewrite;

        long total = 0;
        while (pending > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var offset = 0;
            if (skip > 0)
            {
                offset = Math.Min(skip, pending);
                skip -= offset;
                pending -= offset;
            }

            if (pending > 0)
            {
                // A full buffer is always a multiple of 4, so a byte-order transform never
                // straddles a chunk boundary except on the final short read — which is the end of
                // the ROM and already aligned for any valid image.
                var span = buffer.AsSpan(offset, pending);
                if (rewrite)
                {
                    RomTransform.NormalizeChunk(span, detection.Format);
                }

                crc.Append(span);
                sha1?.TransformBlock(buffer, offset, pending, null, 0);
                total += pending;
            }

            pending = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        }

        sha1?.TransformFinalBlock([], 0, 0);

        return new RomHash(
            Convert.ToHexString(crc.GetHashAndReset()).ToLowerInvariant(),
            sha1 is null ? null : Convert.ToHexString(sha1.Hash!).ToLowerInvariant(),
            total,
            detection,
            normalized);
    }
}
