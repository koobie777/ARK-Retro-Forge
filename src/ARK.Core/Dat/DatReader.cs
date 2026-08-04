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

            // Rejected before parsing, because the parser's own error does not describe what
            // happened. Redump serves its ordinary HTML page for systems that have no DAT
            // published — 21 of its 79 sources on a live run — and "Reference to undeclared entity
            // 'bull'" tells a user nothing about a site that simply has nothing to offer yet.
            if (LooksLikeHtml(seekable))
            {
                throw new InvalidDataException(
                    "the response is an HTML page, not a DAT — the source most likely publishes no DAT for this system");
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

    private static bool LooksLikeHtml(Stream seekable)
    {
        seekable.Position = 0;
        Span<byte> header = stackalloc byte[64];
        var read = seekable.Read(header);
        seekable.Position = 0;

        if (read <= 0)
        {
            return false;
        }

        var text = System.Text.Encoding.ASCII.GetString(header[..read]).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
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
