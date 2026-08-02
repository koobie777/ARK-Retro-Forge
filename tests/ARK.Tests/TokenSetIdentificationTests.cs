using ARK.Core.Configuration;
using ARK.Core.Dat;
using ARK.Core.Naming;

namespace ARK.Tests;

/// <summary>
/// Phase 4 gate 14, and the standing rule it comes from: grouping, matching and comparison run on
/// <see cref="ParsedName"/> token sets, never on formatted strings.
/// </summary>
public class TokenSetIdentificationTests
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);

    private static string Key(string name) => NameMatchKey.For(Tokenizer.Parse(name), Vocabulary);

    // Canonical token order is a PARTIAL order, so one release has more than one canonical
    // spelling. String comparison reports these as two different games; token sets do not.
    [Theory]
    [InlineData("GameShark CDX (USA) (Unl) (v3.4)", "GameShark CDX (USA) (v3.4) (Unl)")]
    [InlineData("Mortal Kombat II (USA) (Rev 1) (Sample)", "Mortal Kombat II (USA) (Sample) (Rev 1)")]
    public void Same_tokens_in_different_order_are_one_release(string left, string right)
    {
        Assert.NotEqual(left, right);
        Assert.Equal(Key(left), Key(right));
    }

    // Article inversion is a spelling difference, not a different game.
    [Fact]
    public void Article_inversion_does_not_split_a_release()
    {
        Assert.Equal(Key("The Alliance Alive (USA)"), Key("Alliance Alive, The (USA)"));
    }

    // Presence of a token is never ignored: an extra (Rev 1) is a different release.
    [Theory]
    [InlineData("Tekken 3 (USA)", "Tekken 3 (USA) (Rev 1)")]
    [InlineData("Tekken 3 (USA)", "Tekken 3 (Europe)")]
    [InlineData("Tekken 3 (USA) (En,Fr,De)", "Tekken 3 (USA) (En,Fr)")]
    public void Different_tokens_are_different_releases(string left, string right)
    {
        Assert.NotEqual(Key(left), Key(right));
    }

    // A language token and a region token with the same text must not collide.
    [Fact]
    public void Category_is_part_of_the_key()
    {
        Assert.NotEqual(Key("Game (Japan)"), Key("Game (USA) (Ja)"));
    }

    // A flagged name yields an empty key, which matches nothing — never identified by guesswork.
    [Fact]
    public void Unparseable_name_yields_a_key_that_matches_nothing()
    {
        Assert.Equal(string.Empty, Key("Adventure Time - Explore the Dungeon"));

        var index = DatNameIndex.Build(
            new[] { new CatalogEntry { GameName = "Tekken 3 (USA) (En,Fr,De)", RomName = "Tekken 3 (USA) (En,Fr,De).bin" } },
            Tokenizer,
            Vocabulary);

        Assert.Null(index.Find("Adventure Time - Explore the Dungeon"));
    }

    [Fact]
    public void Index_identifies_a_reordered_spelling_of_the_same_release()
    {
        var index = DatNameIndex.Build(
            new[]
            {
                new CatalogEntry { GameName = "GameShark CDX (USA) (Unl) (v3.4)", RomName = "GameShark CDX.bin", Crc32 = "aabbccdd" },
            },
            Tokenizer,
            Vocabulary);

        var match = index.Find("GameShark CDX (USA) (v3.4) (Unl)");

        Assert.NotNull(match);
        Assert.Equal("aabbccdd", match!.Crc32);
    }

    // Bracket flags mark a bad dump of a release, not a different release. Whether the bytes are
    // good is verification's job.
    [Fact]
    public void Bracket_flag_does_not_prevent_identification()
    {
        Assert.Equal(Key("Jackass - The Game DS (USA)"), Key("Jackass - The Game DS (USA) [b]"));
    }

    [Fact]
    public void Dat_entry_falls_back_to_rom_name_without_extension()
    {
        var index = DatNameIndex.Build(
            new[] { new CatalogEntry { GameName = null, RomName = "Tekken 3 (USA) (En,Fr,De).bin" } },
            Tokenizer,
            Vocabulary);

        Assert.NotNull(index.Find("Tekken 3 (USA) (En,Fr,De)"));
        Assert.Equal(1, index.Count);
    }
}
