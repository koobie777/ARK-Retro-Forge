namespace ARK.Tests;

/// <summary>Helpers for locating committed test fixtures and shipped config.</summary>
internal static class TestFixtures
{
    /// <summary>Reads a committed fixture (copied next to the test assembly).</summary>
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>Locates the shipped <c>config/systems</c> directory by walking up from the test bin.</summary>
    public static string ShippedSystemsDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config", "systems");
            if (File.Exists(Path.Combine(candidate, "n64.json")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate config/systems.");
    }
}
