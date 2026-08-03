namespace ARK.Core.Hashing;

/// <summary>
/// A ROM's on-disk format, detected from its contents.
/// </summary>
/// <remarks>
/// Detected from magic bytes, never from the extension. Extensions lie — a byteswapped N64 dump
/// is routinely shipped as <c>.z64</c> — and magic bytes do not.
/// </remarks>
public enum RomFormat
{
    /// <summary>Nothing recognizable. Hash the bytes as they are.</summary>
    Unknown = 0,

    /// <summary>N64, big-endian. Magic <c>80 37 12 40</c>. The identity form.</summary>
    N64BigEndian,

    /// <summary>N64, byte-swapped pairs. Magic <c>37 80 40 12</c>.</summary>
    N64ByteSwapped,

    /// <summary>N64, little-endian. Magic <c>40 12 37 80</c>.</summary>
    N64LittleEndian,

    /// <summary>
    /// A 512-byte copier header is prepended. There is deliberately no "headerless" counterpart:
    /// the absence of the offset is not evidence of anything, since almost every ROM of every
    /// system is a whole number of KiB.
    /// </summary>
    SnesHeadered,

    /// <summary>NES with an iNES 1.0 16-byte header.</summary>
    NesINes,

    /// <summary>NES with a NES 2.0 16-byte header.</summary>
    NesNes20,
}

/// <summary>What a detected format says about the file, and which qualifier it corresponds to.</summary>
/// <param name="Format">The detected format.</param>
/// <param name="Qualifier">
/// The DAT format qualifier this corresponds to, or null when the format implies none. This is
/// what lets a detected format be compared against the folder's declared qualifier.
/// </param>
/// <param name="Detail">Human-readable evidence, for the report.</param>
public sealed record RomFormatDetection(RomFormat Format, string? Qualifier, string Detail)
{
    /// <summary>Nothing recognized.</summary>
    public static RomFormatDetection None { get; } = new(RomFormat.Unknown, null, "no recognized format signature");
}
