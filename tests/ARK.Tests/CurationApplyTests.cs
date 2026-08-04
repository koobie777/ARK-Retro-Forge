using System.IO.Compression;
using ARK.Core.Configuration;
using ARK.Core.Dedup;
using ARK.Core.Execution;
using ARK.Core.Instances;
using ARK.Core.Naming;
using ARK.Core.Policy;
using ARK.Core.Scanning;
using ARK.Core.Units;
using ARK.Core.Verification;

namespace ARK.Tests;

/// <summary>
/// Phase 8 apply paths: quarantine removes, sort organizes, and both are fully reversible.
/// </summary>
public sealed class CurationApplyTests : IDisposable
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);

    private readonly string _root = TempRoot.Create();

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Gates 17, 19, 20.
    [Fact]
    public void Quarantine_writes_a_manifest_is_journaled_and_undo_restores_exactly()
    {
        Zip("Game (USA).zip");
        Zip("Game (USA) (Proto).zip");
        var before = Snapshot();

        var report = Curate(VariantPresets.RetailOnly);
        var plan = QuarantinePlanner.Build(
            report.Root, Requests(report), "curate-apply", DateTimeOffset.UnixEpoch, report.Policy.Name);

        var (executor, journals, undo) = Undo();
        Assert.True(executor.Execute(plan.Plan, apply: true).Success);

        Assert.False(File.Exists(Path.Combine(_root, "Game (USA) (Proto).zip")));
        Assert.True(File.Exists(Path.Combine(_root, "Game (USA).zip")));

        var manifestPath = Path.Combine(
            _root, QuarantinePaths.DirectoryName, "curate-apply", QuarantinePaths.ManifestFileName);
        Assert.True(File.Exists(manifestPath));
        Assert.Equal("retail-only", plan.Manifest.Policy);

        var journal = journals.Read("curate-apply");
        Assert.True(journal.Succeeded);
        Assert.Contains(journal.Document!.CompletedActions, action => action.Kind == ActionKind.Quarantine);

        Assert.True(undo.Apply("curate-apply").Result!.Success);
        Assert.Equal(before, Snapshot());
    }

    // Gate 18. Files move but nothing leaves the collection — and it is reversible all the same.
    [Fact]
    public void Sort_moves_variants_into_a_subfolder_without_removing_them_and_undo_reverses_it()
    {
        Zip("Game (USA).zip");
        Zip("Game (USA) (Proto).zip");
        var before = Snapshot();

        var report = Curate(VariantPresets.RetailOnly);
        var plan = SortPlanner.Build(report, "curate-sort", DateTimeOffset.UnixEpoch);

        var (executor, journals, undo) = Undo();
        Assert.True(executor.Execute(plan.Plan, apply: true).Success);

        // Moved, not removed: still present, still readable, just somewhere else.
        var moved = Path.Combine(_root, SortPlanner.DefaultSubfolder, "Game (USA) (Proto).zip");
        Assert.True(File.Exists(moved));
        Assert.False(File.Exists(Path.Combine(_root, "Game (USA) (Proto).zip")));
        Assert.Equal(2, Directory.GetFiles(_root, "*.zip", SearchOption.AllDirectories).Length);

        // Sorting is a Move, never a Quarantine — the report must not present it as a removal.
        Assert.All(plan.Plan.Actions.Where(action => action.Kind != ActionKind.CreateDirectory),
            action => Assert.Equal(ActionKind.Move, action.Kind));
        Assert.DoesNotContain(plan.Plan.Actions, action => action.Kind == ActionKind.Quarantine);

        var journal = journals.Read("curate-sort");
        Assert.True(journal.Succeeded);

        Assert.True(undo.Apply("curate-sort").Result!.Success);
        Assert.Equal(before, Snapshot());
    }

    // Gate 21.
    [Fact]
    public void An_interrupted_apply_reverses_exactly_what_completed()
    {
        Zip("Game (USA).zip");
        Zip("Game (USA) (Proto).zip");
        Zip("Other (USA).zip");
        Zip("Other (USA) (Beta).zip");
        var before = Snapshot();

        var report = Curate(VariantPresets.RetailOnly);
        var plan = QuarantinePlanner.Build(
            report.Root, Requests(report), "curate-partial", DateTimeOffset.UnixEpoch, report.Policy.Name);

        // Stand in for a kill: everything up to and including the first quarantine.
        var upTo = plan.Plan.Actions
            .TakeWhile(action => action.Kind != ActionKind.Quarantine)
            .Concat(plan.Plan.Actions.Where(action => action.Kind == ActionKind.Quarantine).Take(1))
            .ToArray();

        var (executor, journals, undo) = Undo();
        Assert.True(executor.Execute(plan.Plan with { Actions = upTo }, apply: true).Success);

        var journal = journals.Read("curate-partial");
        Assert.Equal(upTo.Length, journal.Document!.CompletedActions.Count);

        Assert.True(undo.Apply("curate-partial").Result!.Success);
        Assert.Equal(before, Snapshot());
    }

    // Gate 22. Reports still run over an active-download directory; writes do not.
    [Fact]
    public void Active_download_directories_are_refused_for_apply_while_still_being_reported()
    {
        Zip("Game (USA).zip");
        var proto = Zip("Game (USA) (Proto).zip");
        var before = Snapshot();

        var report = Curate(VariantPresets.RetailOnly);
        Assert.Single(report.Removable);

        var plan = QuarantinePlanner.Build(
            report.Root, Requests(report), "curate-active", DateTimeOffset.UnixEpoch, report.Policy.Name,
            new[] { Path.GetDirectoryName(proto)! });

        Assert.Single(plan.Refused);
        Assert.Empty(plan.Plan.Actions);
        Assert.Equal(before, Snapshot());

        var sort = SortPlanner.Build(
            report, "curate-active-sort", DateTimeOffset.UnixEpoch, null, new[] { Path.GetDirectoryName(proto)! });
        Assert.Single(sort.Refused);
        Assert.Empty(sort.Plan.Actions);
    }

    // Gate 23, and the Phase 7 carry-over defect. Quarantine lands inside the tree that gets
    // scanned, so without this a later run finds quarantined units as live candidates — and could
    // pick one as the copy to keep.
    [Fact]
    public void A_quarantine_directory_beneath_a_scanned_root_is_excluded_with_a_stated_reason()
    {
        Zip("Game (USA).zip");

        // Mirror what a real quarantine leaves behind: conformant archives, several levels down,
        // in a directory whose own leaf name looks exactly like a live ROM set.
        var quarantined = Path.Combine(
            _root, QuarantinePaths.DirectoryName, "dedup-earlier", "Nintendo - Game Boy");
        Directory.CreateDirectory(quarantined);
        foreach (var name in TestFixtures.ReadCorpus("real-names.txt").Take(10))
        {
            using var archive = ZipFile.Open(Path.Combine(quarantined, name + ".zip"), ZipArchiveMode.Create);
            archive.CreateEntry(name + ".gb");
        }

        var scan = Scan();

        var excluded = scan.ExcludedDirectories
            .FirstOrDefault(directory => directory.Path.Contains(QuarantinePaths.DirectoryName, StringComparison.Ordinal));

        Assert.NotNull(excluded);
        Assert.Equal(ExclusionReason.DirectoryNameRule, excluded!.Reason);
        Assert.Contains(excluded.Profile.Warnings, warning => warning.Contains(QuarantinePaths.DirectoryName, StringComparison.Ordinal));

        // Nothing under the quarantine tree became a live unit.
        Assert.DoesNotContain(scan.Units, unit =>
            unit.Unit.PrimaryPath.Contains(QuarantinePaths.DirectoryName, StringComparison.Ordinal));
    }

    private static IReadOnlyList<QuarantineRequest> Requests(CurationReport report) => report.Removable
        .Select(member => new QuarantineRequest(
            member.Path, member.FileName, null, null, member.Unit.TotalSize,
            report.Resolved.First(group => group.Remove.Contains(member)).Keep[0].Path,
            $"Variant removed by policy '{report.Policy.Name}'"))
        .ToArray();

    private CurationReport Curate(VariantPolicy policy)
    {
        var scan = Scan();
        var verification = new VerificationReport(
            scan.Root,
            scan.Units.Select(unit => new VerifiedUnit(
                unit.Unit.PrimaryPath, unit.Unit.Files[0].Name, unit.Unit.SetFolder, null,
                VerificationState.Verified, "test fixture")).ToArray(),
            0, 0, TimeSpan.Zero, 0);

        return new CurationService(Tokenizer, Vocabulary).Analyze(scan, verification, policy);
    }

    private ScanReport Scan()
    {
        var rules = ScanRulesLoader.Load(TestFixtures.ShippedScanRulesPath());
        rules.MinimumFileCount = 1;
        rules.NamingConformanceThreshold = 0;

        var fileSystem = new FileSystemReader();
        var inspector = new ArchiveInspector(fileSystem, new[] { ".zip" });

        return new ScanService(
            fileSystem,
            new DirectoryProfiler(Tokenizer, rules),
            new IGameUnitResolver[] { new DiscUnitResolver(Tokenizer, inspector, fileSystem), new CartridgeUnitResolver(Tokenizer, inspector) },
            rules).Scan(_root);
    }

    private (Executor Executor, JournalStore Journals, UndoService Undo) Undo()
    {
        var paths = new InstancePaths($"curate-{Guid.NewGuid():N}", Path.Combine(_root, "..", "ark-instances"));
        Directory.CreateDirectory(paths.Journal);
        var executor = new Executor(paths);
        var journals = new JournalStore(paths);
        return (executor, journals, new UndoService(journals, executor));
    }

    private string Zip(string name)
    {
        var path = Path.Combine(_root, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var stream = archive.CreateEntry(Path.GetFileNameWithoutExtension(name) + ".gb").Open();
        stream.Write(new byte[] { 1, 2, 3, 4 });
        return path;
    }

    private string Snapshot() => string.Join("\n", Directory
        .EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => File.Exists(path)
            ? $"{path}:{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(path)))}"
            : path + "/"));
}
