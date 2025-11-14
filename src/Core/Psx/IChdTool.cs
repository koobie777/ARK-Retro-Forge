namespace ARK.Core.Psx;

/// <summary>
/// Interface for CHD conversion tools
/// </summary>
public interface IChdTool
{
    /// <summary>
    /// Convert a CUE file to CHD format
    /// </summary>
    Task<int> ConvertCueToChd(string cuePath, string chdPath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Convert a CHD file to BIN/CUE format
    /// </summary>
    Task<int> ConvertChdToBinCue(string chdPath, string outputDirectory, CancellationToken cancellationToken = default);
}
