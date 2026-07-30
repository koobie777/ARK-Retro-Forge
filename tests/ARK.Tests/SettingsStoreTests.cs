using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ARK.Core.Execution;
using ARK.Core.Instances;
using ARK.Core.Settings;

namespace ARK.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root;
    private readonly InstancePaths _paths;
    private readonly SettingsStore _store;

    public SettingsStoreTests()
    {
        _root = TempRoot.Create();
        _paths = new InstancePaths("settings-tests", _root);
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Gate 3 — write settings, read them back, values identical.
    [Fact]
    [RequiresUnreferencedCode("Exercises SettingsStore JSON.")]
    public void Write_then_read_returns_identical_values()
    {
        var settings = new ArkSettings { RomRoot = @"D:\roms", ActiveSystem = "n64" };

        new Executor(_paths).Execute(_store.BuildWritePlan(settings), apply: true);
        var readBack = _store.Read();

        Assert.Equal(settings.RomRoot, readBack.RomRoot);
        Assert.Equal(settings.ActiveSystem, readBack.ActiveSystem);
        Assert.Equal(settings.SchemaVersion, readBack.SchemaVersion);
    }

    // Gate 4 — the write goes through the Executor and is journaled; the payload rides in Content and
    // Destination carries only a path (here, none).
    [Fact]
    [RequiresUnreferencedCode("Exercises SettingsStore JSON.")]
    public void Write_plan_carries_payload_in_content_and_is_journaled()
    {
        var settings = new ArkSettings { RomRoot = @"D:\roms", ActiveSystem = "snes" };
        var plan = _store.BuildWritePlan(settings);

        var writeText = Assert.Single(plan.Actions.Where(a => a.Kind == ActionKind.WriteText));
        Assert.Equal(_paths.SettingsFile, writeText.Source);
        Assert.Null(writeText.Destination);                 // Destination is a path slot, never content
        Assert.NotNull(writeText.Content);
        var fromContent = JsonSerializer.Deserialize<ArkSettings>(writeText.Content!);
        Assert.Equal("snes", fromContent!.ActiveSystem);    // Content carries the real payload

        var result = new Executor(_paths).Execute(plan, apply: true);
        Assert.True(result.Applied);
        Assert.NotNull(result.JournalPath);

        using var journal = JsonDocument.Parse(File.ReadAllText(result.JournalPath!));
        var journaledWrite = journal.RootElement.GetProperty("CompletedActions").EnumerateArray()
            .Single(a => a.GetProperty("Kind").GetString() == "WriteText");
        Assert.False(string.IsNullOrEmpty(journaledWrite.GetProperty("Content").GetString()));
        Assert.Equal(JsonValueKind.Null, journaledWrite.GetProperty("Destination").ValueKind);
    }

    // Gate 5 & 6 — a missing file returns defaults, throws nothing, and touches nothing (no journal).
    [Fact]
    [RequiresUnreferencedCode("Exercises SettingsStore JSON.")]
    public void Read_of_missing_file_returns_defaults_and_touches_nothing()
    {
        var before = Snapshot(_root);

        var settings = _store.Read();

        Assert.Equal(ArkSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Null(settings.RomRoot);
        Assert.Null(settings.ActiveSystem);
        Assert.False(File.Exists(_paths.SettingsFile));
        Assert.False(Directory.Exists(_paths.Journal));
        Assert.Equal(before, Snapshot(_root));
    }

    // Gate 7 — a malformed file surfaces an error and is not silently reset.
    [Fact]
    [RequiresUnreferencedCode("Exercises SettingsStore JSON.")]
    public void Malformed_file_throws_and_is_left_untouched()
    {
        Directory.CreateDirectory(_paths.Root);
        const string garbage = "{ not valid json ";
        File.WriteAllText(_paths.SettingsFile, garbage);

        var exception = Assert.Throws<SettingsFormatException>(() => _store.Read());

        Assert.Equal(_paths.SettingsFile, exception.Path);
        Assert.Equal(garbage, File.ReadAllText(_paths.SettingsFile)); // not reset to defaults
    }

    // PlanSet* updates one field while preserving the rest.
    [Fact]
    [RequiresUnreferencedCode("Exercises SettingsStore JSON.")]
    public void PlanSetRomRoot_preserves_the_active_system()
    {
        var executor = new Executor(_paths);
        executor.Execute(_store.BuildWritePlan(new ArkSettings { ActiveSystem = "nes" }), apply: true);
        executor.Execute(_store.PlanSetRomRoot(@"E:\games"), apply: true);

        var settings = _store.Read();
        Assert.Equal(@"E:\games", settings.RomRoot);
        Assert.Equal("nes", settings.ActiveSystem);
    }

    private static string Snapshot(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return "<missing>";
        }

        var entries = Directory
            .EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);

        return string.Join('\n', entries);
    }
}
