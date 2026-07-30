using ARK.Core.Dat;
using ARK.Core.Instances;

namespace ARK.Tests;

public sealed class DatCatalogTests : IDisposable
{
    private readonly string _root;
    private readonly InstancePaths _paths;
    private readonly DatCatalog _catalog;

    public DatCatalogTests()
    {
        _root = TempRoot.Create();
        _paths = new InstancePaths("dat-catalog-tests", _root);
        Directory.CreateDirectory(_paths.Db); // the Executor provisions this in the real app
        _catalog = new DatCatalog(_paths);
    }

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    private static LogiqxDat Nointro() => LogiqxParser.Parse(TestFixtures.Read("nointro-n64.dat"));

    // Gate 9 — importing the same DAT twice yields identical catalog state (no duplication).
    [Fact]
    public void Importing_the_same_dat_twice_yields_identical_state()
    {
        _catalog.Import(Nointro(), "n64", "origin");
        var afterFirst = Snapshot();

        _catalog.Import(Nointro(), "n64", "origin");
        var afterSecond = Snapshot();

        Assert.Equal(afterFirst, afterSecond);

        // Entries are replaced, not duplicated: exactly one exact match survives.
        var exact = _catalog.FindByName("Super Test 64 (USA).z64").Where(c => c.Match == NameMatch.Exact);
        Assert.Single(exact);
    }

    // Gate 10 — FindByHash returns the single matching entry; a miss returns none, not a guess.
    [Fact]
    public void FindByHash_returns_the_single_entry_or_none()
    {
        _catalog.Import(Nointro(), "n64", "origin");

        var hit = _catalog.FindByHash("1A2B3C4D"); // uppercase input still matches
        Assert.NotNull(hit);
        Assert.Equal("Super Test 64 (USA).z64", hit!.RomName);

        // match on SHA1 too
        Assert.NotNull(_catalog.FindByHash("cafebabecafebabecafebabecafebabecafebabe"));

        Assert.Null(_catalog.FindByHash("ffffffff")); // miss → none
    }

    // Gate 11 — FindByName returns ranked candidates (exact first).
    [Fact]
    public void FindByName_returns_ranked_candidates()
    {
        _catalog.Import(Nointro(), "n64", "origin");

        // Exact ROM-name match ranks first.
        var exact = _catalog.FindByName("Test Racer (Europe).z64");
        Assert.NotEmpty(exact);
        Assert.Equal(NameMatch.Exact, exact[0].Match);

        // A match that only holds once the ROM extension is stripped ranks Normalized. The game name
        // ("Widget") differs from the query, so the exact tier does not fire.
        _catalog.Import(
            LogiqxParser.Parse("<datafile><header><name>Inline</name></header><game name=\"Widget\"><rom name=\"Cool Title (USA).z64\" crc=\"99999999\"/></game></datafile>"),
            "n64",
            "inline");
        var normalized = _catalog.FindByName("Cool Title (USA)");
        Assert.NotEmpty(normalized);
        Assert.Equal(NameMatch.Normalized, normalized[0].Match);

        // A substring query returns partial candidates, all containing the term.
        var partial = _catalog.FindByName("Racer");
        Assert.NotEmpty(partial);
        Assert.All(partial, candidate => Assert.Contains("Racer", candidate.Entry.RomName, StringComparison.OrdinalIgnoreCase));
    }

    // Coverage reflects the imported DAT and its header metadata.
    [Fact]
    public void Coverage_reports_imported_dat_with_header_metadata()
    {
        _catalog.Import(Nointro(), "n64", "origin");

        var coverage = Assert.Single(_catalog.Coverage());
        Assert.Equal("Nintendo - Nintendo 64", coverage.DatName);
        Assert.Equal("n64", coverage.System);
        Assert.Equal(3, coverage.EntryCount);
        Assert.Equal("2024-02-01", coverage.Date);
    }

    private string Snapshot() =>
        string.Join("|", _catalog.Coverage().Select(c => $"{c.DatName}:{c.System}:{c.EntryCount}"));
}
