namespace ARK.Core.Plugins;

/// <summary>
/// Interface for system-specific modules that can be loaded as plugins
/// </summary>
public interface ISystemModule
{
    /// <summary>
    /// Unique identifier for the system (e.g., "PS1", "PS2", "N64")
    /// </summary>
    string SystemId { get; }

    /// <summary>
    /// Display name for the system
    /// </summary>
    string SystemName { get; }

    /// <summary>
    /// File extensions handled by this module
    /// </summary>
    IEnumerable<string> SupportedExtensions { get; }

    /// <summary>
    /// Initialize the module
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a file is valid for this system
    /// </summary>
    Task<bool> ValidateFileAsync(string filePath, CancellationToken cancellationToken = default);
}
