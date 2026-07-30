using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ARK.Core.Tools;

/// <summary>
/// Discovers external tools and reports their status. Location is delegated to a
/// <see cref="ToolLocator"/>; this type composes no paths of its own. It reports per-tool status
/// only — no global pass/fail verdict, because whether a given tool's absence matters is the
/// calling operation's judgement (Phase 1 brief, defect 2).
/// </summary>
public sealed class ToolManager
{
    private static readonly ExternalTool[] KnownTools =
    [
        new()
        {
            Name = "chdman",
            ExecutableName = "chdman",
            MinimumVersion = "0.261",
            Description = "MAME CHD Manager - PS1/PS2/Dreamcast CHD compression",
            VersionArguments = ["-version"],
            VersionPattern = @"\d+(\.\d+)+"
        },
        new()
        {
            Name = "maxcso",
            ExecutableName = "maxcso",
            Description = "PSP/PS2 CSO compression",
            VersionArguments = ["--version", "-version"]
        },
        new()
        {
            Name = "wit",
            ExecutableName = "wit",
            Description = "Wii Image Tool - Wii/WiiU image management",
            VersionArguments = ["--version", "-version"]
        },
        new()
        {
            Name = "dolphin-tool",
            ExecutableName = "dolphin-tool",
            Description = "Dolphin Tool - GameCube/Wii RVZ compression",
            VersionArguments = ["--version", "-version"]
        },
        new()
        {
            Name = "wuxtool",
            ExecutableName = "wuxtool",
            Description = "Wii U WUX compression",
            VersionArguments = ["--version", "-version"]
        },
        new()
        {
            Name = "nsz",
            ExecutableName = "nsz",
            Description = "Nintendo Switch NSZ compression",
            VersionArguments = ["--version", "-version"]
        },
        new()
        {
            Name = "ffmpeg",
            ExecutableName = "ffmpeg",
            Description = "Media file processing",
            VersionArguments = ["-version"]
        }
    ];

    private static readonly string[] DefaultVersionArguments = ["--version", "-version", "-v"];
    private const int VersionCommandTimeoutMilliseconds = 3000;
    private static readonly Regex DefaultVersionRegex = new(@"\d+(\.\d+)+", RegexOptions.Compiled);

    private readonly ToolLocator _locator;

    /// <summary>Creates a tool manager that locates executables via <paramref name="locator"/>.</summary>
    public ToolManager(ToolLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _locator = locator;
    }

    /// <summary>Every tool ARK knows how to look for.</summary>
    public IEnumerable<ExternalTool> GetKnownTools() => KnownTools;

    /// <summary>Checks the status of every known tool.</summary>
    public IEnumerable<ToolCheckResult> CheckAllTools() => KnownTools.Select(tool => CheckTool(tool.Name));

    /// <summary>Checks the status of a single tool by name.</summary>
    public ToolCheckResult CheckTool(string toolName)
    {
        var tool = KnownTools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            return new ToolCheckResult { Name = toolName, IsFound = false, ErrorMessage = "Unknown tool" };
        }

        var path = _locator.Locate(tool.ExecutableName);
        if (path is null)
        {
            return new ToolCheckResult
            {
                Name = tool.Name,
                IsFound = false,
                MinimumVersion = tool.MinimumVersion,
                ErrorMessage = $"'{ToolLocator.ExecutableFileName(tool.ExecutableName)}' not found in tools/ or on PATH"
            };
        }

        return new ToolCheckResult
        {
            Name = tool.Name,
            IsFound = true,
            Path = path,
            Version = GetToolVersion(path, tool),
            MinimumVersion = tool.MinimumVersion,
            // Real version comparison is deferred; v1 hardcoded this and it is not a Phase 1 defect.
            MeetsMinimumVersion = true
        };
    }

    private static string? GetToolVersion(string toolPath, ExternalTool tool)
    {
        var embedded = TryGetEmbeddedVersion(toolPath);
        if (!string.IsNullOrWhiteSpace(embedded))
        {
            return embedded;
        }

        var arguments = tool.VersionArguments is { Length: > 0 } ? tool.VersionArguments : DefaultVersionArguments;
        foreach (var args in arguments)
        {
            var output = TryRunVersionCommand(toolPath, args);
            if (string.IsNullOrWhiteSpace(output))
            {
                continue;
            }

            var parsed = ParseVersionFromOutput(output, tool.VersionPattern);
            if (!string.IsNullOrWhiteSpace(parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? TryGetEmbeddedVersion(string toolPath)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(toolPath);
            return versionInfo.FileVersion ?? versionInfo.ProductVersion;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? TryRunVersionCommand(string toolPath, string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

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

            if (!process.WaitForExit(VersionCommandTimeoutMilliseconds))
            {
                TryKill(process);
            }

            var combined = outputBuilder.ToString() + errorBuilder.ToString();
            return string.IsNullOrWhiteSpace(combined) ? null : combined;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
    }

    private static string? ParseVersionFromOutput(string output, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var regex = string.IsNullOrWhiteSpace(pattern)
            ? DefaultVersionRegex
            : new Regex(pattern, RegexOptions.IgnoreCase);

        var match = regex.Match(output);
        if (match.Success)
        {
            return match.Value;
        }

        var firstLine = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstLine?.Trim();
    }
}
