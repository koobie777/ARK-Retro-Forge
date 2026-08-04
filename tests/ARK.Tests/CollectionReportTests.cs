using System.Diagnostics;
using System.Reflection;
using ARK.Core.Configuration;
using ARK.Core.Dat;
using ARK.Core.Naming;
using ARK.Core.Policy;
using ARK.Core.Reporting;
using ARK.Core.Scanning;
using ARK.Core.Units;
using ARK.Core.Verification;

namespace ARK.Tests;

/// <summary>
/// Phase 8.5: the join of catalog, scan, verification and policy.
/// </summary>
/// <remarks>
/// Nothing here computes anything new. The interesting property is that the target set comes from
/// the <i>same</i> engine curation uses — run over the catalog instead of over your files — so the
/// two halves cannot describe different collections.
/// </remarks>
public class CollectionReportTests
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);

    // Gate 3. A full DAT compared against a curated collection reports tens of thousands missing:
    // true, and useless. The target set is the policy's output, never the whole catalog.
    [Fact]
    public void The_target_set_is_the_policy_output_not_the_whole_dat()
    {
        var catalog = Catalog(
            "Tetris (USA)", "Tetris (USA) (Rev 1)", "Tetris (Japan)",
            "Tetris (USA) (Proto)", "Dr. Mario (USA)");

        var everything = Target(catalog, VariantPresets.Everything);
        var oneGame = Target(catalog, VariantPresets.OneGameOneRom);

        Assert.Equal(5, everything.Count);

        // 1G1R keeps one Tetris and one Dr. Mario, not five entries.
        Assert.Equal(2, oneGame.Count);
        Assert.Contains(oneGame.Entries, entry => entry.Name.Title == "Dr. Mario");
        Assert.Equal("Rev 1", oneGame.Entries.Single(entry => entry.Name.Title == "Tetris").Revision);
    }

    // Gate 4. The load-bearing test: the same engine drives both halves, so changing the policy
    // changes what is missing and nothing else about the machinery.
    [Fact]
    public void Changing_the_policy_changes_the_missing_count_and_nothing_else()
    {
        var catalog = Catalog("Tetris (USA)", "Tetris (USA) (Rev 1)", "Tetris (Japan)");
        var onDisk = new[] { "Tetris (USA) (Rev 1)" };

        var everything = Report(catalog, onDisk, VariantPresets.Everything);
        var oneGame = Report(catalog, onDisk, VariantPresets.OneGameOneRom);

        // Same disk, same catalog, same root — only the target set moved.
        Assert.Equal(everything.Root, oneGame.Root);
        Assert.Equal(everything.DatNames, oneGame.DatNames);
        Assert.Single(everything.InState(CollectionState.Present));
        Assert.Single(oneGame.InState(CollectionState.Present));

        Assert.Equal(2, everything.InState(CollectionState.Missing).Count);
        Assert.Empty(oneGame.InState(CollectionState.Missing));

        // And the report says which policy produced each number.
        Assert.Equal("everything", everything.PolicyName);
        Assert.Equal("1g1r", oneGame.PolicyName);
    }

    // Gate 5. A mismatched file inside the target set is a gap you can close; outside it, noise.
    [Fact]
    public void A_mismatched_unit_inside_the_target_set_is_damaged_and_outside_it_is_not_missing()
    {
        var catalog = Catalog("Tetris (USA)", "Dr. Mario (USA)");

        var report = Report(
            catalog,
            new[] { "Tetris (USA)", "Some Hack (USA)" },
            VariantPresets.Everything,
            mismatched: new[] { "Tetris (USA)", "Some Hack (USA)" });

        var damaged = Assert.Single(report.InState(CollectionState.Damaged));
        Assert.Equal("Tetris", damaged.Title);
        Assert.Equal(@"D:\Set\Tetris (USA).zip", damaged.Path);

        // The hack is outside the target set: unrecognized, and it does not inflate missing.
        var unrecognized = Assert.Single(report.InState(CollectionState.Unrecognized));
        Assert.Equal("Some Hack", unrecognized.Title);
        Assert.Equal("Dr. Mario", Assert.Single(report.InState(CollectionState.Missing)).Title);
    }

    // Gate 6. The sleeper feature: what you believe is fine and isn't current.
    [Fact]
    public void Upgradable_is_reported_when_the_target_set_ranks_something_higher()
    {
        var catalog = Catalog("Tetris (USA)", "Tetris (USA) (Rev 1)");

        var report = Report(catalog, new[] { "Tetris (USA)" }, VariantPresets.LatestRevision);

        var upgrade = Assert.Single(report.Upgradable);
        Assert.Equal("Tetris", upgrade.Held.Title);
        Assert.Equal(string.Empty, upgrade.Held.Revision); // untagged is Rev 0, the original
        Assert.Equal("Rev 1", upgrade.Better.Revision);
        Assert.Equal(@"D:\Set\Tetris (USA).zip", upgrade.Path);

        // Held, so it is present rather than missing — the point is that it is not the best.
        Assert.Single(report.InState(CollectionState.Present));
    }

    // Gate 7. The same refusal curation makes, on the catalog side.
    [Fact]
    public void A_target_set_the_policy_cannot_rank_is_refused_not_guessed()
    {
        var catalog = Catalog("Action 52 (USA) (Rev 1)", "Action 52 (USA) (Rev A)");

        var target = Target(catalog, VariantPresets.LatestRevision);

        Assert.Single(target.Refused);
        Assert.Contains(target.Refused[0].Refusals, refusal => refusal.Axis == VariantAxis.Revision);

        // Neither is superseded: the policy could not rank them, so both remain wanted.
        Assert.Empty(target.Superseded);
        Assert.Equal(2, target.Count);
    }

    // Gates 8. Pivots by system, region, and both.
    [Fact]
    public void Counts_pivot_by_system_by_region_and_by_both()
    {
        var catalog = Catalog(
            ("gb", "Tetris (USA)"), ("gb", "Dr. Mario (Japan)"), ("nes", "Metroid (USA, Europe)"));

        var report = Report(catalog, Array.Empty<string>(), VariantPresets.Everything);

        var bySystem = report.BySystem(CollectionState.Missing).ToDictionary(entry => entry.System, entry => entry.Count);
        Assert.Equal(2, bySystem["gb"]);
        Assert.Equal(1, bySystem["nes"]);

        var byRegion = report.ByRegion(CollectionState.Missing).ToDictionary(entry => entry.Region, entry => entry.Count);
        Assert.Equal(2, byRegion["USA"]);      // Tetris (USA) and Metroid (USA, Europe)
        Assert.Equal(1, byRegion["Japan"]);
        Assert.Equal(1, byRegion["Europe"]);

        Assert.Contains(report.BySystemAndRegion(CollectionState.Missing), entry =>
            entry.System == "nes" && entry.Region == "Europe" && entry.Count == 1);
    }

    // Gate 9. A release tagged (USA, Europe) IS the USA release — set intersection, not equality.
    [Fact]
    public void Region_matching_is_set_intersection()
    {
        var both = Tokenizer.Parse("Metroid (USA, Europe)");

        Assert.True(VariantEngine.SatisfiesRegion(both, "USA"));
        Assert.True(VariantEngine.SatisfiesRegion(both, "Europe"));
        Assert.False(VariantEngine.SatisfiesRegion(both, "Japan"));
    }

    // Gate 10. World means all regions; it is a statement about scope, not a member of a list.
    [Fact]
    public void World_satisfies_every_region_target_and_is_never_collapsed_into_a_list()
    {
        var world = Tokenizer.Parse("Tetris (World)");

        Assert.True(VariantEngine.SatisfiesRegion(world, "USA"));
        Assert.True(VariantEngine.SatisfiesRegion(world, "Japan"));
        Assert.True(VariantEngine.SatisfiesRegion(world, "Europe"));

        // The token itself stays exactly as it was — not expanded into a region list.
        Assert.Equal(new[] { "World" }, world.Regions);

        // And a World release satisfies a USA-preferring policy.
        var target = Target(Catalog("Tetris (World)", "Tetris (Japan)"), VariantPresets.OneGameOneRom);
        Assert.Equal("Tetris (World)", Assert.Single(target.Entries).Entry.GameName);
    }

    // Gate 11.
    [Fact]
    public void Catalog_tokenization_is_cached_and_a_second_pass_does_not_re_tokenize()
    {
        var cache = new CatalogNameCache(Tokenizer);
        var entries = Catalog("Tetris (USA)", "Dr. Mario (USA)", "Metroid (USA)");
        var builder = new TargetSetBuilder(cache, Vocabulary);

        builder.Build(entries, VariantPresets.Everything);
        var afterFirst = cache.Tokenized;

        builder.Build(entries, VariantPresets.Everything);

        Assert.Equal(3, afterFirst);
        Assert.Equal(afterFirst, cache.Tokenized); // nothing re-tokenized
        Assert.True(cache.Hits >= 3);
    }

    // Gate 12. Comparing 1.5M entries against 10k units pairwise is not a slow implementation of
    // this — it is a different, unusable program.
    [Fact]
    public void The_join_is_indexed_not_quadratic()
    {
        const int Size = 4_000;

        var catalog = Enumerable.Range(0, Size)
            .Select(i => Entry("gb", $"Game {i:D5} (USA)"))
            .ToArray();
        var onDisk = Enumerable.Range(0, Size).Select(i => $"Game {i:D5} (USA)").ToArray();

        var stopwatch = Stopwatch.StartNew();
        var report = Report(catalog, onDisk, VariantPresets.Everything);
        stopwatch.Stop();

        Assert.Equal(Size, report.InState(CollectionState.Present).Count);
        Assert.Empty(report.InState(CollectionState.Missing));

        // 4,000 x 4,000 pairwise is 16M key comparisons; an indexed join is 8,000 lookups. The
        // bound is loose enough not to be flaky and tight enough that O(n·m) cannot pass it.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"join took {stopwatch.Elapsed} for {Size} x {Size} — that is quadratic, not indexed");
    }

    // Gate 14.
    [Fact]
    public void Json_and_rendered_output_carry_the_same_fields()
    {
        var report = Report(Catalog("Tetris (USA)"), new[] { "Tetris (USA)" }, VariantPresets.Everything);

        var json = CollectionReportJson.Serialize(report);

        foreach (var property in typeof(CollectionReport).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.Contains($"\"{property.Name}\"", json, StringComparison.Ordinal);
        }

        foreach (var property in typeof(CollectionRow).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.Contains($"\"{property.Name}\"", json, StringComparison.Ordinal);
        }

        // States serialize by name, not as opaque integers.
        Assert.Contains("\"Present\"", json, StringComparison.Ordinal);
    }

    // Gate 15. A work queue, not a wall of text.
    [Fact]
    public void The_missing_list_is_usable_as_a_re_acquisition_queue()
    {
        var report = Report(
            Catalog(("gb", "Tetris (USA)"), ("gb", "Dr. Mario (Japan) (Rev 1)")),
            Array.Empty<string>(),
            VariantPresets.Everything);

        var queue = report.MissingQueue();

        Assert.Equal(2, queue.Count);
        Assert.All(queue, line => Assert.Equal(5, line.Split('\t').Length));

        var mario = queue.Single(line => line.Contains("Dr. Mario", StringComparison.Ordinal));
        Assert.Contains("gb", mario, StringComparison.Ordinal);      // system
        Assert.Contains("Japan", mario, StringComparison.Ordinal);   // region
        Assert.Contains("Rev 1", mario, StringComparison.Ordinal);   // revision
    }

    // Gate 16.
    [Fact]
    public void The_report_names_the_policy_and_the_dats_it_was_computed_against()
    {
        var report = Report(Catalog("Tetris (USA)"), Array.Empty<string>(), VariantPresets.RetailOnly);

        Assert.Equal("retail-only", report.PolicyName);
        Assert.Equal(new[] { "Test DAT" }, report.DatNames);
        Assert.Equal(1, report.TargetSetSize);
    }

    [Fact]
    public void Completeness_is_measured_against_the_target_set()
    {
        var report = Report(
            Catalog("Tetris (USA)", "Dr. Mario (USA)", "Metroid (USA)", "Kirby (USA)"),
            new[] { "Tetris (USA)" },
            VariantPresets.Everything);

        Assert.Equal(4, report.TargetSetSize);
        Assert.Equal(0.25, report.Completeness, 3);
    }

    // Non-game content is not something the user is missing.
    [Fact]
    public void Non_game_content_never_enters_the_target_set()
    {
        var target = Target(Catalog("Tetris (USA)", "Tetris (USA) (Bonus Disc)"), VariantPresets.Everything);

        Assert.Equal(1, target.Count);
        Assert.Equal("Tetris (USA)", target.Entries[0].Entry.GameName);
    }

    // Gate 13. Nothing moves, nothing is quarantined, and undo has no role in this phase.
    [Fact]
    public void The_report_is_read_only_and_writes_no_journal()
    {
        var root = TempRoot.Create();
        try
        {
            var set = Path.Combine(root, "Nintendo - Game Boy");
            Directory.CreateDirectory(set);
            foreach (var name in new[] { "Tetris (USA)", "Dr. Mario (USA)" })
            {
                using var archive = System.IO.Compression.ZipFile.Open(
                    Path.Combine(set, name + ".zip"), System.IO.Compression.ZipArchiveMode.Create);
                using var stream = archive.CreateEntry(name + ".gb").Open();
                stream.Write(new byte[] { 1, 2, 3, 4 });
            }

            var before = Snapshot(root);

            var rules = ScanRulesLoader.Load(TestFixtures.ShippedScanRulesPath());
            rules.MinimumFileCount = 1;
            rules.NamingConformanceThreshold = 0;

            var fileSystem = new FileSystemReader();
            var inspector = new ArchiveInspector(fileSystem, new[] { ".zip" });
            var scan = new ScanService(
                fileSystem,
                new DirectoryProfiler(Tokenizer, rules),
                new IGameUnitResolver[] { new CartridgeUnitResolver(Tokenizer, inspector), new DiscUnitResolver() },
                rules).Scan(root);

            var verification = new VerificationReport(
                root,
                scan.Units.Select(unit => new VerifiedUnit(
                    unit.Unit.PrimaryPath, unit.Unit.Files[0].Name, unit.Unit.SetFolder, "Test DAT",
                    VerificationState.Verified, "fixture")).ToArray(),
                0, 0, TimeSpan.Zero, 0);

            var cache = new CatalogNameCache(Tokenizer);
            var target = new TargetSetBuilder(cache, Vocabulary)
                .Build(Catalog("Tetris (USA)", "Dr. Mario (USA)", "Metroid (USA)"), VariantPresets.Everything);

            var report = new CollectionReportService(Vocabulary).Build(scan, verification, target, cache);

            Assert.Single(report.InState(CollectionState.Missing));
            Assert.Equal(before, Snapshot(root));
            Assert.False(Directory.Exists(Path.Combine(root, "journal")));
            Assert.False(Directory.Exists(Path.Combine(root, ".ark-quarantine")));
        }
        finally
        {
            TempRoot.Delete(root);
        }
    }

    private static string Snapshot(string root) => string.Join("\n", Directory
        .EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => File.Exists(path)
            ? $"{path}:{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(path)))}"
            : path + "/"));

    private static CatalogEntry Entry(string system, string name) => new()
    {
        System = system,
        DatName = "Test DAT",
        GameName = name,
        RomName = name + ".gb",
        Crc32 = "aabbccdd",
    };

    private static IReadOnlyList<CatalogEntry> Catalog(params string[] names) =>
        names.Select(name => Entry("gb", name)).ToArray();

    private static IReadOnlyList<CatalogEntry> Catalog(params (string System, string Name)[] entries) =>
        entries.Select(entry => Entry(entry.System, entry.Name)).ToArray();

    private static TargetSet Target(IReadOnlyList<CatalogEntry> catalog, VariantPolicy policy) =>
        new TargetSetBuilder(new CatalogNameCache(Tokenizer), Vocabulary).Build(catalog, policy);

    private static CollectionReport Report(
        IReadOnlyList<CatalogEntry> catalog,
        IReadOnlyList<string> onDisk,
        VariantPolicy policy,
        IReadOnlyList<string>? mismatched = null)
    {
        const string Directory = @"D:\Set";

        var files = onDisk
            .Select(name => new FileEntry(
                Path.Combine(Directory, name + ".zip"), name + ".zip", ".zip", 1024, DateTimeOffset.UnixEpoch))
            .ToArray();

        var profile = new DirectoryProfile(
            Directory, "Set", files.Length, ".zip", 1, 1, DirectoryOutcome.RomSet, ExclusionReason.None,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<FileEntry>());

        var units = files.Select(file => new ScannedUnit(
            new GameUnit(
                GameUnitKind.Cartridge, file.FullPath, new[] { file },
                Tokenizer.Parse(file.NameWithoutExtension), "Set",
                Array.Empty<string>(), Array.Empty<ArchiveEntry>(), Array.Empty<GameUnitIssue>()),
            null)).ToArray();

        var scan = new ScanReport(
            Directory,
            new[] { new ScannedDirectory(profile, new DatScope("Test DAT", "gb", null, DatScopeSource.DirectoryName)) },
            units,
            Array.Empty<ExcludedFile>(),
            Array.Empty<FileEntry>());

        var verification = new VerificationReport(
            Directory,
            units.Select(unit => new VerifiedUnit(
                unit.Unit.PrimaryPath,
                unit.Unit.Files[0].Name,
                "Set",
                "Test DAT",
                mismatched?.Any(name => unit.Unit.Files[0].Name.StartsWith(name, StringComparison.Ordinal)) == true
                    ? VerificationState.Mismatched
                    : VerificationState.Verified,
                "test fixture")).ToArray(),
            0, 0, TimeSpan.Zero, 0);

        var cache = new CatalogNameCache(Tokenizer);
        var target = new TargetSetBuilder(cache, Vocabulary).Build(catalog, policy);

        return new CollectionReportService(Vocabulary).Build(scan, verification, target, cache);
    }
}
