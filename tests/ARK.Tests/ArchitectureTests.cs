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

    // SharpCompress 0.41.0 carries an unpatched zip-slip path traversal in
    // IArchive.WriteToDirectory() (CVE-2026-44788 / GHSA-6c8g-7p36-r338): a crafted archive
    // escapes the target root, escalating to arbitrary file writes on TAR via symlink chaining.
    // Every release through 0.47.4 is affected, so there is no version bump to take — banning the
    // method is the mitigation. ARK never needs it: scan reads the entry list and hashing streams
    // a single entry. If extraction ever becomes a verb it must be hand-rolled with per-entry path
    // validation against the target root, never routed through this method.
    [Fact]
    public void SharpCompress_WriteToDirectory_is_never_referenced()
    {
        var sourceRoot = FindSourceRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources(sourceRoot))
        {
            if (File.ReadAllText(file).Contains("WriteToDirectory(", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "SharpCompress IArchive.WriteToDirectory() is banned (unpatched zip-slip, GHSA-6c8g-7p36-r338). " +
            "Extraction must be hand-rolled with path validation. Offenders:" +
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
