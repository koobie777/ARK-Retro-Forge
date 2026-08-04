using System.IO.Compression;
using ARK.Core.Configuration;
using ARK.Core.Dat;
using ARK.Core.Execution;
using ARK.Core.Instances;
using ARK.Core.Naming;
using ARK.Core.Renaming;
using ARK.Core.Scanning;
using ARK.Core.Units;
using ARK.Core.Verification;

namespace ARK.Tests;

/// <summary>
/// Phase 9: what the naming subsystem was built for, and what v1 destroyed collections doing.
/// </summary>
/// <remarks>
/// Every prohibition in <c>CLAUDE.md</c> about naming was written from v1's wreckage —
/// <c>(USA) (USA) (USA)</c>, language tags in region slots, per-title patches that never
/// converged. This is where the foundation is either vindicated or it isn't.
/// </remarks>
public sealed class RenameTests : IDisposable
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);
    private static readonly NameFormatter Formatter = new(Tokenizer);

    private readonly string _root = TempRoot.Create();

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Gate 4. The name is the DAT's. The filename only proposed the entry; the hash proved it.
    [Fact]
    public void The_canonical_name_comes_from_the_dat_entry_not_the_filename()
    {
        Zip("tekken3.zip");

        var report = Decide(RenameMode.Canonicalize, ("tekken3.zip", "Tekken 3 (USA) (En,Fr,De)", VerificationState.Verified));

        var decision = Assert.Single(report.Renames);
        Assert.Equal("Tekken 3 (USA) (En,Fr,De).zip", decision.ProposedName);
        Assert.Contains("DAT entry", decision.Source!, StringComparison.Ordinal);
    }

    // Gate 5. A unit with no DAT match has no known canonical name. v1 invented one.
    [Fact]
    public void A_unit_with_no_dat_match_is_refused_and_no_name_is_invented()
    {
        Zip("Mystery Game (USA).zip");

        var report = Decide(RenameMode.Canonicalize, ("Mystery Game (USA).zip", null, VerificationState.Unrecognized));

        var refused = Assert.Single(report.Refused);
        Assert.Equal(RenameRefusal.NoDatMatch, refused.Refusal);
        Assert.Equal(refused.CurrentName, refused.ProposedName);
        Assert.Empty(report.Renames);
    }

    // Gate 3. A corrupt file renamed to its canonical name looks verified forever after.
    [Theory]
    [InlineData(VerificationState.Mismatched, RenameRefusal.NotVerified)]
    [InlineData(VerificationState.Unrecognized, RenameRefusal.NotVerified)]
    [InlineData(VerificationState.InProgress, RenameRefusal.ActiveDownload)]
    public void Only_verified_units_are_canonicalized(VerificationState state, RenameRefusal expected)
    {
        Zip("wrongname.zip");

        var report = Decide(RenameMode.Canonicalize, ("wrongname.zip", "Tekken 3 (USA)", state));

        Assert.Equal(expected, Assert.Single(report.Refused).Refusal);
        Assert.Empty(report.Renames);
    }

    // Gate 6. Repair, not identification — and never the default.
    [Fact]
    public void Normalize_is_a_separate_mode_that_reformats_the_units_own_name()
    {
        Zip("Tekken 3 (USA) (USA).zip");

        var canonical = Decide(RenameMode.Canonicalize, ("Tekken 3 (USA) (USA).zip", null, VerificationState.Unrecognized));
        var normalized = Decide(RenameMode.Normalize, ("Tekken 3 (USA) (USA).zip", null, VerificationState.Unrecognized));

        // Without a DAT match, canonicalize refuses. Normalize repairs the name it has.
        Assert.Single(canonical.Refused);
        Assert.Equal(RenameMode.Normalize, normalized.Mode);
        Assert.Contains("reformatted from its own name", Assert.Single(normalized.Renames).Source!, StringComparison.Ordinal);
    }

    // Gate 11. The v1 damage, repaired.
    [Theory]
    [InlineData("Crash Bandicoot (USA) (USA) (USA).zip", "Crash Bandicoot (USA).zip")]
    [InlineData("Baten Kaitos (USA) (Disc 1) (Disc 1).zip", "Baten Kaitos (USA) (Disc 1).zip")]
    [InlineData("Adventure Time (Rev 1) (USA).zip", "Adventure Time (USA) (Rev 1).zip")]
    [InlineData("Baten Kaitos (USA) (Disk 1).zip", "Baten Kaitos (USA) (Disc 1).zip")]
    [InlineData("The Alliance Alive (USA).zip", "Alliance Alive, The (USA).zip")]
    public void A_collection_carrying_v1_damage_normalizes_to_correct_names(string damaged, string expected)
    {
        Zip(damaged);

        var report = Decide(RenameMode.Normalize, (damaged, null, VerificationState.Unrecognized));

        Assert.Equal(expected, Assert.Single(report.Renames).ProposedName);
    }

    // Gate 7. On a conformant set the whole operation is a no-op.
    [Fact]
    public void A_unit_already_correctly_named_is_skipped_with_no_filesystem_write()
    {
        Zip("Tekken 3 (USA).zip");
        var before = Snapshot();

        var report = Decide(RenameMode.Canonicalize, ("Tekken 3 (USA).zip", "Tekken 3 (USA)", VerificationState.Verified));

        Assert.Single(report.AlreadyCorrect);
        Assert.Empty(report.Renames);

        // No plan, therefore no possible write.
        var plan = RenamePlanner.Build(report, "rename-noop", DateTimeOffset.UnixEpoch);
        Assert.Empty(plan.Plan.Actions);
        Assert.Equal(before, Snapshot());
    }

    // Gate 8. A case-insensitive comparison would skip a real correction.
    [Fact]
    public void The_skip_check_is_ordinal_so_a_case_only_difference_is_renamed()
    {
        Zip("tekken 3 (usa).zip");

        var report = Decide(RenameMode.Canonicalize, ("tekken 3 (usa).zip", "Tekken 3 (USA)", VerificationState.Verified));

        Assert.Empty(report.AlreadyCorrect);
        Assert.Equal("Tekken 3 (USA).zip", Assert.Single(report.Renames).ProposedName);
    }

    // Gate 9. File.Move between names differing only in case is unreliable on Windows.
    [Fact]
    public void A_case_only_rename_succeeds_on_a_case_insensitive_filesystem()
    {
        Zip("tekken 3 (usa).zip");

        var report = Decide(RenameMode.Canonicalize, ("tekken 3 (usa).zip", "Tekken 3 (USA)", VerificationState.Verified));
        var plan = RenamePlanner.Build(report, "rename-case", DateTimeOffset.UnixEpoch);

        // Staged through a temporary name, because the destination is the source.
        Assert.Single(plan.Staged);
        Assert.Equal(2, plan.Plan.Actions.Count);

        var (executor, _, undo) = Undo();
        Assert.True(executor.Execute(plan.Plan, apply: true).Success);

        var actual = Directory.GetFiles(_root).Single();
        Assert.Equal("Tekken 3 (USA).zip", Path.GetFileName(actual));

        Assert.True(undo.Apply("rename-case").Result!.Success);
        Assert.Equal("tekken 3 (usa).zip", Path.GetFileName(Directory.GetFiles(_root).Single()));
    }

    // Gate 12. A disambiguating suffix is a fabricated name — the one thing this phase must never
    // produce. Either they are duplicates or one is misidentified; both are reported.
    [Fact]
    public void Two_units_canonicalizing_to_the_same_name_are_refused_and_reported()
    {
        Zip("copy-a.zip");
        Zip("copy-b.zip");
        var before = Snapshot();

        var report = Decide(
            RenameMode.Canonicalize,
            ("copy-a.zip", "Tekken 3 (USA)", VerificationState.Verified),
            ("copy-b.zip", "Tekken 3 (USA)", VerificationState.Verified));

        Assert.Empty(report.Renames);
        Assert.Equal(2, report.RefusedFor(RenameRefusal.Collision).Count);
        Assert.All(report.Refused, decision => Assert.DoesNotContain("(2)", decision.ProposedName, StringComparison.Ordinal));
        Assert.Equal(before, Snapshot());
    }

    // Gate 13. Renaming A first would destroy B.
    [Fact]
    public void A_batch_containing_a_swap_completes_without_destroying_either_file()
    {
        Zip("A.zip", "alpha");
        Zip("B.zip", "beta");

        var report = Decide(
            RenameMode.Canonicalize,
            ("A.zip", "B", VerificationState.Verified),
            ("B.zip", "A", VerificationState.Verified));

        var plan = RenamePlanner.Build(report, "rename-swap", DateTimeOffset.UnixEpoch);
        var (executor, _, undo) = Undo();
        Assert.True(executor.Execute(plan.Plan, apply: true).Success);

        // Contents prove the swap really happened rather than one file overwriting the other.
        Assert.Equal("alpha", ReadEntry(Path.Combine(_root, "B.zip")));
        Assert.Equal("beta", ReadEntry(Path.Combine(_root, "A.zip")));
        Assert.Equal(2, Directory.GetFiles(_root).Length);

        Assert.True(undo.Apply("rename-swap").Result!.Success);
        Assert.Equal("alpha", ReadEntry(Path.Combine(_root, "A.zip")));
        Assert.Equal("beta", ReadEntry(Path.Combine(_root, "B.zip")));
    }

    // Gate 14. Three-way cycle: A→B, B→C, C→A.
    [Fact]
    public void A_batch_containing_a_longer_cycle_completes_correctly()
    {
        Zip("A.zip", "alpha");
        Zip("B.zip", "beta");
        Zip("C.zip", "gamma");

        var report = Decide(
            RenameMode.Canonicalize,
            ("A.zip", "B", VerificationState.Verified),
            ("B.zip", "C", VerificationState.Verified),
            ("C.zip", "A", VerificationState.Verified));

        var plan = RenamePlanner.Build(report, "rename-cycle", DateTimeOffset.UnixEpoch);
        var (executor, _, undo) = Undo();
        Assert.True(executor.Execute(plan.Plan, apply: true).Success);

        Assert.Equal("alpha", ReadEntry(Path.Combine(_root, "B.zip")));
        Assert.Equal("beta", ReadEntry(Path.Combine(_root, "C.zip")));
        Assert.Equal("gamma", ReadEntry(Path.Combine(_root, "A.zip")));
        Assert.Equal(3, Directory.GetFiles(_root).Length);

        Assert.True(undo.Apply("rename-cycle").Result!.Success);
        Assert.Equal("alpha", ReadEntry(Path.Combine(_root, "A.zip")));
    }

    // Gate 15. A chain A→B where B→C must run in dependency order, not batch order.
    [Fact]
    public void The_planner_projects_the_filesystem_across_the_batch()
    {
        Zip("A.zip", "alpha");
        Zip("B.zip", "beta");

        var report = Decide(
            RenameMode.Canonicalize,
            ("A.zip", "B", VerificationState.Verified),
            ("B.zip", "C", VerificationState.Verified));

        var plan = RenamePlanner.Build(report, "rename-chain", DateTimeOffset.UnixEpoch);

        // B must vacate before A can take its place — no staging needed, just ordering.
        Assert.Empty(plan.Staged);
        Assert.Equal("B.zip", Path.GetFileName(plan.Plan.Actions[0].Source));

        var (executor, _, _) = Undo();
        Assert.True(executor.Execute(plan.Plan, apply: true).Success);
        Assert.Equal("alpha", ReadEntry(Path.Combine(_root, "B.zip")));
        Assert.Equal("beta", ReadEntry(Path.Combine(_root, "C.zip")));
    }

    // Gate 16. Rename is a pure filesystem move; it never touches a byte of ROM content.
    [Fact]
    public void Archives_are_renamed_never_rewritten()
    {
        var path = Zip("wrongname.zip", "rom-bytes");
        var archiveBytes = File.ReadAllBytes(path);

        var report = Decide(RenameMode.Canonicalize, ("wrongname.zip", "Tekken 3 (USA)", VerificationState.Verified));
        var plan = RenamePlanner.Build(report, "rename-bytes", DateTimeOffset.UnixEpoch);

        var (executor, _, _) = Undo();
        Assert.True(executor.Execute(plan.Plan, apply: true).Success);

        var renamed = Path.Combine(_root, "Tekken 3 (USA).zip");
        Assert.Equal(archiveBytes, File.ReadAllBytes(renamed));

        // The entry inside keeps its own name — renaming it would mean recompressing.
        using var archive = ZipFile.OpenRead(renamed);
        Assert.Equal("wrongname.gb", archive.Entries.Single().FullName);
    }

    // Gates 10, 18, 19, 20.
    [Fact]
    public void Apply_is_journaled_reversible_and_renaming_twice_writes_nothing_the_second_time()
    {
        Zip("wrongname.zip");
        var before = Snapshot();

        var report = Decide(RenameMode.Canonicalize, ("wrongname.zip", "Tekken 3 (USA)", VerificationState.Verified));

        // Gate 18: DRY-RUN by default.
        var plan = RenamePlanner.Build(report, "rename-apply", DateTimeOffset.UnixEpoch);
        Assert.Equal(before, Snapshot());

        var (executor, journals, undo) = Undo();
        Assert.True(executor.Execute(plan.Plan, apply: true).Success);
        Assert.True(File.Exists(Path.Combine(_root, "Tekken 3 (USA).zip")));

        // Gate 19: journaled.
        var journal = journals.Read("rename-apply");
        Assert.True(journal.Succeeded);
        Assert.Contains(journal.Document!.CompletedActions, action => action.Kind == ActionKind.Rename);

        // Gate 10: a second pass finds nothing to do.
        var second = Decide(RenameMode.Canonicalize, ("Tekken 3 (USA).zip", "Tekken 3 (USA)", VerificationState.Verified));
        Assert.Empty(second.Renames);
        Assert.Empty(RenamePlanner.Build(second, "rename-again", DateTimeOffset.UnixEpoch).Plan.Actions);

        // Gate 20: byte-for-byte back to the start.
        Assert.True(undo.Apply("rename-apply").Result!.Success);
        Assert.Equal(before, Snapshot());
    }

    // Gate 21.
    [Fact]
    public void An_interrupted_apply_reverses_exactly_what_completed()
    {
        Zip("a.zip", "alpha");
        Zip("b.zip", "beta");
        var before = Snapshot();

        var report = Decide(
            RenameMode.Canonicalize,
            ("a.zip", "Alpha (USA)", VerificationState.Verified),
            ("b.zip", "Beta (USA)", VerificationState.Verified));

        var plan = RenamePlanner.Build(report, "rename-partial", DateTimeOffset.UnixEpoch);
        var upTo = plan.Plan.Actions.Take(1).ToArray();

        var (executor, journals, undo) = Undo();
        Assert.True(executor.Execute(plan.Plan with { Actions = upTo }, apply: true).Success);

        Assert.Single(journals.Read("rename-partial").Document!.CompletedActions);
        Assert.True(undo.Apply("rename-partial").Result!.Success);
        Assert.Equal(before, Snapshot());
    }

    // Gate 22.
    [Fact]
    public void Active_download_directories_are_refused_while_still_being_reported()
    {
        Zip("wrongname.zip");

        var report = Decide(RenameMode.Canonicalize, ("wrongname.zip", "Tekken 3 (USA)", VerificationState.Verified));
        var plan = RenamePlanner.Build(report, "rename-active", DateTimeOffset.UnixEpoch, new[] { _root });

        // Still analysed and reported; only the write is refused.
        Assert.Single(report.Renames);
        Assert.Single(plan.Refused);
        Assert.Empty(plan.Plan.Actions);
    }

    // Gate 17. Organize is a different operation, and never a removal.
    [Fact]
    public void Organize_files_units_into_dat_named_directories_and_undo_reverses_it()
    {
        Zip("Tekken 3 (USA).zip");
        Zip("Ridge Racer (USA).zip");
        var before = Snapshot();

        var scan = Scan(("Tekken 3 (USA).zip", "Tekken 3 (USA)", VerificationState.Verified),
                        ("Ridge Racer (USA).zip", "Ridge Racer (USA)", VerificationState.Verified));
        var verification = Verify(scan, VerificationState.Verified);

        var plan = OrganizePlanner.Build(scan, verification, "organize-apply", DateTimeOffset.UnixEpoch);

        Assert.Equal(2, plan.Moves.Count);
        Assert.All(plan.Moves, move => Assert.Equal("Sony - PlayStation", move.Folder));

        var (executor, journals, undo) = Undo();
        Assert.True(executor.Execute(plan.Plan, apply: true).Success);

        // Moved, not removed: still two archives, now filed.
        Assert.Equal(2, Directory.GetFiles(_root, "*.zip", SearchOption.AllDirectories).Length);
        Assert.True(File.Exists(Path.Combine(_root, "Sony - PlayStation", "Tekken 3 (USA).zip")));

        // Organize moves; it never quarantines.
        Assert.DoesNotContain(plan.Plan.Actions, action => action.Kind == ActionKind.Quarantine);
        Assert.True(journals.Read("organize-apply").Succeeded);

        Assert.True(undo.Apply("organize-apply").Result!.Success);
        Assert.Equal(before, Snapshot());
    }

    private RenameReport Decide(RenameMode mode, params (string File, string? DatName, VerificationState State)[] units)
    {
        var scan = Scan(units);
        return new RenameService(Formatter).Decide(scan, Verify(scan, units), mode);
    }

    private ScanReport Scan(params (string File, string? DatName, VerificationState State)[] units)
    {
        var files = units
            .Select(unit => new FileEntry(
                Path.Combine(_root, unit.File), unit.File, ".zip", 1024, DateTimeOffset.UnixEpoch))
            .ToArray();

        var profile = new DirectoryProfile(
            _root, Path.GetFileName(_root), files.Length, ".zip", 1, 1, DirectoryOutcome.RomSet,
            ExclusionReason.None, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<FileEntry>());

        var scanned = files.Zip(units, (file, unit) => new ScannedUnit(
            new GameUnit(
                GameUnitKind.Cartridge, file.FullPath, new[] { file },
                Tokenizer.Parse(file.NameWithoutExtension), Path.GetFileName(_root),
                Array.Empty<string>(), Array.Empty<ArchiveEntry>(), Array.Empty<GameUnitIssue>()),
            unit.DatName is null ? null : new CatalogEntry
            {
                DatName = "Sony - PlayStation",
                GameName = unit.DatName,
                RomName = unit.DatName + ".bin",
                Crc32 = "aabbccdd",
            })).ToArray();

        return new ScanReport(
            _root,
            new[] { new ScannedDirectory(profile, new DatScope("Sony - PlayStation", null, null, DatScopeSource.DirectoryName)) },
            scanned,
            Array.Empty<ExcludedFile>(),
            Array.Empty<FileEntry>());
    }

    private static VerificationReport Verify(ScanReport scan, params (string File, string? DatName, VerificationState State)[] units) =>
        new(scan.Root,
            scan.Units.Select(unit => new VerifiedUnit(
                unit.Unit.PrimaryPath,
                unit.Unit.Files[0].Name,
                unit.Unit.SetFolder,
                "Sony - PlayStation",
                units.First(entry => entry.File == unit.Unit.Files[0].Name).State,
                "fixture")).ToArray(),
            0, 0, TimeSpan.Zero, 0);

    private static VerificationReport Verify(ScanReport scan, VerificationState state) =>
        new(scan.Root,
            scan.Units.Select(unit => new VerifiedUnit(
                unit.Unit.PrimaryPath, unit.Unit.Files[0].Name, unit.Unit.SetFolder,
                "Sony - PlayStation", state, "fixture")).ToArray(),
            0, 0, TimeSpan.Zero, 0);

    private (Executor Executor, JournalStore Journals, UndoService Undo) Undo()
    {
        var paths = new InstancePaths($"rename-{Guid.NewGuid():N}", Path.Combine(_root, "..", "ark-instances"));
        Directory.CreateDirectory(paths.Journal);
        var executor = new Executor(paths);
        var journals = new JournalStore(paths);
        return (executor, journals, new UndoService(journals, executor));
    }

    private string Zip(string name, string content = "rom")
    {
        var path = Path.Combine(_root, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var stream = archive.CreateEntry(Path.GetFileNameWithoutExtension(name) + ".gb").Open();
        stream.Write(System.Text.Encoding.UTF8.GetBytes(content));
        return path;
    }

    private static string ReadEntry(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        using var reader = new StreamReader(archive.Entries.Single().Open());
        return reader.ReadToEnd();
    }

    private string Snapshot() => string.Join("\n", Directory
        .EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => File.Exists(path)
            ? $"{path}:{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(path)))}"
            : path + "/"));
}
