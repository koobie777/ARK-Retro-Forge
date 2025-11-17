using System.Text.Json;

namespace ARK.Cli.Infrastructure;

internal sealed record SessionState
{
    public string? RomRoot { get; init; }
    public string SystemCode { get; init; } = "psx";
    public bool MenuDryRun { get; init; } = true;
}

internal static class SessionStateManager
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly object SyncRoot = new();
    private static SessionState _state = new();
    private static string? _statePath;
    private static bool _loaded;

    public static SessionState State
    {
        get
        {
            EnsureLoaded();
            return _state;
        }
    }

    public static void Update(Func<SessionState, SessionState> updater)
    {
        lock (SyncRoot)
        {
            EnsureLoaded();
            _state = updater(_state);
            Persist();
        }
    }

    public static void Save()
    {
        lock (SyncRoot)
        {
            if (!_loaded)
            {
                return;
            }

            Persist();
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_loaded)
            {
                return;
            }

            var instanceRoot = InstancePathResolver.GetInstanceRoot();
            Directory.CreateDirectory(instanceRoot);
            _statePath = Path.Combine(instanceRoot, "session.json");

            if (File.Exists(_statePath))
            {
                try
                {
                    var json = File.ReadAllText(_statePath);
                    var restored = JsonSerializer.Deserialize<SessionState>(json, SerializerOptions);
                    if (restored != null)
                    {
                        _state = restored;
                    }
                }
                catch
                {
                    // Ignore corrupted state, a new file will be written.
                }
            }

            _loaded = true;
        }
    }

    private static void Persist()
    {
        if (_statePath == null)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(_state, SerializerOptions);
            File.WriteAllText(_statePath, json);
        }
        catch
        {
            // Non-fatal: session data is best-effort.
        }
    }
}
