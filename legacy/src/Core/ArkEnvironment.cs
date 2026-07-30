using System.Text;

namespace ARK.Core;

/// <summary>
/// Provides shared helpers for resolving per-instance paths and settings.
/// </summary>
public static class ArkEnvironment
{
    private static string _currentInstance = InitializeFromEnvironment();

    /// <summary>
    /// Gets the sanitized instance name currently in use.
    /// </summary>
    public static string CurrentInstance => _currentInstance;

    /// <summary>
    /// Sets the active instance name and ensures future path lookups are scoped correctly.
    /// </summary>
    public static void SetInstanceName(string? instanceName)
    {
        _currentInstance = Sanitize(instanceName);
        Environment.SetEnvironmentVariable("ARKRF_INSTANCE", _currentInstance);
    }

    /// <summary>
    /// Returns (and creates when necessary) the directory that backs the active instance.
    /// </summary>
    public static string GetInstanceRoot()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "instances", _currentInstance);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Sanitizes user-provided names so they are safe for filesystem usage.
    /// </summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "default";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch);
        }

        var sanitized = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
    }

    private static string InitializeFromEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ARKRF_INSTANCE");
        return Sanitize(env);
    }
}
