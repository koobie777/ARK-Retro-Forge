using System.IO.Compression;
using ARK.Core.Configuration;
using ARK.Core.Naming;
using ARK.Core.Scanning;
using ARK.Core.Units;

namespace ARK.Tests;

/// <summary>
/// Phase 4 Parts B and C: game units, the three buckets, and the read-only guarantee.
/// </summary>
public class ScanServiceTests
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);

    private static ScanRules Rules() => ScanRulesLoader.Load(TestFixtures.ShippedScanRulesPath());

    private static ScanService Build(IFileSystemReader reader, IArchiveInspector? inspector = null)
    {
        var rules = Rules();
        var archives = inspector ?? NullArchiveInspector();
        return new ScanService(
            reader,
            new DirectoryProfiler(Tokenizer, rules, new[] { "BigEndian", "Headered", "Decrypted", "NKit RVZ" }),
            new IGameUnitResolver[]
            {
                new DiscUnitResolver(Tokenizer, archives, reader),
                new CartridgeUnitResolver(Tokenizer, archives),
            },
            rules);
    }

    // Gate 15. Three buckets, never two — every one of the real drive's 22,050 files accounted for.
    [Fact]
    public void Three_buckets_account_for_every_file_on_the_reference_drive()
    {
        var expected = ReferenceDrive.Expectations();

        var report = Build(new InMemoryFileSystemReader(ReferenceDrive.Listings())).Scan(@"D:\");

        Assert.True(report.IsComplete,
            $"{report.TotalFiles} seen; {report.IdentifiedFileCount} identified + {report.CandidateFileCount} candidate + {report.ExcludedFileCount} excluded");
        Assert.Equal(expected.TotalFiles, report.TotalFiles);
        Assert.Equal(expected.RomSetDirectories, report.RomSetDirectories.Count);
        Assert.Equal(expected.OtherDirectories + expected.TooFewDirectories, report.ExcludedDirectories.Count);

        // With no DAT indexed every unit in a ROM set is a candidate — the honest answer.
        Assert.Equal(expected.RomSetFiles, report.CandidateFileCount);
        Assert.Equal(expected.OtherFiles + expected.TooFewFiles, report.ExcludedFileCount);
    }

    // Gate 12 (part). Profiling reads listings only: no file is opened to decide what a directory is.
    [Fact]
    public void Classifying_a_directory_never_opens_a_file()
    {
        var reader = new InMemoryFileSystemReader(ReferenceDrive.Listings());

        Build(reader).Scan(@"D:\");

        Assert.Empty(reader.Opened);
    }

    // Gate 12. Scan is read-only: a real tree is byte-identical afterwards and no journal appears.
    [Fact]
    public void Scanning_a_real_tree_leaves_it_byte_identical_and_writes_no_journal()
    {
        var root = TempRoot.Create();
        try
        {
            var set = Path.Combine(root, "Nintendo - Game Boy (Headered)");
            Directory.CreateDirectory(set);
            foreach (var name in TestFixtures.ReadCorpus("real-names.txt").Take(12))
            {
                File.WriteAllText(Path.Combine(set, name + ".zip"), name);
            }

            var before = Snapshot(root);

            var report = Build(new FileSystemReader()).Scan(root);

            Assert.True(report.TotalFiles >= 12);
            Assert.Equal(before, Snapshot(root));
            Assert.False(Directory.Exists(Path.Combine(root, "journal")));
        }
        finally
        {
            TempRoot.Delete(root);
        }
    }

    // Gate 11.
    [Fact]
    public void Incomplete_download_files_are_flagged_and_never_identified()
    {
        var names = TestFixtures.ReadCorpus("real-names.txt").Take(20).ToArray();
        var files = names.Select((name, i) => Entry(@"D:\Set", name, i < 18 ? ".zip" : ".part")).ToList();

        var report = Build(new InMemoryFileSystemReader(new[] { new DirectoryListing(@"D:\Set", "Set", files) })).Scan(@"D:\Set");

        Assert.Equal(2, report.Flagged.Count);
        Assert.All(report.Flagged, file => Assert.Equal(".part", file.Extension));
        Assert.Contains(report.Excluded, excluded => excluded.Reason.Contains("incomplete", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(report.Units, unit => unit.Unit.PrimaryPath.EndsWith(".part", StringComparison.OrdinalIgnoreCase));
        Assert.True(report.IsComplete);
    }

    // Gate 9. An archive holding more than one ROM is reported, never resolved by guessing.
    [Fact]
    public void Archive_with_multiple_roms_is_reported_as_an_anomaly()
    {
        // The directory needs enough files to be profiled as a ROM set at all; the anomaly is
        // about one archive inside an otherwise ordinary set.
        var names = TestFixtures.ReadCorpus("real-names.txt").Take(10).ToArray();
        var files = names.Select(name => Entry(@"D:\Set", name, ".zip")).ToArray();

        var inspector = new FakeArchiveInspector();
        foreach (var file in files)
        {
            inspector.Entries[file.FullPath] = new[] { new ArchiveEntry("rom.bin", 1) };
        }

        inspector.Entries[files[0].FullPath] = new[] { new ArchiveEntry("a.bin", 1), new ArchiveEntry("b.bin", 2) };

        var report = Build(new InMemoryFileSystemReader(new[] { new DirectoryListing(@"D:\Set", "Set", files) }), inspector).Scan(@"D:\Set");

        Assert.Equal(10, report.Units.Count);
        var anomalous = Assert.Single(report.Anomalies).Unit;
        Assert.Equal(files[0].FullPath, anomalous.PrimaryPath);
        var anomaly = Assert.Single(anomalous.Anomalies);
        Assert.Equal("multiple-roms-in-archive", anomaly.Code);
        Assert.Contains("2 entries", anomaly.Detail, StringComparison.Ordinal);
    }

    // Gate 8. Entry lists are read without extraction, against a genuine zip.
    [Fact]
    public void Archive_entries_are_read_without_extracting()
    {
        var root = TempRoot.Create();
        try
        {
            var archivePath = Path.Combine(root, "Tekken 3 (USA) (En,Fr,De).zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("Tekken 3 (USA).bin");
            }

            var inspector = new ArchiveInspector(new FileSystemReader(), new[] { ".zip" });

            Assert.True(inspector.TryReadEntries(archivePath, out var entries, out var error), error);
            var entry = Assert.Single(entries);
            Assert.Equal("Tekken 3 (USA).bin", entry.Name);

            // Nothing was unpacked next to the archive.
            Assert.Equal(new[] { archivePath }, Directory.GetFiles(root));
            Assert.Empty(Directory.GetDirectories(root));
        }
        finally
        {
            TempRoot.Delete(root);
        }
    }

    [Fact]
    public void Unreadable_archive_becomes_an_anomaly_rather_than_a_thrown_scan()
    {
        var root = TempRoot.Create();
        try
        {
            var path = Path.Combine(root, "Tekken 3 (USA).zip");
            File.WriteAllText(path, "this is not a zip");

            var inspector = new ArchiveInspector(new FileSystemReader(), new[] { ".zip" });

            Assert.False(inspector.TryReadEntries(path, out var entries, out var error));
            Assert.Empty(entries);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
        finally
        {
            TempRoot.Delete(root);
        }
    }

    // Part B: the disc resolver claims disc shapes and declines everything else. Phase 4 asserted
    // it claimed nothing at all; Phase 10 implemented it, so the contract this pins is now the
    // narrower one — a cue is a disc, a cartridge ROM is not.
    [Fact]
    public void Disc_resolver_claims_disc_shapes_and_nothing_else()
    {
        var resolver = new DiscUnitResolver(Tokenizer, NullArchiveInspector(), new InMemoryFileSystemReader([]));

        Assert.Equal(GameUnitKind.Disc, resolver.Kind);
        Assert.True(resolver.CanResolve(Entry(@"D:\Set", "Whatever (USA)", ".cue")));
        Assert.True(resolver.CanResolve(Entry(@"D:\Set", "Whatever (USA)", ".iso")));
        Assert.False(resolver.CanResolve(Entry(@"D:\Set", "Whatever (USA)", ".sfc")));

        // A bare .bin is deliberately not claimed: a Mega Drive ROM and an orphaned disc track are
        // indistinguishable by extension, and claiming it would file the Genesis library as discs.
        Assert.False(resolver.CanResolve(Entry(@"D:\Set", "Whatever (USA)", ".bin")));
    }

    [Fact]
    public void Unit_records_its_set_folder_and_format_qualifier()
    {
        var files = TestFixtures.ReadCorpus("real-names.txt").Take(10)
            .Select(name => Entry(@"D:\Nintendo 64 (BigEndian)", name, ".zip")).ToArray();

        var report = Build(new InMemoryFileSystemReader(
            new[] { new DirectoryListing(@"D:\Nintendo 64 (BigEndian)", "Nintendo 64 (BigEndian)", files) }))
            .Scan(@"D:\Nintendo 64 (BigEndian)");

        var unit = report.Units[0].Unit;
        Assert.Equal("Nintendo 64 (BigEndian)", unit.SetFolder);
        Assert.Contains("BigEndian", unit.FormatQualifiers);
        Assert.Equal(GameUnitKind.Cartridge, unit.Kind);
        Assert.Single(unit.Files);
    }

    private static FileEntry Entry(string directory, string name, string extension) =>
        new(Path.Combine(directory, name + extension), name + extension, extension, 1024, DateTimeOffset.UnixEpoch);

    private static string Snapshot(string root) => string.Join("\n", Directory
        .EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => File.Exists(path) ? $"{path}:{new FileInfo(path).Length}" : path));

    private static FakeArchiveInspector NullArchiveInspector() => new() { Enabled = false };
}
