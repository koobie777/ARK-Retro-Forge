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

    /// <summary>Locates the shipped <c>config/naming</c> directory by walking up from the test bin.</summary>
    public static string ShippedNamingDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config", "naming");
            if (File.Exists(Path.Combine(candidate, "regions.json")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate config/naming.");
    }

    /// <summary>Reads a committed naming-corpus file, skipping <c>#</c> comment and blank lines.</summary>
    public static IReadOnlyList<string> ReadCorpus(string name) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "corpus", name))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
}
