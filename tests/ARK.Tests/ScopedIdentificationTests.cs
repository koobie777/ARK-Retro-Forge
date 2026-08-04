using ARK.Core.Configuration;
using ARK.Core.Dat;
using ARK.Core.Instances;
using ARK.Core.Naming;
using ARK.Core.Scanning;
using ARK.Core.Systems;
using ARK.Core.Units;

namespace ARK.Tests;

/// <summary>
/// Phase 4.1 defect 1: identification is scoped to one DAT per ROM-set directory, never to the
/// whole catalog.
/// </summary>
/// <remarks>
/// Found by running <c>ark scan</c> against a real drive. With no Redump PlayStation DAT
/// imported, 308 Redump disc images matched entries from a different DAT because they share a
/// title — and verification would then have hashed every one against the wrong entry and reported
/// it corrupt.
/// </remarks>
public sealed class ScopedIdentificationTests : IDisposable
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);
    private static readonly SystemRegistry Systems = SystemRegistry.Load(TestFixtures.ShippedSystemsDirectory());

    private readonly string _root;
    private readonly DatCatalog _catalog;

    public ScopedIdentificationTests()
    {
        _root = TempRoot.Create();
        var paths = new InstancePaths("scoped-identification", _root);
        Directory.CreateDirectory(paths.Db); // the Executor provisions this in the real app
        _catalog = new DatCatalog(paths);
    }

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Gate 3. The title exists in both DATs; only the scoped one may answer.
    [Fact]
    public void Directory_matching_a_dat_identifies_only_within_that_dat()
    {
        ImportDat("Sony - PlayStation", null, "Ridge Racer (USA)", "aaaa1111");
        ImportDat("Sony - PlayStation (PS one Classics) (PSN)", null, "Ridge Racer (USA)", "bbbb2222");

        var resolver = Resolver();

        var redump = resolver.Resolve("Sony - PlayStation");
        var psn = resolver.Resolve("Sony - PlayStation (PS one Classics) (PSN)");

        Assert.Equal("Sony - PlayStation", redump!.DatName);
        Assert.Equal("Sony - PlayStation (PS one Classics) (PSN)", psn!.DatName);

        // Same title, two DATs, two artifacts. Each scope answers with its own — which is exactly
        // what a catalog-wide search could not do, since it would return whichever it found first.
        Assert.Equal("aaaa1111", resolver.IndexFor(redump).Find("Ridge Racer (USA)")!.Crc32);
        Assert.Equal("bbbb2222", resolver.IndexFor(psn).Find("Ridge Racer (USA)")!.Crc32);

        Assert.Equal(1, resolver.IndexFor(redump).Count);
    }

    // Gate 4. The dangerous case: a title that exists somewhere in the catalog, in a directory
    // whose own DAT was never imported.
    [Fact]
    public void Directory_with_no_resolvable_dat_identifies_nothing()
    {
        ImportDat("Sony - PlayStation (PS one Classics) (PSN)", null, "Ridge Racer (USA)", "bbbb2222");

        var resolver = Resolver();

        // The Redump PlayStation DAT is not imported, so this directory resolves to no DAT at all.
        Assert.Null(resolver.Resolve("Redump - Sony - PlayStation"));

        var report = Scan(resolver, "Redump - Sony - PlayStation", "Ridge Racer (USA)");

        Assert.Empty(report.Identified);
        Assert.Equal(report.RomSetDirectories.Sum(d => d.FileCount), report.CandidateFileCount);
        Assert.Single(report.UnscopedRomSets);
    }

    // Gate 5, in miniature: a directory whose name matches a DAT identifies even though no system
    // is defined for it. "gb" is not in config/systems and does not need to be.
    [Fact]
    public void Directory_identifies_via_dat_name_even_with_no_system_defined()
    {
        Assert.Null(Systems.ResolveByAlias("Nintendo - Game Boy"));

        ImportDat("Nintendo - Game Boy", null, "Tetris (World)", "cccc3333");

        var report = Scan(Resolver(), "Nintendo - Game Boy", "Tetris (World)");

        var identified = Assert.Single(report.Identified);
        Assert.Equal("cccc3333", identified.Match!.Crc32);
        Assert.Equal("Nintendo - Game Boy", report.RomSetDirectories[0].Scope!.DatName);
    }

    // Resolution step 2: the folder is not spelled like the DAT, but resolves through the system
    // registry to a variant that is indexed.
    [Fact]
    public void Directory_resolves_through_system_and_qualifier()
    {
        ImportDat("Nintendo - Nintendo Entertainment System (Headered)", "nes", "Super Test (USA)", "dddd4444", qualifier: "Headered");

        var scope = Resolver().Resolve("Nintendo - Nintendo Entertainment System (Headered)");

        Assert.NotNull(scope);
        Assert.Equal("nes", scope!.System);
        Assert.Equal("Headered", scope.Qualifier);
    }

    // A system variant that resolves but has no DAT indexed must not borrow another variant's.
    [Fact]
    public void Variant_with_no_indexed_dat_resolves_to_no_scope()
    {
        ImportDat("Nintendo - Nintendo Entertainment System (Headered)", "nes", "Super Test (USA)", "dddd4444", qualifier: "Headered");

        Assert.Null(Resolver().Resolve("Nintendo - Nintendo Entertainment System (Headerless)"));
    }

    // Gate 7. The report must name the DAT, or say plainly that there was none.
    [Fact]
    public void Report_names_the_dat_each_directory_was_compared_against()
    {
        ImportDat("Nintendo - Game Boy", null, "Tetris (World)", "cccc3333");

        var resolver = Resolver();
        var matched = Scan(resolver, "Nintendo - Game Boy", "Tetris (World)");
        var unmatched = Scan(resolver, "Some Unknown Set", "Tetris (World)");

        Assert.Equal("Nintendo - Game Boy", matched.RomSetDirectories[0].Scope!.ToString());
        Assert.Null(unmatched.RomSetDirectories[0].Scope);
    }

    private DatScopeResolver Resolver() => new(_catalog, Systems, Tokenizer, Vocabulary);

    private void ImportDat(string datName, string? system, string gameName, string crc, string? qualifier = null)
    {
        _catalog.Import(
            LogiqxParser.Parse(
                $"<datafile><header><name>{datName}</name></header>" +
                $"<game name=\"{gameName}\"><rom name=\"{gameName}.bin\" crc=\"{crc}\" size=\"1024\"/></game></datafile>"),
            system,
            qualifier,
            datName);
    }

    // Enough files to clear MinimumFileCount, all conformant, one of them the title under test.
    private static ScanReport Scan(IDatScopeResolver resolver, string directoryName, string title)
    {
        var names = new List<string> { title };
        names.AddRange(TestFixtures.ReadCorpus("real-names.txt").Take(9));

        var directory = @"D:\" + directoryName;
        var files = names
            .Select(name => new FileEntry(
                Path.Combine(directory, name + ".zip"), name + ".zip", ".zip", 1024, DateTimeOffset.UnixEpoch))
            .ToArray();

        var rules = ScanRulesLoader.Load(TestFixtures.ShippedScanRulesPath());
        var reader = new InMemoryFileSystemReader(new[] { new DirectoryListing(directory, directoryName, files) });
        var inspector = new FakeArchiveInspector { Enabled = false };
        var service = new ScanService(
            reader,
            new DirectoryProfiler(Tokenizer, rules),
            new IGameUnitResolver[]
            {
                new DiscUnitResolver(Tokenizer, inspector, reader),
                new CartridgeUnitResolver(Tokenizer, inspector),
            },
            rules);

        return service.Scan(directory, resolver);
    }

}
