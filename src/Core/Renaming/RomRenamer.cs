using System.Text.RegularExpressions;

namespace ARK.Core.Renaming;

/// <summary>
/// Provides deterministic ROM file renaming to "Title (Region) [ID]" format
/// </summary>
public partial class RomRenamer
{
    [GeneratedRegex(@"^(.+?)\s*\(([^)]+)\)\s*\[([^\]]+)\]")]
    private static partial Regex StandardNamePattern();

    /// <summary>
    /// Generate a standardized filename from metadata
    /// </summary>
    public static string GenerateStandardName(RomMetadata metadata)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(metadata.Title))
        {
            parts.Add(metadata.Title.Trim());
        }

        if (!string.IsNullOrWhiteSpace(metadata.Region))
        {
            parts.Add($"({metadata.Region.Trim()})");
        }

        if (!string.IsNullOrWhiteSpace(metadata.Id))
        {
            parts.Add($"[{metadata.Id.Trim()}]");
        }

        var filename = string.Join(" ", parts);

        if (!string.IsNullOrWhiteSpace(metadata.Extension))
        {
            filename += metadata.Extension;
        }

        return SanitizeFileName(filename);
    }

    /// <summary>
    /// Check if a filename already follows the standard naming convention
    /// </summary>
    public static bool IsStandardName(string fileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return StandardNamePattern().IsMatch(nameWithoutExtension);
    }

    /// <summary>
    /// Create a rename operation for a file
    /// </summary>
    public static RenameOperation CreateRenameOperation(string filePath, RomMetadata metadata)
    {
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var currentFileName = Path.GetFileName(filePath);
        var newFileName = GenerateStandardName(metadata);
        var newPath = Path.Combine(directory, newFileName);

        var isAlreadyNamed = string.Equals(currentFileName, newFileName, StringComparison.OrdinalIgnoreCase);

        string? warning = null;
        if (File.Exists(newPath) && !string.Equals(filePath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            warning = "Destination file already exists";
        }

        return new RenameOperation
        {
            SourcePath = filePath,
            DestinationPath = newPath,
            DestinationFileName = newFileName,
            IsAlreadyNamed = isAlreadyNamed,
            Warning = warning
        };
    }

    /// <summary>
    /// Sanitize a filename by removing invalid characters
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Trim();
    }
}
