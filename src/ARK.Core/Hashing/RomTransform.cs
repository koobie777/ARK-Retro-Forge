namespace ARK.Core.Hashing;

/// <summary>
/// In-memory normalizations applied only when no DAT variant matches the file as it is.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here writes a file, and nothing here ever will.</b> A transform produces bytes for
/// hashing and the bytes are discarded. The user's ROM is never modified — that is not a policy
/// this phase applies, it is a capability this code does not have.
/// </para>
/// <para>
/// Transformation is the fallback, not the first move. Matching the DAT variant the file already
/// is costs nothing and risks nothing; rewriting bytes to force a match risks everything.
/// </para>
/// </remarks>
public static class RomTransform
{
    /// <summary>Size of an SNES copier header.</summary>
    public const int SnesCopierHeaderSize = 512;

    /// <summary>
    /// Removes an SNES copier header, returning the ROM body. The input is not modified.
    /// </summary>
    public static ReadOnlyMemory<byte> StripSnesCopierHeader(ReadOnlyMemory<byte> rom) =>
        rom.Length > SnesCopierHeaderSize ? rom[SnesCopierHeaderSize..] : rom;

    /// <summary>
    /// Converts an N64 image to big-endian (<c>.z64</c>), the identity form. Returns a new buffer;
    /// the input is untouched.
    /// </summary>
    public static ReadOnlyMemory<byte> ToN64BigEndian(ReadOnlyMemory<byte> rom, RomFormat format)
    {
        switch (format)
        {
            case RomFormat.N64BigEndian:
                return rom;

            case RomFormat.N64ByteSwapped:
            {
                // Swap each adjacent pair: AB CD -> BA DC.
                var swapped = rom.ToArray();
                for (var i = 0; i + 1 < swapped.Length; i += 2)
                {
                    (swapped[i], swapped[i + 1]) = (swapped[i + 1], swapped[i]);
                }

                return swapped;
            }

            case RomFormat.N64LittleEndian:
            {
                // Reverse each 4-byte word: ABCD -> DCBA.
                var reversed = rom.ToArray();
                for (var i = 0; i + 3 < reversed.Length; i += 4)
                {
                    (reversed[i], reversed[i + 3]) = (reversed[i + 3], reversed[i]);
                    (reversed[i + 1], reversed[i + 2]) = (reversed[i + 2], reversed[i + 1]);
                }

                return reversed;
            }

            default:
                return rom;
        }
    }

    /// <summary>
    /// Applies the normalization implied by <paramref name="format"/>, if any. Returns the input
    /// unchanged when no transform applies.
    /// </summary>
    public static ReadOnlyMemory<byte> Normalize(ReadOnlyMemory<byte> rom, RomFormat format) => format switch
    {
        RomFormat.SnesHeadered => StripSnesCopierHeader(rom),
        RomFormat.N64ByteSwapped or RomFormat.N64LittleEndian => ToN64BigEndian(rom, format),
        _ => rom,
    };

    /// <summary>How many leading bytes this format's normalization discards.</summary>
    public static int LeadingBytesToSkip(RomFormat format) =>
        format == RomFormat.SnesHeadered ? SnesCopierHeaderSize : 0;

    /// <summary>Whether normalizing this format rewrites bytes rather than merely skipping them.</summary>
    public static bool RewritesBytes(RomFormat format) =>
        format is RomFormat.N64ByteSwapped or RomFormat.N64LittleEndian;

    /// <summary>
    /// Byte alignment a streaming chunk must respect for <see cref="NormalizeChunk"/> to be
    /// correct across chunk boundaries.
    /// </summary>
    public static int ChunkAlignment(RomFormat format) => format switch
    {
        RomFormat.N64ByteSwapped => 2,
        RomFormat.N64LittleEndian => 4,
        _ => 1,
    };

    /// <summary>
    /// Normalizes one chunk in place, so a transform can be applied while streaming instead of
    /// buffering a whole ROM. <paramref name="chunk"/> must be a multiple of
    /// <see cref="ChunkAlignment"/> except for the final chunk.
    /// </summary>
    public static void NormalizeChunk(Span<byte> chunk, RomFormat format)
    {
        switch (format)
        {
            case RomFormat.N64ByteSwapped:
                for (var i = 0; i + 1 < chunk.Length; i += 2)
                {
                    (chunk[i], chunk[i + 1]) = (chunk[i + 1], chunk[i]);
                }

                break;

            case RomFormat.N64LittleEndian:
                for (var i = 0; i + 3 < chunk.Length; i += 4)
                {
                    (chunk[i], chunk[i + 3]) = (chunk[i + 3], chunk[i]);
                    (chunk[i + 1], chunk[i + 2]) = (chunk[i + 2], chunk[i + 1]);
                }

                break;

            default:
                break;
        }
    }
}
