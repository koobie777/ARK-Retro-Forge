using System.IO.Compression;
using System.Reflection;
using ARK.Core.Dat;
using ARK.Core.Hashing;
using ARK.Core.Instances;
using ARK.Core.Naming;
using ARK.Core.Configuration;
using ARK.Core.Scanning;
using ARK.Core.Units;
using ARK.Core.Verification;

namespace ARK.Tests;

/// <summary>
/// Phase 5 Part C: the five states, and the rules that keep the report worth reading.
/// </summary>
public sealed class VerificationTests : IDisposable
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);

    private readonly string _root = TempRoot.Create();
    private readonly List<HashCache> _caches = new();

    public void Dispose()
    {
        // SQLite keeps the file handle until the connection closes, and the temp root cannot be
        // removed while it is open.
        foreach (var cache in _caches)
        {
            cache.Close();
        }

        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Gate 13, and gate 14's core: correct name, correct size, wrong bytes.
    [Fact]
    public void Correct_name_and_size_with_corrupt_contents_reports_Mismatched()
    {
        var rom = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var corrupt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 9 };

        // The DAT declares the good hash; the file on disk holds the corrupt bytes of equal length.
        var report = Verify(("Tekken 3 (USA).zip", corrupt, Entry("Tekken 3 (USA)", Crc32Of(rom))));

        var unit = Assert.Single(report.InState(VerificationState.Mismatched));
        Assert.Equal(Crc32Of(corrupt), unit.ActualCrc32);
        Assert.Equal(Crc32Of(rom), unit.ExpectedCrc32);

        // Diagnosable, not a shrug: it names what it claims to be.
        Assert.Contains("Tekken 3 (USA)", unit.Detail, StringComparison.Ordinal);
        Assert.Empty(report.InState(VerificationState.Verified));
        Assert.Empty(report.InState(VerificationState.Unrecognized));
    }

    [Fact]
    public void Matching_hash_reports_Verified()
    {
        var rom = new byte[] { 10, 20, 30, 40 };

        var report = Verify(("Tekken 3 (USA).zip", rom, Entry("Tekken 3 (USA)", Crc32Of(rom))));

        var unit = Assert.Single(report.InState(VerificationState.Verified));
        Assert.Equal("hash matches the DAT entry", unit.Detail);
    }

    // Gates 8 and 9. The qualifier selects which DAT to compare against; it does not command a
    // rewrite. A headered ROM matches the Headered DAT as it is, untransformed.
    [Theory]
    [InlineData(new byte[] { 0x4E, 0x45, 0x53, 0x1A, 0, 0, 0, 0 }, RomFormat.NesINes, "Headered")]
    [InlineData(new byte[] { 0x80, 0x37, 0x12, 0x40, 1, 2, 3, 4 }, RomFormat.N64BigEndian, "BigEndian")]
    public void Rom_matches_its_own_dat_variant_with_no_transformation(byte[] rom, RomFormat expected, string qualifier)
    {
        var file = new FileEntry(@"D:\Set\Game (USA).zip", "Game (USA).zip", ".zip", rom.Length, DateTimeOffset.UnixEpoch);
        var reader = new InMemoryFileSystemReader(new[] { new DirectoryListing(@"D:\Set", "Set", new[] { file }) });
        var inspector = new FakeArchiveInspector();
        inspector.Contents[file.FullPath] = rom;

        var scan = ScanOf(file, Entry("Game (USA)", Crc32Of(rom)), qualifier);
        var report = Service(reader, inspector).Verify(scan);

        var unit = Assert.Single(report.InState(VerificationState.Verified));
        Assert.Equal(expected, unit.Format);
        Assert.Equal(qualifier, unit.FormatQualifier);

        // The bytes hashed are the bytes on disk: no stripping, no byte-order conversion.
        Assert.False(unit.Normalized);
        Assert.Equal(Crc32Of(rom), unit.ActualCrc32);

        // Detected format agrees with the folder, so there is nothing to report.
        Assert.Null(unit.FormatContradiction);
        Assert.Empty(report.FormatContradictions);
    }

    // Gate 11 end-to-end: the file's own bytes disagree with the folder it sits in.
    [Fact]
    public void Format_contradicting_the_folder_qualifier_is_surfaced_in_the_report()
    {
        var byteSwapped = new byte[] { 0x37, 0x80, 0x40, 0x12, 1, 2, 3, 4 };
        var file = new FileEntry(@"D:\Set\Game (USA).zip", "Game (USA).zip", ".zip", byteSwapped.Length, DateTimeOffset.UnixEpoch);
        var reader = new InMemoryFileSystemReader(new[] { new DirectoryListing(@"D:\Set", "Set", new[] { file }) });
        var inspector = new FakeArchiveInspector();
        inspector.Contents[file.FullPath] = byteSwapped;

        // The folder says BigEndian; the bytes say ByteSwapped.
        var report = Service(reader, inspector)
            .Verify(ScanOf(file, Entry("Game (USA)", Crc32Of(byteSwapped)), "BigEndian"));

        var unit = Assert.Single(report.FormatContradictions);
        Assert.Contains("ByteSwapped", unit.FormatContradiction!, StringComparison.Ordinal);
        Assert.Contains("BigEndian", unit.FormatContradiction!, StringComparison.Ordinal);
    }

    [Fact]
    public void No_dat_entry_reports_Unrecognized()
    {
        var report = Verify(("Mystery (USA).zip", new byte[] { 1, 2, 3 }, null));

        Assert.Single(report.InState(VerificationState.Unrecognized));
    }

    // Gate 15.
    [Fact]
    public void Incomplete_download_extension_reports_In_Progress()
    {
        var rom = new byte[] { 1, 2, 3, 4 };

        // Same bytes, but the name carries a client's in-flight extension.
        var report = Verify(
            new[] { ("Tekken 3 (USA).part", rom, (CatalogEntry?)Entry("Tekken 3 (USA)", "ffffffff")) },
            extensions: new[] { ".part" });

        var unit = Assert.Single(report.InState(VerificationState.InProgress));
        Assert.Equal(InProgressSignal.IncompleteExtension, unit.Signal);

        // Not judged: it never reached a hash comparison at all.
        Assert.Empty(report.InState(VerificationState.Mismatched));
        Assert.Null(unit.ActualCrc32);
    }

    // Gate 13's ordering rule, asserted directly: a file that would fail its hash and is also
    // in progress must report In Progress, never Mismatched.
    [Fact]
    public void In_Progress_is_evaluated_before_Mismatched()
    {
        var report = Verify(
            new[] { ("Tekken 3 (USA).part", new byte[] { 9, 9, 9 }, (CatalogEntry?)Entry("Tekken 3 (USA)", "00000000")) },
            extensions: new[] { ".part" });

        Assert.Single(report.InState(VerificationState.InProgress));
        Assert.Empty(report.InState(VerificationState.Mismatched));
    }

    [Fact]
    public void Declared_incomplete_directory_reports_In_Progress()
    {
        var report = Verify(
            new[] { ("Tekken 3 (USA).zip", new byte[] { 1 }, (CatalogEntry?)null) },
            declaredDirectories: new[] { @"D:\Set" });

        Assert.Equal(InProgressSignal.DeclaredDirectory, Assert.Single(report.InState(VerificationState.InProgress)).Signal);
    }

    // Gate 15a. The file's bytes moved under the read, so the hash describes a state that is gone.
    [Fact]
    public void File_changing_during_hashing_reports_In_Progress_and_is_never_cached()
    {
        var file = new FileEntry(@"D:\Set\Tekken 3 (USA).zip", "Tekken 3 (USA).zip", ".zip", 8, DateTimeOffset.UnixEpoch);
        var reader = new InMemoryFileSystemReader(new[] { new DirectoryListing(@"D:\Set", "Set", new[] { file }) });
        var inspector = new FakeArchiveInspector();
        inspector.Contents[file.FullPath] = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        // Simulate a concurrent write: the timestamp advances while the entry is being read.
        inspector.OnOpenEntry = _ => reader.Overrides[file.FullPath] =
            file with { ModifiedUtc = file.ModifiedUtc.AddSeconds(30) };

        var paths = new InstancePaths("changed-during-read", _root);
        Directory.CreateDirectory(paths.Db);
        var cache = new HashCache(paths);

        var report = new VerificationService(new RomHasher(reader, inspector), cache, new InProgressDetector())
            .Verify(ScanOf(file, Entry("Tekken 3 (USA)", "aaaaaaaa")));

        var unit = Assert.Single(report.InState(VerificationState.InProgress));
        Assert.Equal(InProgressSignal.ChangedDuringRead, unit.Signal);
        Assert.Empty(report.InState(VerificationState.Mismatched));

        // The discarded hash was never written to the cache.
        Assert.Equal(0, cache.Count());
        cache.Close();
    }

    // Gate 15b. A truncated abandoned download and an actively growing one are structurally
    // identical; only the in-progress signals separate them.
    [Fact]
    public void Unreadable_archive_that_is_still_being_written_reports_In_Progress_not_damage()
    {
        var recent = new FileEntry(@"D:\Set\Growing (USA).zip", "Growing (USA).zip", ".zip", 8, DateTimeOffset.UtcNow);
        var stale = new FileEntry(@"D:\Set\Truncated (USA).zip", "Truncated (USA).zip", ".zip", 8, DateTimeOffset.UnixEpoch);

        var reader = new InMemoryFileSystemReader(new[]
        {
            new DirectoryListing(@"D:\Set", "Set", new[] { recent, stale }),
        });

        var inspector = new FakeArchiveInspector();
        inspector.Unreadable.Add(recent.FullPath);
        inspector.Unreadable.Add(stale.FullPath);

        var report = Service(reader, inspector).Verify(ScanOf(new[] { (recent, (CatalogEntry?)null), (stale, null) }));

        // Same failure to open; opposite verdicts, decided by whether it is still moving.
        Assert.Equal(recent.FullPath, Assert.Single(report.InState(VerificationState.InProgress)).Path);
        Assert.Equal(stale.FullPath, Assert.Single(report.InState(VerificationState.Unrecognized)).Path);
    }

    // Gate 16. Enforced now even though renaming does not exist: a corrupt file renamed to its
    // canonical name looks verified forever after.
    [Fact]
    public void Only_Verified_units_are_rename_eligible()
    {
        var good = new byte[] { 1, 2, 3, 4 };

        var report = Verify(
            ("Good (USA).zip", good, Entry("Good (USA)", Crc32Of(good))),
            ("Bad (USA).zip", new byte[] { 9, 9, 9, 9 }, Entry("Bad (USA)", "00000000")),
            ("Unknown (USA).zip", new byte[] { 5 }, null));

        var eligible = Assert.Single(report.RenameEligible);
        Assert.Equal("Good (USA).zip", eligible.Name);
        Assert.All(report.Units.Where(unit => unit.State != VerificationState.Verified),
            unit => Assert.False(unit.IsRenameEligible));
    }

    [Fact]
    public void Excluded_files_are_reported_without_being_hashed()
    {
        var file = new FileEntry(@"D:\Set\notes.txt", "notes.txt", ".txt", 4, DateTimeOffset.UnixEpoch);
        var scan = new ScanReport(@"D:\", Array.Empty<ScannedDirectory>(), Array.Empty<ScannedUnit>(),
            new[] { new ExcludedFile(file, "names not conformant") }, Array.Empty<FileEntry>());

        var reader = new InMemoryFileSystemReader(Array.Empty<DirectoryListing>());
        var report = Service(reader, new FakeArchiveInspector()).Verify(scan);

        var unit = Assert.Single(report.InState(VerificationState.Excluded));
        Assert.Equal("names not conformant", unit.Detail);
        Assert.Equal(0, report.Hashed);
    }

    // Gate 4 at service level: a second pass over an unchanged set hashes approximately nothing.
    [Fact]
    public void Second_run_over_an_unchanged_set_performs_no_hashing()
    {
        var rom = new byte[] { 1, 2, 3, 4 };
        var file = new FileEntry(@"D:\Set\Game (USA).zip", "Game (USA).zip", ".zip", 4, DateTimeOffset.UnixEpoch);
        var reader = new InMemoryFileSystemReader(new[] { new DirectoryListing(@"D:\Set", "Set", new[] { file }) });
        var inspector = new FakeArchiveInspector();
        inspector.Contents[file.FullPath] = rom;

        var paths = new InstancePaths("second-run", _root);
        Directory.CreateDirectory(paths.Db);
        var cache = new HashCache(paths);
        var service = new VerificationService(new RomHasher(reader, inspector), cache, new InProgressDetector());
        var scan = ScanOf(file, Entry("Game (USA)", Crc32Of(rom)));

        var first = service.Verify(scan);
        var second = service.Verify(scan);

        Assert.Equal(1, first.Hashed);
        Assert.Equal(0, second.Hashed);
        Assert.Single(second.InState(VerificationState.Verified));
        cache.Close();
    }

    // Gate 7. Progress is reported, not optional.
    [Fact]
    public void Progress_is_reported_during_a_run()
    {
        var updates = new List<VerificationProgress>();
        var rom = new byte[] { 1 };
        var files = Enumerable.Range(0, 5)
            .Select(i => new FileEntry($@"D:\Set\Game {i} (USA).zip", $"Game {i} (USA).zip", ".zip", 1, DateTimeOffset.UnixEpoch))
            .ToArray();

        var reader = new InMemoryFileSystemReader(new[] { new DirectoryListing(@"D:\Set", "Set", files) });
        var inspector = new FakeArchiveInspector();
        foreach (var file in files)
        {
            inspector.Contents[file.FullPath] = rom;
        }

        Service(reader, inspector).Verify(
            ScanOf(files.Select(file => (file, (CatalogEntry?)null)).ToArray()),
            new SynchronousProgress<VerificationProgress>(updates.Add));

        Assert.NotEmpty(updates);
        Assert.Equal(5, updates[^1].Total);
    }

    // Gate 6. Cancelling returns what was completed rather than discarding it.
    [Fact]
    public void Cancelling_mid_run_preserves_completed_work()
    {
        var files = Enumerable.Range(0, 6)
            .Select(i => new FileEntry($@"D:\Set\Game {i} (USA).zip", $"Game {i} (USA).zip", ".zip", 1, DateTimeOffset.UnixEpoch))
            .ToArray();

        var reader = new InMemoryFileSystemReader(new[] { new DirectoryListing(@"D:\Set", "Set", files) });
        var inspector = new FakeArchiveInspector();
        foreach (var file in files)
        {
            inspector.Contents[file.FullPath] = new byte[] { 1 };
        }

        var paths = new InstancePaths("cancel", _root);
        Directory.CreateDirectory(paths.Db);
        var cache = new HashCache(paths);
        _caches.Add(cache);

        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<VerificationProgress>(update =>
        {
            if (update.Completed >= 3)
            {
                cts.Cancel();
            }
        });

        var report = new VerificationService(new RomHasher(reader, inspector), cache, new InProgressDetector())
            .Verify(ScanOf(files.Select(file => (file, (CatalogEntry?)null)).ToArray()), progress, cts.Token);

        Assert.True(report.Cancelled);
        Assert.True(report.Units.Count < files.Length, "the run stopped early");

        // Everything hashed before the interruption is already committed.
        Assert.True(cache.Count() > 0, "completed hashes survived the cancellation");
        cache.Close();
    }

    // Gate 17.
    [Fact]
    public void Verify_is_read_only_and_writes_no_journal()
    {
        var set = Path.Combine(_root, "Nintendo - Game Boy");
        Directory.CreateDirectory(set);
        foreach (var name in TestFixtures.ReadCorpus("real-names.txt").Take(8))
        {
            var path = Path.Combine(set, name + ".zip");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            using var stream = archive.CreateEntry(name + ".gb").Open();
            stream.Write(new byte[] { 1, 2, 3, 4 });
        }

        var before = Snapshot(_root);

        var rules = ScanRulesLoader.Load(TestFixtures.ShippedScanRulesPath());
        var fileSystem = new FileSystemReader();
        var inspector = new ArchiveInspector(fileSystem, new[] { ".zip" });
        var scan = new ScanService(
            fileSystem,
            new DirectoryProfiler(Tokenizer, rules),
            new IGameUnitResolver[] { new CartridgeUnitResolver(Tokenizer, inspector), new DiscUnitResolver() },
            rules).Scan(_root);

        var paths = new InstancePaths("read-only", Path.Combine(_root, "instance"));
        Directory.CreateDirectory(paths.Db);
        var cache = new HashCache(paths);
        new VerificationService(new RomHasher(fileSystem, inspector), cache, new InProgressDetector()).Verify(scan);
        cache.Close();

        // The instance directory is ARK's own; the scanned tree itself is untouched.
        Assert.Equal(before, Snapshot(set));
        Assert.False(Directory.Exists(Path.Combine(_root, "journal")));
        Assert.False(Directory.Exists(paths.Journal));
    }

    // Gate 18. One serializer feeds both the JSON and this test, so they cannot drift.
    [Fact]
    public void Human_and_json_output_carry_the_same_fields()
    {
        var rom = new byte[] { 1, 2, 3, 4 };
        var report = Verify(("Game (USA).zip", rom, Entry("Game (USA)", Crc32Of(rom))));

        var json = VerificationJson.Serialize(report);

        foreach (var property in typeof(VerificationReport).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.Contains($"\"{property.Name}\"", json, StringComparison.Ordinal);
        }

        foreach (var property in typeof(VerifiedUnit).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.Contains($"\"{property.Name}\"", json, StringComparison.Ordinal);
        }

        // States serialize by name, not as opaque integers.
        Assert.Contains("\"Verified\"", json, StringComparison.Ordinal);
    }

    private VerificationReport Verify(params (string Name, byte[] Content, CatalogEntry? Match)[] files) =>
        Verify(files, null, null);

    private VerificationReport Verify(
        (string Name, byte[] Content, CatalogEntry? Match)[] files,
        IEnumerable<string>? extensions = null,
        IEnumerable<string>? declaredDirectories = null)
    {
        var entries = files
            .Select(file => new FileEntry(
                $@"D:\Set\{file.Name}",
                file.Name,
                Path.GetExtension(file.Name).ToLowerInvariant(),
                file.Content.Length,
                DateTimeOffset.UnixEpoch))
            .ToArray();

        var reader = new InMemoryFileSystemReader(new[] { new DirectoryListing(@"D:\Set", "Set", entries) });
        var inspector = new FakeArchiveInspector();
        inspector.Extensions.Add(".part");
        for (var i = 0; i < files.Length; i++)
        {
            inspector.Contents[entries[i].FullPath] = files[i].Content;
        }

        var service = Service(reader, inspector, extensions, declaredDirectories);
        return service.Verify(ScanOf(entries.Zip(files, (entry, file) => (entry, file.Match)).ToArray()));
    }

    private VerificationService Service(
        InMemoryFileSystemReader reader,
        FakeArchiveInspector inspector,
        IEnumerable<string>? extensions = null,
        IEnumerable<string>? declaredDirectories = null)
    {
        var paths = new InstancePaths($"verify-{Guid.NewGuid():N}", _root);
        Directory.CreateDirectory(paths.Db);
        var cache = new HashCache(paths);
        _caches.Add(cache);
        return new VerificationService(
            new RomHasher(reader, inspector),
            cache,
            new InProgressDetector(extensions, declaredDirectories));
    }

    private static ScanReport ScanOf(FileEntry file, CatalogEntry? match, string? qualifier = null) =>
        ScanOf(new[] { (file, match) }, qualifier);

    private static ScanReport ScanOf((FileEntry File, CatalogEntry? Match)[] units, string? qualifier = null)
    {
        var qualifiers = qualifier is null ? Array.Empty<string>() : new[] { qualifier };
        var profile = new DirectoryProfile(
            @"D:\Set", "Set", units.Length, ".zip", 1, 1, DirectoryOutcome.RomSet, ExclusionReason.None,
            qualifiers, Array.Empty<string>(), Array.Empty<FileEntry>());

        var scanned = units.Select(unit => new ScannedUnit(
            new GameUnit(
                GameUnitKind.Cartridge,
                unit.File.FullPath,
                new[] { unit.File },
                Tokenizer.Parse(unit.File.NameWithoutExtension),
                "Set",
                qualifiers,
                Array.Empty<ArchiveEntry>(),
                Array.Empty<GameUnitIssue>()),
            unit.Match)).ToArray();

        return new ScanReport(
            @"D:\",
            new[] { new ScannedDirectory(profile, new DatScope("Test DAT", null, null, DatScopeSource.DirectoryName)) },
            scanned,
            Array.Empty<ExcludedFile>(),
            Array.Empty<FileEntry>());
    }

    private static CatalogEntry Entry(string name, string crc) =>
        new() { GameName = name, RomName = name + ".gb", Crc32 = crc, DatName = "Test DAT" };

    private static string Crc32Of(byte[] bytes)
    {
        using var crc = new Crc32Hasher();
        crc.Append(bytes);
        return Convert.ToHexString(crc.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Reports synchronously. <see cref="Progress{T}"/> posts to the thread pool, so assertions on
    /// what it collected can run before the callbacks do — a race that makes these tests flaky.
    /// </summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SynchronousProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }

    private static string Snapshot(string root) => string.Join("\n", Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => $"{path}:{new FileInfo(path).Length}"));
}
