using System.Diagnostics;
using System.Security.Cryptography;

namespace ARK.Core.Systems.PSX;

/// <summary>
/// Represents a duplicate file group
/// </summary>
public record DuplicateGroup
{
    public required string Hash { get; init; }
    public required List<DuplicateFileInfo> Files { get; init; }
    public string? Title { get; init; }
    public string? Serial { get; init; }
    public int? DiscNumber { get; init; }
}

/// <summary>
/// Represents a file in a duplicate group
/// </summary>
public record DuplicateFileInfo
{
    public required string FilePath { get; init; }
    public required long FileSize { get; init; }
    public required PsxDiscInfo DiscInfo { get; init; }
}

/// <summary>
/// Detects duplicate PSX disc images using hash-based detection
/// </summary>
public readonly record struct DuplicateScanProgress(
    int ProcessedFiles,
    int TotalFiles,
    long ProcessedBytes,
    long TotalBytes,
    string? CurrentFile,
    TimeSpan Elapsed);

public class PsxDuplicateDetector
{
    private readonly PsxNameParser _parser;
    
    public PsxDuplicateDetector(PsxNameParser? parser = null)
    {
        _parser = parser ?? new PsxNameParser();
    }
    
    /// <summary>
    /// Scan for duplicate disc images in a directory
    /// </summary>
    /// <param name="rootPath">Root directory to scan</param>
    /// <param name="recursive">Whether to scan recursively</param>
    /// <param name="hashAlgorithm">Hash algorithm to use (SHA1, MD5, CRC32)</param>
    /// <param name="progress">Optional progress reporter invoked for each hashed file</param>
    public List<DuplicateGroup> ScanForDuplicates(
        string rootPath, 
        bool recursive = false,
        string hashAlgorithm = "SHA1",
        IProgress<DuplicateScanProgress>? progress = null)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var psxExtensions = new[] { ".bin", ".cue", ".chd", ".pbp", ".iso" };

        var allFiles = Directory.EnumerateFiles(rootPath, "*.*", searchOption)
            .Where(file => psxExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .ToList();
        var totalFiles = allFiles.Count;
        long totalBytes = 0;
        var fileSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in allFiles)
        {
            var length = new FileInfo(file).Length;
            fileSizes[file] = length;
            totalBytes += length;
        }

        // Hash files and group by hash
        var filesByHash = new Dictionary<string, List<(string filePath, long fileSize, PsxDiscInfo discInfo)>>();
        var stopwatch = Stopwatch.StartNew();
        int processedFiles = 0;
        long processedBytes = 0;

        foreach (var file in allFiles)
        {
            var discInfo = _parser.Parse(file);
            var fileSize = fileSizes[file];

            // Skip audio track BIN files - they're not primary disc images
            if (discInfo.IsAudioTrack)
            {
                processedFiles++;
                processedBytes += fileSize;
                progress?.Report(new DuplicateScanProgress(processedFiles, totalFiles, processedBytes, totalBytes, file, stopwatch.Elapsed));
                continue;
            }

            // For CUE files, we might want to hash the referenced BIN instead
            // For now, just hash the file itself
            var hash = ComputeHash(file, hashAlgorithm);

            if (!filesByHash.ContainsKey(hash))
            {
                filesByHash[hash] = new List<(string, long, PsxDiscInfo)>();
            }

            filesByHash[hash].Add((file, fileSize, discInfo));

            processedFiles++;
            processedBytes += fileSize;
            progress?.Report(new DuplicateScanProgress(processedFiles, totalFiles, processedBytes, totalBytes, file, stopwatch.Elapsed));
        }

        // Filter to only groups with duplicates
        var duplicateGroups = new List<DuplicateGroup>();
        
        foreach (var (hash, files) in filesByHash.Where(kvp => kvp.Value.Count > 1))
        {
            var firstDisc = files.First().discInfo;
            
            duplicateGroups.Add(new DuplicateGroup
            {
                Hash = hash,
                Files = files.Select(f => new DuplicateFileInfo
                {
                    FilePath = f.filePath,
                    FileSize = f.fileSize,
                    DiscInfo = f.discInfo
                }).ToList(),
                Title = firstDisc.Title,
                Serial = firstDisc.Serial,
                DiscNumber = firstDisc.DiscNumber
            });
        }
        
        return duplicateGroups.OrderBy(g => g.Title).ThenBy(g => g.DiscNumber).ToList();
    }
    
    /// <summary>
    /// Compute hash for a file
    /// </summary>
    private string ComputeHash(string filePath, string algorithm)
    {
        using var stream = File.OpenRead(filePath);
        
        byte[] hashBytes = algorithm.ToUpperInvariant() switch
        {
            "MD5" => MD5.HashData(stream),
            "SHA1" => SHA1.HashData(stream),
            _ => SHA1.HashData(stream) // Default to SHA1
        };
        
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}
