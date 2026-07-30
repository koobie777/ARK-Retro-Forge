using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ARK.Core.Execution;
using ARK.Core.Instances;

namespace ARK.Core.Settings;

/// <summary>
/// Reads and writes instance settings. Reading is pure — a missing file yields defaults and touches
/// nothing. Writing does not happen here: settings changes are file writes, so (Prohibition 7) they
/// go through a <see cref="Plan"/> handed to the <see cref="Executor"/> like any other mutation,
/// which keeps them journaled and reversible. The write payload rides in the plan's
/// <see cref="PlannedAction.Content"/>; no settings data is ever placed in a path field.
/// </summary>
public sealed class SettingsStore
{
    private const string WriteOperation = "settings-write";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly InstancePaths _paths;

    /// <summary>Creates a store scoped to <paramref name="paths"/>' instance.</summary>
    public SettingsStore(InstancePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    /// <summary>
    /// Reads the instance's settings. A missing file returns defaults. A file that exists but fails
    /// to parse throws <see cref="SettingsFormatException"/> — it is never silently reset.
    /// </summary>
    [RequiresUnreferencedCode("Deserializes ArkSettings with reflection-based System.Text.Json.")]
    public ArkSettings Read()
    {
        var file = _paths.SettingsFile;
        if (!File.Exists(file))
        {
            return new ArkSettings();
        }

        var json = File.ReadAllText(file);
        try
        {
            return JsonSerializer.Deserialize<ArkSettings>(json, JsonOptions)
                ?? throw new SettingsFormatException($"Settings file '{file}' is malformed: it deserialized to null.") { Path = file };
        }
        catch (JsonException ex)
        {
            throw new SettingsFormatException($"Settings file '{file}' is malformed: {ex.Message}", ex) { Path = file };
        }
    }

    /// <summary>
    /// Builds the plan that persists <paramref name="settings"/>. The plan ensures the instance
    /// directory exists, then writes <c>settings.json</c> via a <see cref="ActionKind.WriteText"/>
    /// action whose <see cref="PlannedAction.Content"/> carries the serialized payload.
    /// </summary>
    [RequiresUnreferencedCode("Serializes ArkSettings with reflection-based System.Text.Json.")]
    public Plan BuildWritePlan(ArkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var payload = JsonSerializer.Serialize(settings, JsonOptions);
        return new Plan(
            SessionId: $"settings-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            CreatedUtc: DateTimeOffset.UtcNow,
            Operation: WriteOperation,
            Actions:
            [
                new PlannedAction(ActionKind.CreateDirectory, _paths.Root, null, "Ensure the instance directory exists"),
                new PlannedAction(ActionKind.WriteText, _paths.SettingsFile, null, "Write settings.json", Content: payload)
            ]);
    }

    /// <summary>Builds a plan that updates only the ROM root, preserving the other settings.</summary>
    [RequiresUnreferencedCode("Reads and serializes ArkSettings with reflection-based System.Text.Json.")]
    public Plan PlanSetRomRoot(string romRoot) => BuildWritePlan(Read() with { RomRoot = romRoot });

    /// <summary>Builds a plan that updates only the active system, preserving the other settings.</summary>
    [RequiresUnreferencedCode("Reads and serializes ArkSettings with reflection-based System.Text.Json.")]
    public Plan PlanSetActiveSystem(string activeSystem) => BuildWritePlan(Read() with { ActiveSystem = activeSystem });
}
