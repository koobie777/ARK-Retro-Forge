using ARK.Core.Dat;

namespace ARK.Tests;

public class LogiqxParserTests
{
    // Gate 6 — No-Intro variant: entry count, names, and all hash types correct.
    [Fact]
    public void Parses_no_intro_variant_with_all_and_partial_hash_types()
    {
        var dat = LogiqxParser.Parse(TestFixtures.Read("nointro-n64.dat"));

        Assert.Equal("Nintendo - Nintendo 64", dat.Header.Name);
        Assert.Equal("No-Intro", dat.Header.Author);
        Assert.Equal(3, dat.Entries.Count);

        var full = dat.Entries.Single(entry => entry.RomName == "Super Test 64 (USA).z64");
        Assert.Equal(8388608, full.Size);
        Assert.Equal("1a2b3c4d", full.Crc32);
        Assert.Equal("0123456789abcdef0123456789abcdef", full.Md5);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", full.Sha1);

        var crcOnly = dat.Entries.Single(entry => entry.RomName == "Test Racer (Europe).z64");
        Assert.Equal("deadbeef", crcOnly.Crc32);
        Assert.Null(crcOnly.Md5);
        Assert.Null(crcOnly.Sha1);

        var crcAndSha = dat.Entries.Single(entry => entry.RomName == "Puzzle Quest (Japan).z64");
        Assert.Equal("cafebabe", crcAndSha.Crc32);
        Assert.Null(crcAndSha.Md5);
        Assert.Equal("cafebabecafebabecafebabecafebabecafebabe", crcAndSha.Sha1);
    }

    // Gate 7 — Redump variant: multi-ROM games, entry count, names, hashes.
    [Fact]
    public void Parses_redump_variant_with_multi_rom_games()
    {
        var dat = LogiqxParser.Parse(TestFixtures.Read("redump-psx.dat"));

        Assert.Equal("Sony - PlayStation", dat.Header.Name);
        Assert.Equal("redump.org", dat.Header.Author);
        Assert.Equal(5, dat.Entries.Count); // 3 + 2 ROMs across two games

        var track1 = dat.Entries.Single(entry => entry.RomName == "Test Adventure (USA) (Track 1).bin");
        Assert.Equal("Test Adventure (USA)", track1.GameName);
        Assert.Equal("11111111", track1.Crc32);
        Assert.Equal(52348800, track1.Size);

        var cue = dat.Entries.Single(entry => entry.RomName == "Test Adventure (USA).cue");
        Assert.Equal("33333333", cue.Crc32);
        Assert.Null(cue.Md5);
        Assert.Null(cue.Sha1);
    }

    // Gate 8 — header metadata (version, date, description) parsed for staleness reporting.
    [Fact]
    public void Parses_header_metadata_for_staleness()
    {
        var dat = LogiqxParser.Parse(TestFixtures.Read("nointro-n64.dat"));

        Assert.Equal("20240201-123456", dat.Header.Version);
        Assert.Equal("2024-02-01", dat.Header.Date);
        Assert.Equal("Nintendo - Nintendo 64 (BigEndian)", dat.Header.Description);
    }

    // Hashes are normalized to lower case regardless of the DAT's casing.
    [Fact]
    public void Normalizes_hashes_to_lowercase()
    {
        var dat = LogiqxParser.Parse(
            "<datafile><header><name>X</name></header><game name=\"g\"><rom name=\"r\" crc=\"ABCD1234\"/></game></datafile>");

        Assert.Equal("abcd1234", dat.Entries.Single().Crc32);
    }
}
