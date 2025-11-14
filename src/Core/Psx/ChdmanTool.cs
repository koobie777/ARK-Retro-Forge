using ARK.Core.Tools;

namespace ARK.Core.Psx;

/// <summary>
/// Implementation of IChdTool using chdman from ToolManager
/// </summary>
public class ChdmanTool : IChdTool
{
    private readonly ToolManager _toolManager;
    
    public ChdmanTool(ToolManager? toolManager = null)
    {
        _toolManager = toolManager ?? new ToolManager();
    }
    
    /// <summary>
    /// Convert a CUE file to CHD format
    /// </summary>
    public async Task<int> ConvertCueToChd(string cuePath, string chdPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(cuePath))
        {
            throw new FileNotFoundException($"CUE file not found: {cuePath}");
        }
        
        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(chdPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }
        
        // chdman createcd -i "input.cue" -o "output.chd"
        var arguments = $"createcd -i \"{cuePath}\" -o \"{chdPath}\"";
        
        var (exitCode, output, error) = await _toolManager.ExecuteToolAsync("chdman", arguments, cancellationToken);
        
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"chdman failed with exit code {exitCode}: {error}");
        }
        
        return exitCode;
    }
    
    /// <summary>
    /// Convert a CHD file to BIN/CUE format
    /// </summary>
    public async Task<int> ConvertChdToBinCue(string chdPath, string outputDirectory, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(chdPath))
        {
            throw new FileNotFoundException($"CHD file not found: {chdPath}");
        }
        
        // Ensure output directory exists
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
        
        // Determine output file name (same as CHD but with .cue extension)
        var baseName = Path.GetFileNameWithoutExtension(chdPath);
        var outputCue = Path.Combine(outputDirectory, baseName + ".cue");
        
        // chdman extractcd -i "input.chd" -o "output.cue"
        var arguments = $"extractcd -i \"{chdPath}\" -o \"{outputCue}\"";
        
        var (exitCode, output, error) = await _toolManager.ExecuteToolAsync("chdman", arguments, cancellationToken);
        
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"chdman failed with exit code {exitCode}: {error}");
        }
        
        return exitCode;
    }
}
