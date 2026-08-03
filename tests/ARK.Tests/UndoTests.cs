using System.Text.Json;
using ARK.Core.Execution;
using ARK.Core.Instances;
using ARK.Core.Serialization;

namespace ARK.Tests;

/// <summary>
/// Phase 6: inversion, preconditions, and the verb. Undo lands before anything destructive ships,
/// so there is never a window in which ARK can do something it cannot take back.
/// </summary>
public sealed class UndoTests : IDisposable
{
    private readonly string _root = TempRoot.Create();
    private readonly InstancePaths _paths;
    private readonly Executor _executor;
    private readonly JournalStore _journals;
    private readonly UndoService _undo;

    public UndoTests()
    {
        _paths = new InstancePaths("undo", _root);
        Directory.CreateDirectory(_paths.Journal);
        _executor = new Executor(_paths);
        _journals = new JournalStore(_paths);
        _undo = new UndoService(_journals, _executor);
    }

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Gate 6. The headline: a real session of moves, renames and quarantines reverses exactly.
    [Fact]
    public void Moves_renames_and_quarantines_reverse_to_the_exact_starting_state()
    {
        var games = Dir("games");
        var sorted = Path.Combine(_root, "sorted");
        var quarantine = Path.Combine(_root, "quarantine");

        var a = Write(Path.Combine(games, "Tekken 3 (USA).zip"), "aaa");
        var b = Write(Path.Combine(games, "wrongname.zip"), "bbb");
        var c = Write(Path.Combine(games, "Duplicate (USA).zip"), "ccc");

        var before = Snapshot();

        var plan = new Plan("session-1", DateTimeOffset.UnixEpoch, "organize", new[]
        {
            new PlannedAction(ActionKind.CreateDirectory, sorted, null, "make target"),
            new PlannedAction(ActionKind.CreateDirectory, quarantine, null, "make quarantine"),
            new PlannedAction(ActionKind.Move, a, Path.Combine(sorted, "Tekken 3 (USA).zip"), "organize"),
            new PlannedAction(ActionKind.Rename, b, Path.Combine(games, "Right Name (USA).zip"), "canonical name"),
            new PlannedAction(ActionKind.Quarantine, c, Path.Combine(quarantine, "Duplicate (USA).zip"), "duplicate"),
        });

        Assert.True(_executor.Execute(plan, apply: true).Success);
        Assert.NotEqual(before, Snapshot());

        var (preview, result) = _undo.Apply("session-1");

        Assert.Null(preview.Refusal);
        Assert.True(result!.Success);
        Assert.Equal(before, Snapshot());
    }

    // Gate 7.
    [Fact]
    public void Actions_are_inverted_in_reverse_order()
    {
        var journal = new JournalDocument("s", DateTimeOffset.UnixEpoch, "op", new[]
        {
            new PlannedAction(ActionKind.CreateDirectory, @"D:\one", null, "first"),
            new PlannedAction(ActionKind.Move, @"D:\a", @"D:\b", "second"),
            new PlannedAction(ActionKind.Rename, @"D:\c", @"D:\d", "third"),
        });

        var plan = JournalInverter.Invert(journal, "undo-s", DateTimeOffset.UnixEpoch);

        Assert.Equal(new[] { "third", "second", "first" },
            plan.Actions.Select(action => action.Reason.Split(": ")[^1]).ToArray());
        Assert.Equal("s", plan.ReversesSessionId);
    }

    [Theory]
    [InlineData(ActionKind.Move, ActionKind.Move)]
    [InlineData(ActionKind.Rename, ActionKind.Rename)]
    // Restoring from quarantine is an ordinary move back, not another quarantine.
    [InlineData(ActionKind.Quarantine, ActionKind.Move)]
    public void Inverse_swaps_source_and_destination(ActionKind forward, ActionKind expected)
    {
        var inverse = JournalInverter.Inverse(new PlannedAction(forward, @"D:\from", @"D:\to", "because"));

        Assert.Equal(expected, inverse.Kind);
        Assert.Equal(@"D:\to", inverse.Source);
        Assert.Equal(@"D:\from", inverse.Destination);
    }

    // Gate 8. Restoring the prior state never means discarding what arrived afterwards.
    [Fact]
    public void CreateDirectory_inverse_removes_an_empty_directory_but_leaves_a_filled_one()
    {
        var empty = Path.Combine(_root, "empty");
        var filled = Path.Combine(_root, "filled");

        var plan = new Plan("session-dirs", DateTimeOffset.UnixEpoch, "provision", new[]
        {
            new PlannedAction(ActionKind.CreateDirectory, empty, null, "empty"),
            new PlannedAction(ActionKind.CreateDirectory, filled, null, "filled"),
        });

        Assert.True(_executor.Execute(plan, apply: true).Success);

        // The user puts something in one of them afterwards.
        Write(Path.Combine(filled, "mine.txt"), "user data");

        var preview = _undo.Prepare("session-dirs");
        _undo.Apply("session-dirs");

        Assert.False(Directory.Exists(empty));
        Assert.True(Directory.Exists(filled));
        Assert.True(File.Exists(Path.Combine(filled, "mine.txt")));

        // Reported as a deliberate decision, not as something an earlier run had already done.
        var left = Assert.Single(preview.LeftAlone);
        Assert.Equal(filled, left.Action.Source);
        Assert.Empty(preview.AlreadyDone);
    }

    // Gate 9. Resolution chosen: capture prior content at write time.
    [Fact]
    public void WriteText_over_an_existing_file_restores_the_prior_content()
    {
        var settings = Write(Path.Combine(_root, "settings.json"), "{\"before\":true}");

        var plan = new Plan("session-write", DateTimeOffset.UnixEpoch, "config-set", new[]
        {
            new PlannedAction(ActionKind.WriteText, settings, null, "set value", "{\"after\":true}"),
        });

        Assert.True(_executor.Execute(plan, apply: true).Success);
        Assert.Equal("{\"after\":true}", File.ReadAllText(settings));

        // The journal captured what was displaced, which is the only way this is reversible.
        var journalled = Assert.Single(_journals.Read("session-write").Document!.CompletedActions);
        Assert.Equal("{\"before\":true}", journalled.PriorContent);

        Assert.True(_undo.Apply("session-write").Result!.Success);
        Assert.Equal("{\"before\":true}", File.ReadAllText(settings));

        // Re-running recognizes the restore already happened rather than writing it again.
        var (again, noResult) = _undo.Apply("session-write");
        Assert.Null(noResult);
        Assert.Single(again.AlreadyDone);
        Assert.Empty(again.Actionable);
    }

    // Restoring over a file edited since the session would discard that edit — the same hazard as
    // restoring a move over an unrelated file, and it gets the same refusal.
    [Fact]
    public void WriteText_restore_refuses_a_file_edited_since_the_session()
    {
        var settings = Write(Path.Combine(_root, "settings.json"), "original");

        Assert.True(_executor.Execute(
            new Plan("session-edited", DateTimeOffset.UnixEpoch, "config-set", new[]
            {
                new PlannedAction(ActionKind.WriteText, settings, null, "set", "written by ark"),
            }),
            apply: true).Success);

        File.WriteAllText(settings, "the user edited this afterwards");

        Assert.False(_undo.Prepare("session-edited").CanApply);
        Assert.True(_undo.Prepare("session-edited", force: true).CanApply);

        // Refused means untouched.
        Assert.Equal("the user edited this afterwards", File.ReadAllText(settings));
    }

    // A file that did not exist before is removed again, not left behind empty.
    [Fact]
    public void WriteText_creating_a_new_file_inverts_to_removing_it()
    {
        var created = Path.Combine(_root, "new-settings.json");

        var plan = new Plan("session-new", DateTimeOffset.UnixEpoch, "config-set", new[]
        {
            new PlannedAction(ActionKind.WriteText, created, null, "first write", "{}"),
        });

        Assert.True(_executor.Execute(plan, apply: true).Success);
        Assert.True(File.Exists(created));

        Assert.True(_undo.Apply("session-new").Result!.Success);
        Assert.False(File.Exists(created));
    }

    // Phase 7 gate 3. The mirror of the restore refusal: if the session created a file and the
    // user has edited it since, removing it on undo would destroy that edit.
    [Fact]
    public void DeleteFile_inverse_refuses_a_file_edited_since_the_session_created_it()
    {
        var created = Path.Combine(_root, "created.json");

        Assert.True(_executor.Execute(
            new Plan("session-created", DateTimeOffset.UnixEpoch, "config-set", new[]
            {
                new PlannedAction(ActionKind.WriteText, created, null, "first write", "{}"),
            }),
            apply: true).Success);

        File.WriteAllText(created, "{\"edited\":true}");

        Assert.False(_undo.Prepare("session-created").CanApply);
        Assert.Equal("{\"edited\":true}", File.ReadAllText(created));

        // Explicit, per-run, never the default.
        Assert.True(_undo.Prepare("session-created", force: true).CanApply);
        Assert.True(_undo.Apply("session-created", force: true).Result!.Success);
        Assert.False(File.Exists(created));
    }

    // Gate 9's other half: undo never silently skips something it cannot reverse.
    [Fact]
    public void A_session_containing_an_uninvertible_action_is_refused_whole()
    {
        WriteJournal(new JournalDocument("session-bad", DateTimeOffset.UnixEpoch, "op", new[]
        {
            new PlannedAction(ActionKind.Move, @"D:\a", @"D:\b", "fine"),
            new PlannedAction(ActionKind.DeleteFile, @"D:\gone", null, "not invertible"),
        }));

        var preview = _undo.Prepare("session-bad");

        Assert.NotNull(preview.Refusal);
        Assert.Contains("DeleteFile", preview.Refusal!, StringComparison.Ordinal);
        Assert.Empty(preview.Steps);
    }

    // Gate 10.
    [Fact]
    public void Precondition_failure_stops_the_run_and_changes_nothing()
    {
        var games = Dir("games");
        var sorted = Dir("sorted");
        var a = Write(Path.Combine(games, "A (USA).zip"), "aaa");
        var b = Write(Path.Combine(games, "B (USA).zip"), "bbb");

        var plan = new Plan("session-block", DateTimeOffset.UnixEpoch, "organize", new[]
        {
            new PlannedAction(ActionKind.Move, a, Path.Combine(sorted, "A (USA).zip"), "move a"),
            new PlannedAction(ActionKind.Move, b, Path.Combine(sorted, "B (USA).zip"), "move b"),
        });
        Assert.True(_executor.Execute(plan, apply: true).Success);

        // Something else now occupies the path one of them would be restored to.
        Write(a, "a different file entirely");
        var before = Snapshot();

        var (preview, result) = _undo.Apply("session-block");

        Assert.NotNull(preview.Blocker);
        Assert.False(preview.CanApply);
        Assert.Null(result);

        // Nothing moved — not even the action that would have succeeded.
        Assert.Equal(before, Snapshot());
    }

    // Gate 11.
    [Fact]
    public void Restoring_over_an_existing_file_requires_force()
    {
        var games = Dir("games");
        var sorted = Dir("sorted");
        var a = Write(Path.Combine(games, "A (USA).zip"), "original");

        Assert.True(_executor.Execute(
            new Plan("session-force", DateTimeOffset.UnixEpoch, "organize", new[]
            {
                new PlannedAction(ActionKind.Move, a, Path.Combine(sorted, "A (USA).zip"), "move"),
            }),
            apply: true).Success);

        Write(a, "something else");

        Assert.False(_undo.Prepare("session-force").CanApply);
        Assert.True(_undo.Prepare("session-force", force: true).CanApply);
    }

    // Gate 12.
    [Fact]
    public void Undo_without_apply_touches_nothing()
    {
        var games = Dir("games");
        var sorted = Dir("sorted");
        var a = Write(Path.Combine(games, "A (USA).zip"), "aaa");

        Assert.True(_executor.Execute(
            new Plan("session-dry", DateTimeOffset.UnixEpoch, "organize", new[]
            {
                new PlannedAction(ActionKind.Move, a, Path.Combine(sorted, "A (USA).zip"), "move"),
            }),
            apply: true).Success);

        var before = Snapshot();
        var preview = _undo.Prepare("session-dry");

        Assert.True(preview.CanApply);
        Assert.Single(preview.Actionable);
        Assert.Equal(before, Snapshot());
    }

    // Gate 13.
    [Fact]
    public void Undo_writes_its_own_journal_marked_as_reversing_the_target()
    {
        var games = Dir("games");
        var sorted = Dir("sorted");
        var a = Write(Path.Combine(games, "A (USA).zip"), "aaa");

        _executor.Execute(
            new Plan("session-audit", DateTimeOffset.UnixEpoch, "organize", new[]
            {
                new PlannedAction(ActionKind.Move, a, Path.Combine(sorted, "A (USA).zip"), "move"),
            }),
            apply: true);

        var (_, result) = _undo.Apply("session-audit");

        var undoJournal = _journals.Read(result!.SessionId);
        Assert.True(undoJournal.Succeeded);
        Assert.Equal("session-audit", undoJournal.Document!.ReversesSessionId);
        Assert.True(undoJournal.Document.IsUndo);

        // And the original now reads as reversed.
        var original = _journals.List().Single(session => session.SessionId == "session-audit");
        Assert.True(original.IsReversed);
    }

    // Gate 14. The interrupted case: half reversed, then run again.
    [Fact]
    public void Re_running_after_an_interrupted_undo_resumes_without_double_applying()
    {
        var games = Dir("games");
        var sorted = Dir("sorted");
        var a = Write(Path.Combine(games, "A (USA).zip"), "aaa");
        var b = Write(Path.Combine(games, "B (USA).zip"), "bbb");
        var before = Snapshot();

        _executor.Execute(
            new Plan("session-resume", DateTimeOffset.UnixEpoch, "organize", new[]
            {
                new PlannedAction(ActionKind.Move, a, Path.Combine(sorted, "A (USA).zip"), "move a"),
                new PlannedAction(ActionKind.Move, b, Path.Combine(sorted, "B (USA).zip"), "move b"),
            }),
            apply: true);

        // Stand in for a process killed mid-undo: one of the two reversals already happened.
        File.Move(Path.Combine(sorted, "B (USA).zip"), b);

        var preview = _undo.Prepare("session-resume");
        Assert.Single(preview.AlreadyDone);
        Assert.Single(preview.Actionable);

        var (_, result) = _undo.Apply("session-resume");

        Assert.True(result!.Success);
        Assert.Equal(1, result.CompletedCount); // only the outstanding one ran
        Assert.Equal(before, Snapshot());

        // Running a third time is a no-op rather than an error or a repeat.
        var (again, noResult) = _undo.Apply("session-resume");
        Assert.Null(noResult);
        Assert.Empty(again.Actionable);
        Assert.Equal(before, Snapshot());
    }

    // Gate 15.
    [Fact]
    public void Journal_list_shows_sessions_and_their_reversed_state()
    {
        var games = Dir("games");
        var sorted = Dir("sorted");
        var a = Write(Path.Combine(games, "A (USA).zip"), "aaa");

        _executor.Execute(
            new Plan("session-list", DateTimeOffset.UnixEpoch, "organize", new[]
            {
                new PlannedAction(ActionKind.Move, a, Path.Combine(sorted, "A (USA).zip"), "move"),
            }),
            apply: true);

        var listed = Assert.Single(_journals.List());
        Assert.Equal("session-list", listed.SessionId);
        Assert.Equal("organize", listed.Operation);
        Assert.Equal(1, listed.ActionCount);
        Assert.False(listed.IsReversed);

        _undo.Apply("session-list");

        var after = _journals.List();
        Assert.Equal(2, after.Count);
        Assert.True(after.Single(session => session.SessionId == "session-list").IsReversed);
        Assert.Contains(after, session => session.IsUndo);
    }

    // Gate 5.
    [Fact]
    public void A_journal_from_a_newer_build_is_refused()
    {
        WriteJournal(new JournalDocument(
            "session-future", DateTimeOffset.UnixEpoch, "op",
            new[] { new PlannedAction(ActionKind.Move, @"D:\a", @"D:\b", "x") },
            JournalDocument.CurrentSchemaVersion + 1));

        var load = _journals.Read("session-future");

        Assert.Equal(JournalFault.SchemaTooNew, load.Fault);
        Assert.NotNull(_undo.Prepare("session-future").Refusal);
    }

    // Gate 4.
    [Fact]
    public void A_journal_with_an_unrecognized_action_kind_is_refused_not_guessed()
    {
        var json = """
            {
              "SessionId": "session-alien",
              "CreatedUtc": "1970-01-01T00:00:00+00:00",
              "Operation": "op",
              "SchemaVersion": 1,
              "CompletedActions": [
                { "Kind": "Teleport", "Source": "D:\\a", "Destination": "D:\\b", "Reason": "from the future" }
              ]
            }
            """;
        File.WriteAllText(_paths.JournalFileFor("session-alien"), json);

        var load = _journals.Read("session-alien");

        Assert.Equal(JournalFault.UnknownActionKind, load.Fault);
        Assert.Null(load.Document);
        Assert.NotNull(_undo.Prepare("session-alien").Refusal);
    }

    // Undo-of-undo is not a feature; the journal record is the audit trail.
    [Fact]
    public void An_undo_session_cannot_itself_be_undone()
    {
        var games = Dir("games");
        var sorted = Dir("sorted");
        var a = Write(Path.Combine(games, "A (USA).zip"), "aaa");

        _executor.Execute(
            new Plan("session-once", DateTimeOffset.UnixEpoch, "organize", new[]
            {
                new PlannedAction(ActionKind.Move, a, Path.Combine(sorted, "A (USA).zip"), "move"),
            }),
            apply: true);

        var (_, result) = _undo.Apply("session-once");

        Assert.NotNull(_undo.Prepare(result!.SessionId).Refusal);
    }

    [Fact]
    public void An_unknown_session_is_reported_not_thrown()
    {
        Assert.Equal(JournalFault.NotFound, _journals.Read("nope").Fault);
        Assert.NotNull(_undo.Prepare("nope").Refusal);
    }

    private string Dir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Write(string path, string content)
    {
        File.WriteAllText(path, content);
        return path;
    }

    private void WriteJournal(JournalDocument document) =>
        File.WriteAllText(
            _paths.JournalFileFor(document.SessionId),
            JsonSerializer.Serialize(document, ArkJson.Write));

    // Everything under the root except ARK's own instance directory, which undo legitimately writes to.
    private string Snapshot() => string.Join("\n", Directory
        .EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
        .Where(path => !path.StartsWith(_paths.Root, StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => File.Exists(path) ? $"{path}:{File.ReadAllText(path)}" : path + "/"));
}
