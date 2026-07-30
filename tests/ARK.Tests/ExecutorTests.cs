using System.Text.Json;
using ARK.Core.Execution;
using ARK.Core.Instances;

namespace ARK.Tests;

public sealed class ExecutorTests : IDisposable
{
    private readonly string _root;
    private readonly string _work;
    private readonly InstancePaths _paths;

    public ExecutorTests()
    {
        _root = TempRoot.Create();
        _work = Path.Combine(_root, "work");
        Directory.CreateDirectory(_work);
        _paths = new InstancePaths("executor-tests", _root);
    }

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Gate 7 — dry run leaves the directory byte-identical and writes no journal.
    [Fact]
    public void DryRun_touches_nothing_and_writes_no_journal()
    {
        File.WriteAllText(Path.Combine(_work, "origin.txt"), "payload");
        var before = SnapshotWork();
        var plan = ThreeActionPlan();

        var result = new Executor(_paths).Execute(plan, apply: false);

        Assert.False(result.Applied);
        Assert.Null(result.JournalPath);
        Assert.Equal(3, plan.Actions.Count);
        Assert.False(File.Exists(_paths.JournalFileFor(plan.SessionId)));
        Assert.False(Directory.Exists(_paths.Journal));
        Assert.Equal(before, SnapshotWork());
    }

    // Gate 8 — apply performs all three actions and journals all three.
    [Fact]
    public void Apply_performs_all_actions_and_journals_all_three()
    {
        File.WriteAllText(Path.Combine(_work, "origin.txt"), "payload");
        var plan = ThreeActionPlan();

        var result = new Executor(_paths).Execute(plan, apply: true);

        Assert.True(result.Applied);
        Assert.True(result.Success);
        Assert.Equal(3, result.CompletedCount);

        var renamed = Path.Combine(_work, "sub", "renamed.txt");
        Assert.True(File.Exists(renamed));
        Assert.Equal("payload", File.ReadAllText(renamed));
        Assert.False(File.Exists(Path.Combine(_work, "origin.txt")));

        var journalPath = _paths.JournalFileFor(plan.SessionId);
        Assert.Equal(journalPath, result.JournalPath);
        Assert.True(File.Exists(journalPath));
        Assert.Equal(3, CountJournalActions(journalPath));
    }

    // Gate 9 — a plan whose second action fails leaves a journal with exactly one completed action.
    [Fact]
    public void Apply_stops_at_first_failure_and_journals_only_the_completed_action()
    {
        var subDir = Path.Combine(_work, "sub");
        var neverReached = Path.Combine(_work, "never-reached");

        var plan = new Plan(
            SessionId: "session-" + Guid.NewGuid().ToString("N"),
            CreatedUtc: DateTimeOffset.UtcNow,
            Operation: "test-partial-failure",
            Actions: new[]
            {
                new PlannedAction(ActionKind.CreateDirectory, subDir, null, "first: succeeds"),
                new PlannedAction(ActionKind.Move, Path.Combine(_work, "missing.txt"), Path.Combine(subDir, "x.txt"), "second: fails, source missing"),
                new PlannedAction(ActionKind.CreateDirectory, neverReached, null, "third: must not run"),
            });

        var result = new Executor(_paths).Execute(plan, apply: true);

        Assert.False(result.Success);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(ActionStatus.Completed, result.Results[0].Status);
        Assert.Equal(ActionStatus.Failed, result.Results[1].Status);
        Assert.Equal(ActionStatus.Skipped, result.Results[2].Status);

        Assert.True(Directory.Exists(subDir));         // first action ran
        Assert.False(Directory.Exists(neverReached));  // third action did not

        var journalPath = _paths.JournalFileFor(plan.SessionId);
        Assert.True(File.Exists(journalPath));
        Assert.Equal(1, CountJournalActions(journalPath));
    }

    // WriteText writes its Content field (never Destination) to Source.
    [Fact]
    public void Apply_writes_text_from_the_content_field()
    {
        var target = Path.Combine(_work, "note.txt");
        var plan = new Plan(
            SessionId: "session-" + Guid.NewGuid().ToString("N"),
            CreatedUtc: DateTimeOffset.UtcNow,
            Operation: "test-write-text",
            Actions: new[]
            {
                new PlannedAction(ActionKind.WriteText, target, Destination: null, Reason: "write a note", Content: "hello world")
            });

        var result = new Executor(_paths).Execute(plan, apply: true);

        Assert.True(result.Success);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal("hello world", File.ReadAllText(target));
    }

    private Plan ThreeActionPlan()
    {
        var origin = Path.Combine(_work, "origin.txt");
        var subDir = Path.Combine(_work, "sub");
        var moved = Path.Combine(subDir, "origin.txt");
        var renamed = Path.Combine(subDir, "renamed.txt");

        return new Plan(
            SessionId: "session-" + Guid.NewGuid().ToString("N"),
            CreatedUtc: DateTimeOffset.UtcNow,
            Operation: "test-three-action",
            Actions: new[]
            {
                new PlannedAction(ActionKind.CreateDirectory, subDir, null, "create sub directory"),
                new PlannedAction(ActionKind.Move, origin, moved, "move file into sub"),
                new PlannedAction(ActionKind.Rename, moved, renamed, "rename moved file"),
            });
    }

    private string SnapshotWork()
    {
        if (!Directory.Exists(_work))
        {
            return "<missing>";
        }

        var entries = Directory
            .EnumerateFileSystemEntries(_work, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Directory.Exists(path)
                ? $"D {Path.GetRelativePath(_work, path)}"
                : $"F {Path.GetRelativePath(_work, path)} {new FileInfo(path).Length} {File.ReadAllText(path)}");

        return string.Join('\n', entries);
    }

    private static int CountJournalActions(string journalPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(journalPath));
        return document.RootElement.GetProperty("CompletedActions").GetArrayLength();
    }
}
