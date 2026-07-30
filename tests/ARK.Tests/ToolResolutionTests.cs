using ARK.Core.Tools;

namespace ARK.Tests;

public sealed class ToolResolutionTests : IDisposable
{
    private readonly string _root;

    public ToolResolutionTests()
    {
        _root = TempRoot.Create();
    }

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Gate 7 — executable name resolution is platform-appropriate.
    [Fact]
    public void Executable_file_name_is_platform_appropriate()
    {
        var expected = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";
        Assert.Equal(expected, ToolLocator.ExecutableFileName("chdman"));
    }

    // Gate 5 — a tool present in tools/ is found.
    [Fact]
    public void ToolManager_finds_a_tool_present_in_the_tools_directory()
    {
        var toolsRoot = Path.Combine(_root, "tools");
        Directory.CreateDirectory(toolsRoot);
        var executable = Path.Combine(toolsRoot, ToolLocator.ExecutableFileName("chdman"));
        File.WriteAllText(executable, "stub");

        // Empty PATH: the only way to find it is via tools/.
        var manager = new ToolManager(new ToolLocator(toolsRoot, pathVariable: string.Empty));
        var result = manager.CheckTool("chdman");

        Assert.True(result.IsFound);
        Assert.Equal(executable, result.Path);
    }

    // Gate 6 — a tool present on PATH but absent from tools/ is found.
    [Fact]
    public void ToolManager_finds_a_tool_on_PATH_absent_from_tools()
    {
        var toolsRoot = Path.Combine(_root, "tools");
        Directory.CreateDirectory(toolsRoot); // empty

        var pathDirectory = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(pathDirectory);
        var executable = Path.Combine(pathDirectory, ToolLocator.ExecutableFileName("ffmpeg"));
        File.WriteAllText(executable, "stub");

        var manager = new ToolManager(new ToolLocator(toolsRoot, pathVariable: pathDirectory));
        var result = manager.CheckTool("ffmpeg");

        Assert.True(result.IsFound);
        Assert.Equal(executable, result.Path);
    }

    // tools/ takes precedence over PATH.
    [Fact]
    public void Tools_directory_takes_precedence_over_PATH()
    {
        var toolsRoot = Path.Combine(_root, "tools");
        Directory.CreateDirectory(toolsRoot);
        var inTools = Path.Combine(toolsRoot, ToolLocator.ExecutableFileName("wit"));
        File.WriteAllText(inTools, "stub");

        var pathDirectory = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(pathDirectory);
        File.WriteAllText(Path.Combine(pathDirectory, ToolLocator.ExecutableFileName("wit")), "stub");

        var locator = new ToolLocator(toolsRoot, pathVariable: pathDirectory);
        Assert.Equal(inTools, locator.Locate("wit"));
    }
}
