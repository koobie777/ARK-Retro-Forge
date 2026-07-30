using System.IO.Compression;

namespace ARK.Core.Dat;

/// <summary>
/// Reads one or more Logiqx DATs from a stream. A ZIP payload (Datomatic's "Daily" pack) yields every
/// <c>.dat</c> entry; anything else is parsed as a single DAT. Entries are parsed from their streams
/// in memory — nothing is ever extracted to disk, so there is no path-traversal surface.
/// </summary>
internal static class DatReader
{
    public static IReadOnlyList<(string? EntryName, LogiqxDat Dat)> ReadDats(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var seekable = EnsureSeekable(content);
        try
        {
            if (LooksLikeZip(seekable))
            {
                return ReadFromZip(seekable);
            }

            seekable.Position = 0;
            return [(null, LogiqxParser.Parse(seekable))];
        }
        finally
        {
            if (!ReferenceEquals(seekable, content))
            {
                seekable.Dispose();
            }
        }
    }

    private static IReadOnlyList<(string? EntryName, LogiqxDat Dat)> ReadFromZip(Stream seekable)
    {
        seekable.Position = 0;
        using var archive = new ZipArchive(seekable, ZipArchiveMode.Read, leaveOpen: true);

        var results = new List<(string?, LogiqxDat)>();
        foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            if (!entry.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var entryStream = entry.Open();
            results.Add((entry.Name, LogiqxParser.Parse(entryStream)));
        }

        return results;
    }

    private static bool LooksLikeZip(Stream seekable)
    {
        seekable.Position = 0;
        Span<byte> header = stackalloc byte[2];
        var read = seekable.Read(header);
        seekable.Position = 0;
        return read == 2 && header[0] == (byte)'P' && header[1] == (byte)'K';
    }

    private static Stream EnsureSeekable(Stream content)
    {
        if (content.CanSeek)
        {
            return content;
        }

        var memory = new MemoryStream();
        content.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }
}
