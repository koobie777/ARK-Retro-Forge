namespace ARK.Core.Psx;

/// <summary>
/// Interface for CHD conversion operations using chdman.
/// </summary>
public interface IChdTool
{
    /// <summary>
    /// Converts a CUE file and its associated BIN files to CHD format.
    /// </summary>
    /// <param name="cuePath">Path to the CUE file.</param>
    /// <param name="chdPath">Output path for the CHD file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exit code from chdman (0 = success).</returns>
    Task<int> ConvertCueToChd(string cuePath, string chdPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a CHD file back to BIN/CUE format.
    /// </summary>
    /// <param name="chdPath">Path to the CHD file.</param>
    /// <param name="outputDirectory">Directory where BIN/CUE files will be created.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exit code from chdman (0 = success).</returns>
    Task<int> ConvertChdToBinCue(string chdPath, string outputDirectory, CancellationToken cancellationToken = default);
}
