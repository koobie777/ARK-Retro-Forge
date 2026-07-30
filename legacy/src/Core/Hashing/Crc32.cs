namespace ARK.Core.Hashing;

/// <summary>
/// CRC32 hasher implementation
/// </summary>
public sealed class Crc32Hasher : IDisposable
{
    private static readonly uint[] Crc32Table = BuildCrc32Table();
    private uint _crc = 0xFFFFFFFF;

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        const uint polynomial = 0xEDB88320;

        for (uint i = 0; i < 256; i++)
        {
            var crc = i;
            for (var j = 8; j > 0; j--)
            {
                if ((crc & 1) == 1)
                {
                    crc = (crc >> 1) ^ polynomial;
                }
                else
                {
                    crc >>= 1;
                }
            }
            table[i] = crc;
        }
        return table;
    }

    public void Append(ReadOnlySpan<byte> source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            var index = (_crc ^ source[i]) & 0xFF;
            _crc = (_crc >> 8) ^ Crc32Table[index];
        }
    }

    public byte[] GetHashAndReset()
    {
        var finalCrc = _crc ^ 0xFFFFFFFF;
        var hash = BitConverter.GetBytes(finalCrc);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(hash);
        }
        Reset();
        return hash;
    }

    public void Reset()
    {
        _crc = 0xFFFFFFFF;
    }

    public void Dispose()
    {
        // No resources to dispose
    }
}
