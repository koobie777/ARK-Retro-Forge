using ARK.Core.Configuration;
using ARK.Core.Dat;
using ARK.Core.Dedup;
using ARK.Core.Execution;
using ARK.Core.Hashing;
using ARK.Core.Instances;
using ARK.Core.Naming;
using ARK.Core.Scanning;
using ARK.Core.Units;
using ARK.Core.Verification;

namespace ARK.Tests;

/// <summary>
/// Phase 10 — what operations may do to a disc unit and to a multi-disc set.
/// </summary>
/// <remarks>
/// Prohibition 4 was written for exactly this: "deleting a byte-identical Disc 2 out of an
/// otherwise complete set." Until this phase a unit was one file and the prohibition cost nothing
/// to honour. These tests are where it starts having teeth.
/// </remarks>
public sealed class DiscOperationTests : IDisposable
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);

    private readonly string _root = TempRoot.Create();
    private readonly List<HashCache> _caches = new();

    public void Dispose()
    {
        foreach (var cache in _caches)
        {
            cache.Close();
        }

        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // ---- Gate 9: a disc unit verifies only when every constituent matches ----

    [Fact]
    public void A_disc_unit_verifies_only_when_every_constituent_file_matches()
    {
        var cueBytes = DiscUnitTests.Cue("Ridge Racer (USA).bin");
        var binBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var report = VerifyDisc(cueBytes, binBytes, cueBytes, binBytes);

        var unit = Assert.Single(report.Units);
        Assert.Equal(VerificationState.Verified, unit.State);
        Assert.Contains("2 constituent", unit.Detail, StringComparison.Ordinal);
    }

    // The load-bearing half. One track is corrupt; the cue is perfect. Judging the unit on its
    // first file would call the disc good.
    [Fact]
    public void One_corrupt_track_makes_the_whole_disc_unit_mismatched()
    {
        var cueBytes = DiscUnitTests.Cue("Ridge Racer (USA).bin");
        var good = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var corrupt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 9 };

        var report = VerifyDisc(cueBytes, corrupt, cueBytes, good);

        var unit = Assert.Single(report.Units);
        Assert.Equal(VerificationState.Mismatched, unit.State);
        Assert.Contains("Ridge Racer (USA).bin", unit.Detail, StringComparison.Ordinal);
    }

    // Gate 21 — Phase 5's cache gate still holds for disc units. Constituents are cached per entry,
    // keyed on the containing archive's size and mtime, so a second pass over an unchanged disc set
    // reads nothing. Without this a 490 GB set re-hashes in full on every run.
    [Fact]
    public void A_second_pass_over_an_unchanged_disc_unit_hashes_nothing()
    {
        var cueBytes = DiscUnitTests.Cue("Ridge Racer (USA).bin");
        var binBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var cache = new HashCache(Paths());
        _caches.Add(cache);

        var first = VerifyDisc(cueBytes, binBytes, cueBytes, binBytes, cache);
        Assert.Equal(2, first.Hashed);

        var second = VerifyDisc(cueBytes, binBytes, cueBytes, binBytes, cache);
        Assert.Equal(0, second.Hashed);
        Assert.Equal(VerificationState.Verified, Assert.Single(second.Units).State);
    }

    // ---- Gate 11: a cue whose hash does not match is reported, not repaired ----

    [Fact]
    public void A_mismatching_cue_is_reported_and_the_bytes_are_left_alone()
    {
        var onDisk = DiscUnitTests.Cue("Ridge Racer (USA) (Track 1).bin");
        var inDat = DiscUnitTests.Cue("Ridge Racer (USA).bin");
        var binBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var report = VerifyDisc(onDisk, binBytes, inDat, binBytes);

        var unit = Assert.Single(report.Units);
        Assert.Equal(VerificationState.Mismatched, unit.State);
        Assert.Contains("Ridge Racer (USA).cue", unit.Detail, StringComparison.Ordinal);

        // Reported is the whole of it. Verification produces no plan and no actions, so there is
        // nothing that could rewrite the sheet even if something wanted to.
        Assert.Equal(VerificationState.Mismatched, unit.State);
        Assert.False(unit.IsRenameEligible);
    }

    // Found on the real drive, not predicted: one PlayStation archive in 1,765 holds a lone .bin
    // and no cue sheet. Its DAT game declares both a .bin and a .cue, so judging the BIN against
    // whichever entry indexed first would report corruption on a healthy file.
    [Fact]
    public void A_lone_bin_is_judged_against_the_bin_entry_not_the_cue_entry()
    {
        const string directory = @"D:\Sony - PlayStation";
        var binBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var archive = new FileEntry(
            Path.Combine(directory, "MediEvil (USA) (Demo 2).zip"),
            "MediEvil (USA) (Demo 2).zip", ".zip", 4096, DateTimeOffset.UnixEpoch);

        var inspector = new FakeArchiveInspector();
        inspector.Entries[archive.FullPath] = new[] { new ArchiveEntry("MediEvil (USA) (Demo 2).bin", binBytes.Length) };
        inspector.Contents[archive.FullPath] = binBytes;

        var unit = new GameUnit(
            GameUnitKind.Cartridge, archive.FullPath, new[] { archive },
            Tokenizer.Parse("MediEvil (USA) (Demo 2)"), "Sony - PlayStation",
            Array.Empty<string>(), inspector.Entries[archive.FullPath], Array.Empty<GameUnitIssue>());

        // The cue entry is listed first, exactly as a DAT bucket may order them.
        var entries = new[]
        {
            new CatalogEntry
            {
                GameName = "MediEvil (USA) (Demo 2)", RomName = "MediEvil (USA) (Demo 2).cue",
                Crc32 = "deadbeef", DatName = "Sony - PlayStation",
            },
            new CatalogEntry
            {
                GameName = "MediEvil (USA) (Demo 2)", RomName = "MediEvil (USA) (Demo 2).bin",
                Crc32 = Crc32Of(binBytes), DatName = "Sony - PlayStation",
            },
        };

        var scan = new ScanReport(
            directory,
            new[] { new ScannedDirectory(Profile(directory), new DatScope("Sony - PlayStation", "psx", null, DatScopeSource.DirectoryName)) },
            new[] { new ScannedUnit(unit, entries[0], entries) },
            Array.Empty<ExcludedFile>(),
            Array.Empty<FileEntry>());

        var reader = new InMemoryFileSystemReader(
            new[] { new DirectoryListing(directory, "Sony - PlayStation", new[] { archive }) });

        var cache = new HashCache(Paths());
        _caches.Add(cache);

        var report = new VerificationService(
            new RomHasher(reader, inspector),
            cache,
            new InProgressDetector(Array.Empty<string>(), Array.Empty<string>(), TimeSpan.Zero),
            Vocabulary).Verify(scan);

        Assert.Equal(VerificationState.Verified, Assert.Single(report.Units).State);
    }

    // ---- Gate 16: no operation acts on a constituent file independently of its unit ----

    [Fact]
    public void Quarantining_a_loose_disc_unit_moves_every_constituent_file()
    {
        var unit = LooseUnit("Ridge Racer (USA)", "Ridge Racer (USA).bin", "Ridge Racer (USA) (Track 2).bin");

        var plan = QuarantinePlanner.Build(
            _root,
            new[] { Request(unit, "duplicate") },
            "session-1",
            DateTimeOffset.UnixEpoch,
            "test");

        var moved = plan.Plan.Actions
            .Where(action => action.Kind == ActionKind.Quarantine)
            .Select(action => Path.GetFileName(action.Source))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "Ridge Racer (USA) (Track 2).bin", "Ridge Racer (USA).bin", "Ridge Racer (USA).cue" },
            moved);
    }

    // ---- Gate 17: a multi-disc set is removed whole or not at all ----

    [Fact]
    public void Removing_one_disc_of_a_set_is_refused()
    {
        var (sets, discOne, _) = TwoDiscSet();

        var plan = QuarantinePlanner.Build(
            _root,
            new[] { Request(discOne, "byte-identical to another release's disc", sets) },
            "session-1",
            DateTimeOffset.UnixEpoch,
            "test",
            discSets: sets);

        Assert.Empty(plan.Plan.Actions);
        var refusal = Assert.Single(plan.Refused);
        Assert.Contains("1 of 2 discs", refusal.Reason, StringComparison.Ordinal);
        Assert.Contains("whole or not at all", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_every_disc_of_a_set_is_allowed()
    {
        var (sets, discOne, discTwo) = TwoDiscSet();

        var plan = QuarantinePlanner.Build(
            _root,
            new[] { Request(discOne, "policy", sets), Request(discTwo, "policy", sets) },
            "session-1",
            DateTimeOffset.UnixEpoch,
            "test",
            discSets: sets);

        Assert.Empty(plan.Refused);
        Assert.Equal(4, plan.Plan.Actions.Count(action => action.Kind == ActionKind.Quarantine));
    }

    // ---- Gate 18: set completeness reflects every disc ----

    [Fact]
    public void One_damaged_disc_makes_the_whole_set_incomplete()
    {
        var (sets, one, two) = TwoDiscSet();

        var damaged = DiscSetCompleteness.Evaluate(
            sets,
            Report((one, VerificationState.Verified), (two, VerificationState.Mismatched)));

        var verdict = Assert.Single(damaged);
        Assert.Equal(DiscSetState.Damaged, verdict.State);
        Assert.False(verdict.IsComplete);
        Assert.Contains("1 of 2 discs mismatched", verdict.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_set_is_complete_only_when_every_disc_verifies()
    {
        var (sets, one, two) = TwoDiscSet();

        var complete = Assert.Single(DiscSetCompleteness.Evaluate(
            sets, Report((one, VerificationState.Verified), (two, VerificationState.Verified))));
        Assert.Equal(DiscSetState.Complete, complete.State);

        // An unjudged disc is not a passing disc. A set is never complete on an absence of bad news.
        var partial = Assert.Single(DiscSetCompleteness.Evaluate(
            sets, Report((one, VerificationState.Verified))));
        Assert.Equal(DiscSetState.Unresolved, partial.State);
    }

    // ---- Gates 10 and 16 at the rename boundary ----

    // Renaming a loose cue's tracks would invalidate the FILE lines naming them, and the only way
    // to keep the unit coherent is to rewrite the cue. No cue is written, so the unit is refused
    // whole rather than renaming the sheet and orphaning what it points at.
    [Fact]
    public void Renaming_a_loose_disc_unit_is_refused_rather_than_rewriting_its_cue()
    {
        var unit = LooseUnit("ridge racer (usa)", "ridge racer (usa).bin");

        var entry = new CatalogEntry
        {
            GameName = "Ridge Racer (USA)",
            RomName = "Ridge Racer (USA).cue",
            Crc32 = "00000000",
            DatName = "Sony - PlayStation",
        };

        var scan = new ScanReport(
            _root,
            new[] { new ScannedDirectory(Profile(_root), new DatScope("Sony - PlayStation", "psx", null, DatScopeSource.DirectoryName)) },
            new[] { new ScannedUnit(unit, entry, new[] { entry }) },
            Array.Empty<ExcludedFile>(),
            Array.Empty<FileEntry>());

        var report = new ARK.Core.Renaming.RenameService(new NameFormatter(Tokenizer)).Decide(
            scan,
            Report((unit, VerificationState.Verified)),
            ARK.Core.Renaming.RenameMode.Canonicalize);

        var decision = Assert.Single(report.Decisions);
        Assert.Equal(ARK.Core.Renaming.RenameOutcome.Refused, decision.Outcome);
        Assert.Equal(ARK.Core.Renaming.RenameRefusal.LooseDiscUnit, decision.Refusal);
    }

    // ---- Gate 20: DRY-RUN by default, journaled, reversed exactly ----

    [Fact]
    public void A_disc_unit_quarantine_is_dry_run_by_default_and_undoes_exactly()
    {
        var directory = Path.Combine(_root, "Sony - PlayStation");
        Directory.CreateDirectory(directory);

        var names = new[] { "Ridge Racer (USA).cue", "Ridge Racer (USA).bin", "Ridge Racer (USA) (Track 2).bin" };
        foreach (var name in names)
        {
            File.WriteAllBytes(Path.Combine(directory, name), new byte[] { 1, 2, 3 });
        }

        var before = Snapshot(_root);
        var unit = LooseUnit("Ridge Racer (USA)", names[1], names[2], directory);

        var plan = QuarantinePlanner.Build(
            _root, new[] { Request(unit, "duplicate") }, "session-1", DateTimeOffset.UnixEpoch, "test");

        var paths = new InstancePaths("disc-undo", Path.Combine(_root, "instance"));
        Directory.CreateDirectory(paths.Journal);
        var journals = new JournalStore(paths);
        var executor = new Executor(paths);

        // DRY-RUN: the plan exists, the filesystem does not change.
        executor.Execute(plan.Plan, apply: false);
        Assert.Equal(before, Snapshot(_root));

        var applied = executor.Execute(plan.Plan, apply: true);
        Assert.True(applied.Success);
        Assert.NotEqual(before, Snapshot(_root));

        // Every constituent left together.
        foreach (var name in names)
        {
            Assert.False(File.Exists(Path.Combine(directory, name)));
        }

        var undo = new UndoService(journals, executor);
        var (preview, result) = undo.Apply(journals.List().Single().SessionId);

        Assert.True(preview.CanApply);
        Assert.True(result!.Success);
        Assert.Equal(before, Snapshot(_root));
    }

    // ---- helpers ----

    private (IReadOnlyList<DiscSet> Sets, GameUnit One, GameUnit Two) TwoDiscSet()
    {
        var one = LooseUnit("Final Fantasy VII (USA) (Disc 1)", "Final Fantasy VII (USA) (Disc 1).bin");
        var two = LooseUnit("Final Fantasy VII (USA) (Disc 2)", "Final Fantasy VII (USA) (Disc 2).bin");

        var sets = DiscSetGrouper.Group(new[] { one, two }, Vocabulary);
        Assert.Single(sets);
        Assert.True(sets[0].IsMultiDisc);

        return (sets, one, two);
    }

    private GameUnit LooseUnit(string stem, params string[] trackNames) =>
        LooseUnit(stem, trackNames[0], trackNames.Length > 1 ? trackNames[1] : null, Path.Combine(_root, "Set"));

    private GameUnit LooseUnit(string stem, string firstTrack, string? secondTrack, string directory)
    {
        var files = new List<FileEntry> { Entry(directory, stem + ".cue") , Entry(directory, firstTrack) };
        if (secondTrack is not null)
        {
            files.Add(Entry(directory, secondTrack));
        }

        return new GameUnit(
            GameUnitKind.Disc,
            files[0].FullPath,
            files,
            Tokenizer.Parse(stem),
            "Sony - PlayStation",
            Array.Empty<string>(),
            Array.Empty<ArchiveEntry>(),
            Array.Empty<GameUnitIssue>());
    }

    private static VerificationReport Report(params (GameUnit Unit, VerificationState State)[] judged) =>
        new(
            @"D:\Sony - PlayStation",
            judged.Select(entry => new VerifiedUnit(
                entry.Unit.PrimaryPath,
                entry.Unit.Files[0].Name,
                entry.Unit.SetFolder,
                "Sony - PlayStation",
                entry.State,
                entry.State.ToString())).ToArray(),
            judged.Length,
            0,
            TimeSpan.Zero,
            0);

    private static FileEntry Entry(string directory, string name) =>
        new(Path.Combine(directory, name), name, Path.GetExtension(name).ToLowerInvariant(), 1024, DateTimeOffset.UnixEpoch);

    private static QuarantineRequest Request(GameUnit unit, string reason, IReadOnlyList<DiscSet>? sets = null) =>
        new(
            unit.PrimaryPath,
            unit.Files[0].Name,
            null,
            null,
            unit.TotalSize,
            "kept elsewhere",
            reason,
            unit.Files.Select(file => file.FullPath).ToArray(),
            QuarantinePlanner.SetKeysByPath(sets).GetValueOrDefault(unit.PrimaryPath));

    /// <summary>
    /// Verifies one archived disc unit holding a cue and a bin, against a DAT declaring a hash for
    /// each.
    /// </summary>
    private VerificationReport VerifyDisc(
        byte[] cueOnDisk,
        byte[] binOnDisk,
        byte[] cueInDat,
        byte[] binInDat,
        HashCache? shared = null)
    {
        const string directory = @"D:\Sony - PlayStation";
        var archive = new FileEntry(
            Path.Combine(directory, "Ridge Racer (USA).zip"), "Ridge Racer (USA).zip", ".zip", 4096, DateTimeOffset.UnixEpoch);

        var reader = new InMemoryFileSystemReader(
            new[] { new DirectoryListing(directory, "Sony - PlayStation", new[] { archive }) });

        var inspector = new FakeArchiveInspector();
        inspector.Entries[archive.FullPath] = new[]
        {
            new ArchiveEntry("Ridge Racer (USA).cue", cueOnDisk.Length),
            new ArchiveEntry("Ridge Racer (USA).bin", binOnDisk.Length),
        };
        inspector.SetEntry(archive.FullPath, "Ridge Racer (USA).cue", cueOnDisk);
        inspector.SetEntry(archive.FullPath, "Ridge Racer (USA).bin", binOnDisk);

        var unit = new GameUnit(
            GameUnitKind.Disc,
            archive.FullPath,
            new[] { archive },
            Tokenizer.Parse("Ridge Racer (USA)"),
            "Sony - PlayStation",
            Array.Empty<string>(),
            inspector.Entries[archive.FullPath],
            Array.Empty<GameUnitIssue>());

        var entries = new[]
        {
            Catalog("Ridge Racer (USA).cue", Crc32Of(cueInDat)),
            Catalog("Ridge Racer (USA).bin", Crc32Of(binInDat)),
        };

        var scan = new ScanReport(
            directory,
            new[] { new ScannedDirectory(Profile(directory), new DatScope("Sony - PlayStation", "psx", null, DatScopeSource.DirectoryName)) },
            new[] { new ScannedUnit(unit, entries[0], entries) },
            Array.Empty<ExcludedFile>(),
            Array.Empty<FileEntry>());

        var cache = shared;
        if (cache is null)
        {
            cache = new HashCache(Paths());
            _caches.Add(cache);
        }

        return new VerificationService(
            new RomHasher(reader, inspector),
            cache,
            new InProgressDetector(Array.Empty<string>(), Array.Empty<string>(), TimeSpan.Zero),
            Vocabulary)
            .Verify(scan);
    }

    private InstancePaths Paths()
    {
        var path = Path.Combine(_root, "instance-" + Guid.NewGuid().ToString("N"));
        var paths = new InstancePaths("disc", path);
        Directory.CreateDirectory(paths.Db);
        return paths;
    }

    private static CatalogEntry Catalog(string romName, string crc) =>
        new() { GameName = "Ridge Racer (USA)", RomName = romName, Crc32 = crc, DatName = "Sony - PlayStation" };

    private static DirectoryProfile Profile(string directory) => new(
        directory, "Sony - PlayStation", 1, ".zip", 1, 1, DirectoryOutcome.RomSet, ExclusionReason.None,
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<FileEntry>());

    private static string Crc32Of(byte[] bytes)
    {
        using var crc = new Crc32Hasher();
        crc.Append(bytes);
        return Convert.ToHexString(crc.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Snapshot(string root) => string.Join("\n", Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !path.Contains("instance", StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => $"{path}:{new FileInfo(path).Length}"));
}
