using System.Diagnostics;

namespace ARK.Core.Psx;

/// <summary>
/// Implementation of IChdTool that uses chdman.exe for CHD conversions.
/// </summary>
public class ChdmanTool : IChdTool
{
    private readonly string _chdmanPath;

    public ChdmanTool(string? chdmanPath = null)
    {
        _chdmanPath = chdmanPath ?? FindChdmanPath();
    }

    private static string FindChdmanPath()
    {
        // Look for chdman.exe in tools directory relative to the application
        var appDir = AppContext.BaseDirectory;
        var toolsDir = Path.Combine(appDir, "tools");

        var chdmanExe = Path.Combine(toolsDir, "chdman.exe");
        if (File.Exists(chdmanExe))
        {
            return chdmanExe;
        }

        // Also check chdman without .exe extension (for Linux/macOS)
        var chdman = Path.Combine(toolsDir, "chdman");
        if (File.Exists(chdman))
        {
            return chdman;
        }

        throw new FileNotFoundException(
            $"chdman executable not found in {toolsDir}. " +
            "Please download chdman and place it in the tools directory.");
    }

    public async Task<int> ConvertCueToChd(string cuePath, string chdPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(cuePath))
        {
            throw new FileNotFoundException($"CUE file not found: {cuePath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _chdmanPath,
            Arguments = $"createcd -i \"{cuePath}\" -o \"{chdPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    public async Task<int> ConvertChdToBinCue(string chdPath, string outputDirectory, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(chdPath))
        {
            throw new FileNotFoundException($"CHD file not found: {chdPath}");
        }

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _chdmanPath,
            Arguments = $"extractcd -i \"{chdPath}\" -o \"{Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(chdPath) + ".cue")}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
