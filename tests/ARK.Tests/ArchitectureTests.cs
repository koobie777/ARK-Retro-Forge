namespace ARK.Tests;

/// <summary>
/// Enforces the structural invariant from Phase 0: the <c>Executor</c> is the only type in
/// production code permitted to mutate the filesystem. This makes DRY-RUN safety and
/// reversibility impossible to violate by accident rather than matters of discipline.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] ForbiddenCalls =
    {
        "File.Move(",
        "File.Delete(",
        "File.WriteAllText(",
        "Directory.CreateDirectory(",
    };

    [Fact]
    public void Only_the_executor_mutates_the_filesystem()
    {
        var sourceRoot = FindSourceRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources(sourceRoot))
        {
            if (Path.GetFileName(file).Equals("Executor.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var call in ForbiddenCalls)
            {
                if (text.Contains(call, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)} contains '{call}'");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Filesystem-mutating calls are permitted only in Executor.cs. Offenders:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> EnumerateProductionSources(string sourceRoot)
    {
        var obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";

        return Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(obj, StringComparison.Ordinal) &&
                !path.Contains(bin, StringComparison.Ordinal));
    }

    private static string FindSourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var src = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(src, "ARK.Core")) &&
                Directory.Exists(Path.Combine(src, "ARK.Cli")))
            {
                return src;
            }
        }

        throw new DirectoryNotFoundException("Could not locate src/ containing ARK.Core and ARK.Cli.");
    }
}
