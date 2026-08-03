namespace ARK.Core.Hashing;

/// <summary>
/// Identifies a ROM's format from its leading bytes and size.
/// </summary>
/// <remarks>
/// <para>
/// Detection is from <b>content</b>, never from the extension. A byteswapped N64 dump named
/// <c>.z64</c> is common, and trusting the name produces a hash that matches nothing.
/// </para>
/// <para>
/// Detecting the format does not imply changing the file. The preferred resolution is to match
/// the DAT variant the file already is — Phase 2.1 keyed the catalog by qualifier precisely so a
/// headered NES ROM can match the Headered DAT untouched. Transformation is the fallback, and it
/// happens in memory.
/// </para>
/// </remarks>
public static class RomFormatDetector
{
    /// <summary>Bytes needed to reach a verdict.</summary>
    public const int ProbeSize = 16;

    private static readonly byte[] N64BigEndian = { 0x80, 0x37, 0x12, 0x40 };
    private static readonly byte[] N64ByteSwapped = { 0x37, 0x80, 0x40, 0x12 };
    private static readonly byte[] N64LittleEndian = { 0x40, 0x12, 0x37, 0x80 };
    private static readonly byte[] NesMagic = { 0x4E, 0x45, 0x53, 0x1A }; // "NES\x1A"

    /// <summary>
    /// Detects the format of a ROM from its first bytes and total length.
    /// </summary>
    /// <param name="head">At least <see cref="ProbeSize"/> bytes from the start, or fewer if the file is shorter.</param>
    /// <param name="length">Total length of the ROM in bytes.</param>
    public static RomFormatDetection Detect(ReadOnlySpan<byte> head, long length)
    {
        if (head.Length >= 4)
        {
            if (head[..4].SequenceEqual(N64BigEndian))
            {
                return new RomFormatDetection(RomFormat.N64BigEndian, "BigEndian", "N64 magic 80 37 12 40");
            }

            if (head[..4].SequenceEqual(N64ByteSwapped))
            {
                return new RomFormatDetection(RomFormat.N64ByteSwapped, "ByteSwapped", "N64 magic 37 80 40 12");
            }

            if (head[..4].SequenceEqual(N64LittleEndian))
            {
                return new RomFormatDetection(RomFormat.N64LittleEndian, "LittleEndian", "N64 magic 40 12 37 80");
            }

            if (head[..4].SequenceEqual(NesMagic))
            {
                // iNES 1.0 vs NES 2.0: byte 7 bits 2-3 are 0b10 for NES 2.0. Same ROM data either
                // way, different header, therefore a different hash.
                var nes20 = head.Length >= 8 && (head[7] & 0x0C) == 0x08;
                return nes20
                    ? new RomFormatDetection(RomFormat.NesNes20, "Headered", "NES 2.0 header (byte 7 flags)")
                    : new RomFormatDetection(RomFormat.NesINes, "Headered", "iNES 1.0 header");
            }
        }

        // An SNES copier header is 512 bytes prepended to an otherwise whole-KiB image, so the
        // tell is the size — there is no magic number to look for.
        if (length > 512 && length % 1024 == 512)
        {
            return new RomFormatDetection(RomFormat.SnesHeadered, "Headered", "512-byte copier header (size % 1024 == 512)");
        }

        // Deliberately no "headerless" verdict. The absence of a 512-byte offset is not evidence
        // of anything: almost every ROM of every system is a whole number of KiB, so claiming it
        // would label a Game Boy library as headerless SNES — which it did, for all 592 of them,
        // until this was caught by reading a real report.
        return RomFormatDetection.None;
    }

    /// <summary>
    /// True when a detected format and a folder-declared qualifier disagree. Neither side is
    /// trusted over the other — the disagreement itself is the finding, and it is reported.
    /// </summary>
    public static bool Contradicts(RomFormatDetection detection, string? folderQualifier)
    {
        ArgumentNullException.ThrowIfNull(detection);

        if (folderQualifier is null || detection.Qualifier is null || detection.Format == RomFormat.Unknown)
        {
            return false;
        }

        return !detection.Qualifier.Equals(folderQualifier, StringComparison.OrdinalIgnoreCase);
    }
}
