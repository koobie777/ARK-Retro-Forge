using System.IO.Compression;
using System.Text.Json;
using ARK.Core.Configuration;
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
/// Phase 7: byte-identical units collapse to one, and every APPLY is reversible.
/// </summary>
/// <remarks>
/// This is the first phase that moves the user's ROMs, so the standard is higher: a dedup that
/// works but cannot be reversed is a failed phase, not a partial one. Every gate here that applies
/// also asserts <c>ark undo</c> puts the set back exactly.
/// </remarks>
public sealed class DedupTests : IDisposable
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

    // Gate 5. Zip compression is not deterministic, so identical ROMs sit inside entirely
    // different archive bytes. Comparing archives would miss every real duplicate.
    [Fact]
    public void Identical_rom_content_in_differently_compressed_archives_is_a_duplicate()
    {
        var rom = Rom(4096, seed: 1);
        var a = Zip("setA", "Tekken 3 (USA).zip", "Tekken 3 (USA).bin", rom, CompressionLevel.Optimal);
        var b = Zip("setB", "Tekken 3 (USA).zip", "Tekken 3 (USA).bin", rom, CompressionLevel.NoCompression);

        // The archives genuinely differ; the ROMs inside do not.
        Assert.NotEqual(File.ReadAllBytes(a), File.ReadAllBytes(b));

        var group = Assert.Single(Analyze(KeepPolicy.KeepShortestPath).Groups);

        Assert.Equal(2, group.Members.Count);
        Assert.Equal(rom.Length, group.RomSize);
        Assert.NotNull(group.Sha1);
    }

    // Gate 6.
    [Fact]
    public void Same_name_with_different_content_is_not_a_duplicate()
    {
        Zip("setA", "Tekken 3 (USA).zip", "Tekken 3 (USA).bin", Rom(4096, seed: 1));
        Zip("setB", "Tekken 3 (USA).zip", "Tekken 3 (USA).bin", Rom(4096, seed: 2));

        Assert.Empty(Analyze(KeepPolicy.KeepShortestPath).Groups);
    }

    // Gate 7. Variants are a curation question for the policy engine, not redundancy.
    [Fact]
    public void Different_revisions_of_the_same_title_are_not_duplicates()
    {
        Zip("setA", "Tekken 3 (USA).zip", "Tekken 3 (USA).bin", Rom(4096, seed: 1));
        Zip("setA", "Tekken 3 (USA) (Rev 1).zip", "Tekken 3 (USA) (Rev 1).bin", Rom(4096, seed: 2));

        Assert.Empty(Analyze(KeepPolicy.KeepShortestPath).Groups);
    }

    // Gate 8. A unique size cannot have a byte-identical twin, so it is never opened.
    [Fact]
    public void Unique_sized_units_are_never_hashed()
    {
        Zip("setA", "Alone (USA).zip", "Alone (USA).bin", Rom(1024, seed: 3));
        var rom = Rom(4096, seed: 1);
        Zip("setA", "Twin A (USA).zip", "Twin A (USA).bin", rom);
        Zip("setB", "Twin B (USA).zip", "Twin B (USA).bin", rom);

        var report = Analyze(KeepPolicy.KeepShortestPath);

        var skipped = Assert.Single(report.ExcludedFor(DedupExclusion.UniqueSize));
        Assert.Equal("Alone (USA).zip", skipped.Candidate.Name);
        Assert.Contains("never hashed", skipped.Detail, StringComparison.Ordinal);
    }

    // Gate 9. A shared CRC32 is not proof of identity at 1.5M-entry scale.
    [Fact]
    public void A_crc32_collision_is_resolved_by_sha1_not_accepted()
    {
        var left = Rom(2048, seed: 10);
        var right = Rom(2048, seed: 11);
        var a = Zip("setA", "Left (USA).zip", "Left (USA).bin", left);
        var b = Zip("setB", "Right (USA).zip", "Right (USA).bin", right);

        // Force the CRC32 tier to collide while the bytes genuinely differ.
        var collidingCrc = "deadbeef";
        var cache = Cache("collision");
        foreach (var path in new[] { a, b })
        {
            var file = new FileSystemReader().Describe(path)!;
            cache.Store(file.FullPath, file.Size, file.ModifiedUtc,
                new RomHash(collidingCrc, null, 2048, RomFormatDetection.None, false));
        }

        var report = Analyze(KeepPolicy.KeepShortestPath, cache);

        // SHA1 separated them, so no duplicate was declared on the strength of CRC32 alone.
        Assert.Empty(report.Groups);
    }

    // Gate 11. A file still being written will change; its hash means nothing.
    [Fact]
    public void In_progress_units_are_excluded_from_grouping_entirely()
    {
        var rom = Rom(4096, seed: 1);
        Zip("setA", "Twin A (USA).zip", "Twin A (USA).bin", rom);
        Zip("setB", "Twin B (USA).zip", "Twin B (USA).bin", rom);

        var report = Analyze(KeepPolicy.KeepShortestPath, state: VerificationState.InProgress);

        Assert.Empty(report.Groups);
        Assert.Equal(2, report.ExcludedFor(DedupExclusion.InProgress).Count);
    }

    // Gate 12. Two identical corrupt files are two corrupt files; collapsing them would leave one
    // corrupt file and hide the second.
    [Fact]
    public void Mismatched_units_are_reported_and_never_acted_on()
    {
        var rom = Rom(4096, seed: 1);
        Zip("setA", "Twin A (USA).zip", "Twin A (USA).bin", rom);
        Zip("setB", "Twin B (USA).zip", "Twin B (USA).bin", rom);

        var report = Analyze(KeepPolicy.KeepShortestPath, state: VerificationState.Mismatched);

        Assert.Empty(report.Groups);
        Assert.Empty(report.Removable);
        Assert.Equal(2, report.ExcludedFor(DedupExclusion.Mismatched).Count);
    }

    // Gates 10 and 20. Report-only is the default policy and DRY-RUN the default mode.
    [Fact]
    public void A_first_run_with_no_flags_decides_nothing_and_moves_nothing()
    {
        var rom = Rom(4096, seed: 1);
        Zip("setA", "Twin A (USA).zip", "Twin A (USA).bin", rom);
        Zip("setB", "Twin B (USA).zip", "Twin B (USA).bin", rom);
        var before = Snapshot();

        var report = Analyze();

        var group = Assert.Single(report.Groups);
        Assert.True(group.IsTie);
        Assert.Null(group.Keep);
        Assert.Empty(report.Removable);

        // The plan itself is empty, so there is nothing an accidental --apply could carry out.
        var plan = QuarantinePlanner.Build(report, "dedup-noop", DateTimeOffset.UnixEpoch);
        Assert.Empty(plan.Plan.Actions);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Dry_run_builds_the_plan_without_touching_anything()
    {
        var rom = Rom(4096, seed: 1);
        Zip("setA", "Twin A (USA).zip", "Twin A (USA).bin", rom);
        Zip("deep/setB", "Twin B (USA).zip", "Twin B (USA).bin", rom);
        var before = Snapshot();

        var plan = QuarantinePlanner.Build(Analyze(KeepPolicy.KeepShortestPath), "dedup-dry", DateTimeOffset.UnixEpoch);

        Assert.NotEmpty(plan.Plan.Actions);
        Assert.Equal(before, Snapshot());
    }

    // Gate 19. A coin flip presented as a decision is a guess wearing a confident label.
    [Fact]
    public void A_tie_the_policy_cannot_break_is_reported_and_skipped()
    {
        var rom = Rom(4096, seed: 1);
        var a = Zip("setA", "Twin A (USA).zip", "Twin A (USA).bin", rom);
        var b = Zip("setB", "Twin B (USA).zip", "Twin B (USA).bin", rom);

        // Equal nesting depth, so "keep shortest path" has nothing to choose between.
        Assert.Equal(a.Count(c => c == Path.DirectorySeparatorChar), b.Count(c => c == Path.DirectorySeparatorChar));

        var report = Analyze(KeepPolicy.KeepShortestPath);
        var group = Assert.Single(report.Groups);

        Assert.True(group.IsTie);
        Assert.Contains("tie", group.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(group.Removable);
        Assert.Empty(QuarantinePlanner.Build(report, "dedup-tie", DateTimeOffset.UnixEpoch).Plan.Actions);
    }

    [Fact]
    public void Keep_shortest_path_prefers_the_least_nested_copy()
    {
        var rom = Rom(4096, seed: 1);
        var shallow = Zip("setA", "Twin (USA).zip", "Twin (USA).bin", rom);
        Zip("deep/deeper/setB", "Twin (USA).zip", "Twin (USA).bin", rom);

        var group = Assert.Single(Analyze(KeepPolicy.KeepShortestPath).Groups);

        Assert.Equal(shallow, group.Keep!.Path);
        Assert.Single(group.Removable);
    }

    // Gates 14, 15, 16, 17. The full apply round-trip, ending where it started.
    [Fact]
    public void Apply_quarantines_writes_a_manifest_journals_it_and_undo_restores_exactly()
    {
        var rom = Rom(4096, seed: 1);
        var kept = Zip("setA", "Twin (USA).zip", "Twin (USA).bin", rom);
        var removed = Zip("deep/setB", "Twin (USA).zip", "Twin (USA).bin", rom);
        var before = Snapshot();

        var report = Analyze(KeepPolicy.KeepShortestPath);
        var plan = QuarantinePlanner.Build(report, "dedup-apply", DateTimeOffset.UnixEpoch);

        var (executor, journals, undo) = Undo();
        Assert.True(executor.Execute(plan.Plan, apply: true).Success);

        // The redundant copy moved; the kept one did not.
        Assert.False(File.Exists(removed));
        Assert.True(File.Exists(kept));

        // Gate 14: quarantine landed on the same volume, so the move was a move.
        var quarantined = plan.Manifest.Units.Single();
        Assert.True(File.Exists(quarantined.QuarantinePath));
        Assert.Equal(
            Path.GetPathRoot(Path.GetFullPath(removed)),
            Path.GetPathRoot(Path.GetFullPath(quarantined.QuarantinePath)));

        // Gate 15: the manifest stands on its own.
        var manifestPath = Path.Combine(
            _root, QuarantinePaths.DirectoryName, "dedup-apply", QuarantinePaths.ManifestFileName);
        Assert.True(File.Exists(manifestPath));
        var manifest = JsonSerializer.Deserialize<QuarantineManifest>(
            File.ReadAllText(manifestPath), ARK.Core.Serialization.ArkJson.Read)!;
        var entry = Assert.Single(manifest.Units);
        Assert.Equal(removed, entry.OriginalPath);
        Assert.Equal(kept, entry.KeptPath);
        Assert.Equal(group_Crc(report), entry.RomCrc32);
        Assert.NotNull(entry.RomSha1);
        Assert.Equal(rom.Length, entry.RomSize);
        Assert.Contains("least-nested", entry.KeptReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(KeepPolicy.KeepShortestPath.ToString(), manifest.Policy);

        // Gate 16: every move is journaled.
        var journal = journals.Read("dedup-apply");
        Assert.True(journal.Succeeded);
        Assert.Contains(journal.Document!.CompletedActions, action => action.Kind == ActionKind.Quarantine);

        // Gate 17: byte-for-byte back to the start.
        Assert.True(undo.Apply("dedup-apply").Result!.Success);
        Assert.Equal(before, Snapshot());
    }

    // Gate 18. Interrupted partway, the journal reverses exactly what completed — no more.
    [Fact]
    public void An_interrupted_apply_leaves_a_journal_that_reverses_only_what_completed()
    {
        var romA = Rom(4096, seed: 1);
        var romB = Rom(8192, seed: 2);
        Zip("setA", "A (USA).zip", "A (USA).bin", romA);
        var removedA = Zip("deep/setB", "A (USA).zip", "A (USA).bin", romA);
        Zip("setA", "B (USA).zip", "B (USA).bin", romB);
        var removedB = Zip("deep/setB", "B (USA).zip", "B (USA).bin", romB);
        var before = Snapshot();

        var report = Analyze(KeepPolicy.KeepShortestPath);
        var full = QuarantinePlanner.Build(report, "dedup-partial", DateTimeOffset.UnixEpoch);

        // Stand in for a kill: run only the actions up to and including the first quarantine.
        var upTo = full.Plan.Actions
            .TakeWhile(action => action.Kind != ActionKind.Quarantine)
            .Concat(full.Plan.Actions.Where(action => action.Kind == ActionKind.Quarantine).Take(1))
            .ToArray();

        var (executor, journals, undo) = Undo();
        Assert.True(executor.Execute(full.Plan with { Actions = upTo }, apply: true).Success);

        // Exactly one of the two moved.
        Assert.Equal(1, new[] { removedA, removedB }.Count(path => !File.Exists(path)));

        var journal = journals.Read("dedup-partial");
        Assert.True(journal.Succeeded);
        Assert.Equal(upTo.Length, journal.Document!.CompletedActions.Count);

        Assert.True(undo.Apply("dedup-partial").Result!.Success);
        Assert.Equal(before, Snapshot());
    }

    // Gate 13. Reports still run over an active-download directory; writes do not.
    [Fact]
    public void Active_download_directory_is_refused_for_apply_while_still_being_reported()
    {
        var rom = Rom(4096, seed: 1);
        Zip("setA", "Twin (USA).zip", "Twin (USA).bin", rom);
        var downloading = Zip("deep/incoming", "Twin (USA).zip", "Twin (USA).bin", rom);
        var before = Snapshot();

        var report = Analyze(KeepPolicy.KeepShortestPath);

        // The group is still found and reported — only the write is refused.
        Assert.Single(report.Groups);

        var plan = QuarantinePlanner.Build(
            report, "dedup-active", DateTimeOffset.UnixEpoch,
            new[] { Path.GetDirectoryName(downloading)! });

        var refusal = Assert.Single(plan.Refused);
        Assert.Equal(downloading, refusal.Request.Path);
        Assert.Contains("active-download", refusal.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(plan.Plan.Actions);
        Assert.Equal(before, Snapshot());
    }

    // Prohibition 4: operations take whole units. A quarantine action names the unit's archive,
    // never a file inside it.
    [Fact]
    public void Quarantine_acts_on_whole_units_never_on_files_inside_them()
    {
        var rom = Rom(4096, seed: 1);
        Zip("setA", "Twin (USA).zip", "Twin (USA).bin", rom);
        var removed = Zip("deep/setB", "Twin (USA).zip", "Twin (USA).bin", rom);

        var plan = QuarantinePlanner.Build(
            Analyze(KeepPolicy.KeepShortestPath), "dedup-units", DateTimeOffset.UnixEpoch);

        var move = Assert.Single(plan.Plan.Actions.Where(action => action.Kind == ActionKind.Quarantine));
        Assert.Equal(removed, move.Source);
        Assert.EndsWith(".zip", move.Source, StringComparison.OrdinalIgnoreCase);
    }

    private static string group_Crc(DedupReport report) => report.Resolved.Single().Crc32;

    private DedupReport Analyze(
        KeepPolicy policy = KeepPolicy.ReportOnly,
        HashCache? cache = null,
        VerificationState state = VerificationState.Verified)
    {
        var fileSystem = new FileSystemReader();
        var inspector = new ArchiveInspector(fileSystem, new[] { ".zip" });
        var rules = ScanRulesLoader.Load(TestFixtures.ShippedScanRulesPath());

        // MinimumFileCount would reject these small fixtures, so profile them permissively; the
        // classifier itself is exercised by the Phase 4 tests.
        rules.MinimumFileCount = 1;
        rules.NamingConformanceThreshold = 0;

        var scan = new ScanService(
            fileSystem,
            new DirectoryProfiler(Tokenizer, rules),
            new IGameUnitResolver[] { new CartridgeUnitResolver(Tokenizer, inspector), new DiscUnitResolver() },
            rules).Scan(_root);

        var verification = new VerificationReport(
            _root,
            scan.Units.Select(unit => new VerifiedUnit(
                unit.Unit.PrimaryPath, unit.Unit.Files[0].Name, unit.Unit.SetFolder, null, state, "test fixture")).ToArray(),
            0, 0, TimeSpan.Zero, 0);

        return new DedupService(new RomHasher(fileSystem, inspector), cache ?? Cache("dedup"))
            .Analyze(scan, verification, policy);
    }

    private (Executor Executor, JournalStore Journals, UndoService Undo) Undo()
    {
        var paths = new InstancePaths($"dedup-{Guid.NewGuid():N}", Path.Combine(_root, "..", "ark-instances"));
        Directory.CreateDirectory(paths.Journal);
        var executor = new Executor(paths);
        var journals = new JournalStore(paths);
        return (executor, journals, new UndoService(journals, executor));
    }

    private HashCache Cache(string name)
    {
        var paths = new InstancePaths($"{name}-{Guid.NewGuid():N}", Path.Combine(_root, "..", "ark-instances"));
        Directory.CreateDirectory(paths.Db);
        var cache = new HashCache(paths);
        _caches.Add(cache);
        return cache;
    }

    private string Zip(string directory, string archiveName, string entryName, byte[] content,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        var folder = Path.Combine(_root, directory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, archiveName);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var stream = archive.CreateEntry(entryName, level).Open();
        stream.Write(content);
        return path;
    }

    private static byte[] Rom(int size, int seed)
    {
        var bytes = new byte[size];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private string Snapshot() => string.Join("\n", Directory
        .EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => File.Exists(path)
            ? $"{path}:{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(path)))}"
            : path + "/"));
}
