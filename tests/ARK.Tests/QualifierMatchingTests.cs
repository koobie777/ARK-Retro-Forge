using ARK.Core.Dat;
using ARK.Core.Instances;
using ARK.Core.Systems;

namespace ARK.Tests;

/// <summary>
/// Phase 2.1: DAT names carrying a format qualifier resolve to a system <b>and</b> that qualifier.
/// </summary>
/// <remarks>
/// The defect this fixes: importing the real No-Intro Love Pack resolved only SNES, because SNES's
/// DAT name carries no parenthetical while NES and N64 do, and alias matching was exact-string.
/// Two of the three shipped systems had no DAT backing at all, leaving Phase 4's Identified bucket
/// permanently empty for them.
/// </remarks>
public sealed class QualifierMatchingTests : IDisposable
{
    private static readonly SystemRegistry Systems = SystemRegistry.Load(TestFixtures.ShippedSystemsDirectory());

    private readonly string _root;
    private readonly DatCatalog _catalog;

    public QualifierMatchingTests()
    {
        _root = TempRoot.Create();
        var paths = new InstancePaths("qualifier-tests", _root);
        Directory.CreateDirectory(paths.Db); // the Executor provisions this in the real app
        _catalog = new DatCatalog(paths);
    }

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Gates 3, 4, 5. The qualifier strings are the ones the real No-Intro DATs actually use.
    [Theory]
    [InlineData("Nintendo - Nintendo Entertainment System (Headered)", "nes", "Headered")]
    [InlineData("Nintendo - Nintendo Entertainment System (Headerless)", "nes", "Headerless")]
    [InlineData("Nintendo - Nintendo 64 (BigEndian)", "n64", "BigEndian")]
    [InlineData("Nintendo - Nintendo 64 (ByteSwapped)", "n64", "ByteSwapped")]
    public void Declared_qualifier_resolves_to_system_and_qualifier(string datName, string code, string qualifier)
    {
        var match = Systems.ResolveDatName(datName);

        Assert.NotNull(match);
        Assert.Equal(code, match!.Code);
        Assert.Equal(qualifier, match.Qualifier);
    }

    // Gates 6 and 7. The negative cases matter as much as the positive ones.
    [Theory]
    // A subset, not a format. Treating it as a byte-order variant would file a handful of
    // SmartMedia dumps as though they were the N64 library.
    [InlineData("Nintendo - Nintendo 64 (Mario no Photopi SmartMedia)")]
    // A different system entirely — the base never matches an alias.
    [InlineData("Nintendo - Nintendo 64DD")]
    [InlineData("Nintendo - Game Boy Advance (Headered)")]
    [InlineData("Acorn - Archimedes")]
    public void Undeclared_or_unknown_names_stay_unrecognized(string datName)
    {
        Assert.Null(Systems.ResolveDatName(datName));
    }

    // Gate 8. Unqualified names still resolve exactly as before.
    [Theory]
    [InlineData("Nintendo - Super Nintendo Entertainment System", "snes")]
    [InlineData("Nintendo - Nintendo Entertainment System", "nes")]
    [InlineData("SNES", "snes")]
    public void Unqualified_names_resolve_with_a_null_qualifier(string datName, string code)
    {
        var match = Systems.ResolveDatName(datName);

        Assert.NotNull(match);
        Assert.Equal(code, match!.Code);
        Assert.Null(match.Qualifier);
    }

    // The declarations must describe what No-Intro actually publishes, not what we assumed.
    [Fact]
    public void Definitions_declare_the_qualifiers_the_real_dats_use()
    {
        Assert.Equal(
            new[] { "Headered", "Headerless" },
            Systems.Resolve("nes")!.FormatQualifiers.Intersect(new[] { "Headered", "Headerless" }).Order().ToArray());

        var n64 = Systems.Resolve("n64")!.FormatQualifiers;
        Assert.Contains("BigEndian", n64);
        Assert.Contains("ByteSwapped", n64);
    }

    // Gates 4 and 9. Headered and Headerless are two distinct hash sets over the same games; a
    // lookup that cannot name the variant cannot give a correct answer.
    [Fact]
    public void Variants_are_separate_entry_sets_addressable_by_qualifier()
    {
        Import("Nintendo - Nintendo Entertainment System (Headered)", "nes", "Headered", "aaaa1111");
        Import("Nintendo - Nintendo Entertainment System (Headerless)", "nes", "Headerless", "bbbb2222");

        var headered = _catalog.EntriesFor("nes", "Headered");
        var headerless = _catalog.EntriesFor("nes", "Headerless");

        Assert.Equal("aaaa1111", Assert.Single(headered).Crc32);
        Assert.Equal("bbbb2222", Assert.Single(headerless).Crc32);

        // The same game, two hashes — which is exactly why merging them would be worse than not
        // matching at all.
        Assert.Equal(headered[0].RomName, headerless[0].RomName);

        // Both variants are visible under the system, and neither is the unqualified set.
        Assert.Equal(2, _catalog.AllEntries("nes").Count);
        Assert.Empty(_catalog.EntriesFor("nes", null));
    }

    // The unqualified DAT is a third, distinct set — null is matched exactly, not as "any".
    [Fact]
    public void Unqualified_variant_is_its_own_set()
    {
        Import("Nintendo - Nintendo Entertainment System", "nes", null, "cccc3333");
        Import("Nintendo - Nintendo Entertainment System (Headered)", "nes", "Headered", "aaaa1111");

        Assert.Equal("cccc3333", Assert.Single(_catalog.EntriesFor("nes", null)).Crc32);
        Assert.Equal("aaaa1111", Assert.Single(_catalog.EntriesFor("nes", "Headered")).Crc32);
    }

    // Gate 10.
    [Fact]
    public void Coverage_shows_both_variants_with_distinct_counts()
    {
        Import("Nintendo - Nintendo Entertainment System (Headered)", "nes", "Headered", "aaaa1111");
        Import("Nintendo - Nintendo Entertainment System (Headerless)", "nes", "Headerless", "bbbb2222", games: 2);
        Import("Nintendo - Super Nintendo Entertainment System", "snes", null, "dddd4444");

        var nes = _catalog.Coverage("nes");

        Assert.Equal(2, nes.Count);
        Assert.Equal(new[] { "Headered", "Headerless" }, nes.Select(c => c.Qualifier).Order().ToArray());
        Assert.Equal(1, nes.Single(c => c.Qualifier == "Headered").EntryCount);
        Assert.Equal(2, nes.Single(c => c.Qualifier == "Headerless").EntryCount);
    }

    // Gate 10, filters.
    [Fact]
    public void Coverage_filters_by_resolution_state()
    {
        Import("Nintendo - Nintendo Entertainment System (Headered)", "nes", "Headered", "aaaa1111");
        Import("Acorn - Archimedes", null, null, "eeee5555");

        Assert.Equal(2, _catalog.Coverage().Count);
        Assert.Equal("nes", Assert.Single(_catalog.Coverage(recognized: true)).System);
        Assert.Null(Assert.Single(_catalog.Coverage(recognized: false)).System);
        Assert.Empty(_catalog.Coverage("gba"));
    }

    // Gate 12. Phase 2's idempotency guarantee still holds with the qualifier in play.
    [Fact]
    public void Re_import_remains_idempotent()
    {
        Import("Nintendo - Nintendo Entertainment System (Headered)", "nes", "Headered", "aaaa1111");
        var first = Snapshot();

        Import("Nintendo - Nintendo Entertainment System (Headered)", "nes", "Headered", "aaaa1111");

        Assert.Equal(first, Snapshot());
        Assert.Single(_catalog.EntriesFor("nes", "Headered"));
    }

    // A catalog written before the qualifier column existed must survive the upgrade: the real one
    // holds over a million entries and re-indexing it to gain a nullable column is a poor trade.
    [Fact]
    public void Existing_catalog_migrates_without_losing_entries()
    {
        Import("Nintendo - Nintendo Entertainment System (Headered)", "nes", "Headered", "aaaa1111");

        var paths = new InstancePaths("qualifier-tests", _root);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={paths.CatalogDatabase};Pooling=False"))
        {
            connection.Open();
            using var drop = connection.CreateCommand();
            drop.CommandText = "ALTER TABLE catalogs DROP COLUMN qualifier;";
            drop.ExecuteNonQuery();
        }

        var reopened = new DatCatalog(paths);

        var coverage = Assert.Single(reopened.Coverage());
        Assert.Equal(1, coverage.EntryCount);
        Assert.Null(coverage.Qualifier);
    }

    private void Import(string datName, string? system, string? qualifier, string crc, int games = 1)
    {
        var roms = string.Concat(Enumerable.Range(0, games).Select(i =>
            $"<game name=\"Sample\"><rom name=\"Sample (USA).nes\" crc=\"{crc}\" size=\"{1024 + i}\"/></game>"));

        _catalog.Import(
            LogiqxParser.Parse($"<datafile><header><name>{datName}</name></header>{roms}</datafile>"),
            system,
            qualifier,
            datName);
    }

    private string Snapshot() =>
        string.Join("|", _catalog.Coverage().Select(c => $"{c.DatName}:{c.System}:{c.Qualifier}:{c.EntryCount}"));
}
