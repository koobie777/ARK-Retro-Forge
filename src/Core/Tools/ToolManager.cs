using System.Diagnostics;

namespace ARK.Core.Tools;

/// <summary>
/// Manages discovery and execution of external tools
/// </summary>
public class ToolManager
{
    private readonly string _toolsDirectory;
    
    private static readonly ExternalTool[] KnownTools =
    [
        new() { Name = "chdman", ExecutableName = "chdman.exe", MinimumVersion = "0.261", Description = "MAME CHD Manager - PS1/PS2/Dreamcast CHD compression" },
        new() { Name = "maxcso", ExecutableName = "maxcso.exe", Description = "PSP/PS2 CSO compression", IsOptional = true },
        new() { Name = "wit", ExecutableName = "wit.exe", Description = "Wii Image Tool - Wii/WiiU image management", IsOptional = true },
        new() { Name = "dolphin-tool", ExecutableName = "dolphin-tool.exe", Description = "Dolphin Tool - GameCube/Wii RVZ compression", IsOptional = true },
        new() { Name = "wuxtool", ExecutableName = "wuxtool.exe", Description = "Wii U WUX compression", IsOptional = true },
        new() { Name = "nsz", ExecutableName = "nsz.exe", Description = "Nintendo Switch NSZ compression", IsOptional = true },
        new() { Name = "ffmpeg", ExecutableName = "ffmpeg.exe", Description = "Media file processing", IsOptional = true }
    ];

    public ToolManager(string? toolsDirectory = null)
    {
        _toolsDirectory = toolsDirectory ?? Path.Combine(AppContext.BaseDirectory, "tools");
    }

    /// <summary>
    /// Check if a specific tool is available
    /// </summary>
    public ToolCheckResult CheckTool(string toolName)
    {
        var tool = KnownTools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (tool == null)
        {
            return new ToolCheckResult
            {
                Name = toolName,
                IsFound = false,
                ErrorMessage = "Unknown tool",
                IsOptional = false
            };
        }

        var toolPath = Path.Combine(_toolsDirectory, tool.ExecutableName);
        if (!File.Exists(toolPath))
        {
            return new ToolCheckResult
            {
                Name = tool.Name,
                IsFound = false,
                MinimumVersion = tool.MinimumVersion,
                ErrorMessage = $"Not found in {_toolsDirectory}",
                IsOptional = tool.IsOptional
            };
        }

        // Try to get version (simplified - would need tool-specific logic)
        var versionInfo = GetToolVersion(toolPath);

        return new ToolCheckResult
        {
            Name = tool.Name,
            IsFound = true,
            Path = toolPath,
            Version = versionInfo,
            MinimumVersion = tool.MinimumVersion,
            MeetsMinimumVersion = true, // Simplified - would need actual version comparison
            IsOptional = tool.IsOptional
        };
    }

    /// <summary>
    /// Check all known tools
    /// </summary>
    public IEnumerable<ToolCheckResult> CheckAllTools()
    {
        return KnownTools.Select(tool => CheckTool(tool.Name));
    }

    /// <summary>
    /// Get all known external tools
    /// </summary>
    public IEnumerable<ExternalTool> GetKnownTools()
    {
        return KnownTools;
    }

    /// <summary>
    /// Execute an external tool
    /// </summary>
    public async Task<(int ExitCode, string Output, string Error)> ExecuteToolAsync(
        string toolName,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        var checkResult = CheckTool(toolName);
        if (!checkResult.IsFound || checkResult.Path == null)
        {
            throw new FileNotFoundException($"Tool '{toolName}' not found in {_toolsDirectory}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = checkResult.Path,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }

    private static string? GetToolVersion(string toolPath)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(toolPath);
            return versionInfo.FileVersion ?? versionInfo.ProductVersion;
        }
        catch
        {
            return null;
        }
    }
}
