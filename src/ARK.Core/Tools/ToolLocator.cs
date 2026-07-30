namespace ARK.Core.Tools;

/// <summary>
/// Resolves where an external tool's executable lives: the shared <c>tools/</c> directory first,
/// then each entry on <c>PATH</c>. This is a path-resolution component — composition of tool paths
/// is centralized here rather than scattered through tool logic, which keeps the "nothing composes
/// paths outside the resolver" rule enforceable. Executable names are made platform-appropriate
/// (<c>.exe</c> on Windows, bare elsewhere), fixing v1's verbatim <c>chdman.exe</c> lookups.
/// </summary>
public sealed class ToolLocator
{
    private readonly string _toolsRoot;
    private readonly IReadOnlyList<string> _pathDirectories;

    /// <summary>Creates a locator rooted at <paramref name="toolsRoot"/> (the shared tools directory).</summary>
    /// <param name="toolsRoot">Shared tools directory, taken from the path resolver.</param>
    /// <param name="pathVariable">
    /// <c>PATH</c> contents to search after <c>tools/</c>. Defaults to the process <c>PATH</c>;
    /// tests supply a controlled value.
    /// </param>
    public ToolLocator(string toolsRoot, string? pathVariable = null)
    {
        ArgumentNullException.ThrowIfNull(toolsRoot);
        _toolsRoot = toolsRoot;
        var raw = pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        _pathDirectories = raw
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    /// <summary>Platform-appropriate executable file name for a base name.</summary>
    public static string ExecutableFileName(string baseName) =>
        OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

    /// <summary>
    /// Returns the full path to the tool's executable, searching <c>tools/</c> first and then each
    /// <c>PATH</c> directory, or null if it is present in neither.
    /// </summary>
    public string? Locate(string executableBaseName)
    {
        var fileName = ExecutableFileName(executableBaseName);

        var inTools = System.IO.Path.Combine(_toolsRoot, fileName);
        if (File.Exists(inTools))
        {
            return inTools;
        }

        foreach (var directory in _pathDirectories)
        {
            var candidate = System.IO.Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
