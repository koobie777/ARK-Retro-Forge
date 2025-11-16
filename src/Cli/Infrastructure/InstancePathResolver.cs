namespace ARK.Cli.Infrastructure;

internal static class InstancePathResolver
{
    private static string _currentInstance = InitializeFromEnvironment();

    public static string CurrentInstance => _currentInstance;

    public static void SetInstanceName(string? instanceName)
    {
        _currentInstance = Sanitize(instanceName);
    }

    public static string GetInstanceRoot()
    {
        var sanitized = Sanitize(_currentInstance);
        var path = Path.Combine(AppContext.BaseDirectory, "instances", sanitized);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string Sanitize(string? value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value)
            ? "default"
            : value.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(ch, '_');
        }
        return trimmed;
    }

    private static string InitializeFromEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ARKRF_INSTANCE");
        return Sanitize(env);
    }
}
