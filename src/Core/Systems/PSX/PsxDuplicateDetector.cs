using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

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
    /// <summary>How this group was matched (Hash or Metadata).</summary>
    public string MatchType { get; init; } = "Hash";
    public string? Reason { get; init; }
    public long TotalBytes => Files.Sum(f => f.FileSize);
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
    /// <param name="hashCachePath">Optional cache file path to store hashes keyed by path/size/timestamp.</param>
    public List<DuplicateGroup> ScanForDuplicates(
        string rootPath, 
        bool recursive = false,
        string hashAlgorithm = "SHA1",
        IProgress<DuplicateScanProgress>? progress = null,
        string? hashCachePath = null)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var psxExtensions = new[] { ".bin", ".cue", ".chd", ".pbp", ".iso" };

        var allFiles = Directory.EnumerateFiles(rootPath, "*.*", searchOption)
            .Where(file => psxExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var cache = LoadHashCache(hashCachePath);
        var cacheDirty = false;

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
        var allEntries = new List<(string filePath, long fileSize, PsxDiscInfo discInfo, string hash)>();
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

            // Try cached hash first
            var cacheKey = file;
            var fileTimestamp = File.GetLastWriteTimeUtc(file);
            string hash;
            if (cache.TryGetValue(cacheKey, out var cached) &&
                cached.Algorithm.Equals(hashAlgorithm, StringComparison.OrdinalIgnoreCase) &&
                cached.FileSize == fileSize &&
                cached.LastWriteUtc == fileTimestamp)
            {
                hash = cached.Hash;
            }
            else
            {
                // Hash now and cache
                hash = ComputeHash(file, hashAlgorithm);
                cache[cacheKey] = new HashCacheEntry
                {
                    Hash = hash,
                    FileSize = fileSize,
                    LastWriteUtc = fileTimestamp,
                    Algorithm = hashAlgorithm
                };
                cacheDirty = true;
            }

            if (!filesByHash.ContainsKey(hash))
            {
                filesByHash[hash] = new List<(string, long, PsxDiscInfo)>();
            }

            filesByHash[hash].Add((file, fileSize, discInfo));
            allEntries.Add((file, fileSize, discInfo, hash));

            processedFiles++;
            processedBytes += fileSize;
            progress?.Report(new DuplicateScanProgress(processedFiles, totalFiles, processedBytes, totalBytes, file, stopwatch.Elapsed));
        }

        if (cacheDirty)
        {
            SaveHashCache(hashCachePath, cache);
        }

        // Filter to only groups with duplicates
        var duplicateGroups = new List<DuplicateGroup>();
        
        var alreadyGrouped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                DiscNumber = firstDisc.DiscNumber,
                MatchType = "Hash",
                Reason = "Identical hash"
            });

            foreach (var f in files)
            {
                alreadyGrouped.Add(f.filePath);
            }
        }

        // Secondary pass: metadata-based duplicates (same serial/title+region and disc number) even if hashes differ
        var metadataGroups = allEntries
            .Where(entry => !alreadyGrouped.Contains(entry.filePath))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.discInfo.Serial) || (!string.IsNullOrWhiteSpace(entry.discInfo.Title) && !string.IsNullOrWhiteSpace(entry.discInfo.Region)))
            .Where(entry => entry.discInfo.DiscNumber.HasValue)
            .GroupBy(entry =>
            {
                if (!string.IsNullOrWhiteSpace(entry.discInfo.Serial))
                {
                    return $"SERIAL:{entry.discInfo.Serial!.Trim().ToUpperInvariant()}|DISC:{entry.discInfo.DiscNumber!.Value}";
                }

                return $"TITLE:{entry.discInfo.Title!.Trim().ToUpperInvariant()}|REGION:{entry.discInfo.Region!.Trim().ToUpperInvariant()}|DISC:{entry.discInfo.DiscNumber!.Value}";
            })
            .Where(g => g.Count() > 1);

        foreach (var group in metadataGroups)
        {
            var files = group.ToList();
            var firstDisc = files.First().discInfo;

            duplicateGroups.Add(new DuplicateGroup
            {
                Hash = $"metadata:{group.Key}",
                Files = files.Select(f => new DuplicateFileInfo
                {
                    FilePath = f.filePath,
                    FileSize = f.fileSize,
                    DiscInfo = f.discInfo
                }).ToList(),
                Title = firstDisc.Title,
                Serial = firstDisc.Serial,
                DiscNumber = firstDisc.DiscNumber,
                MatchType = "Metadata",
                Reason = "Matching serial/title+region and disc number"
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

    private static Dictionary<string, HashCacheEntry> LoadHashCache(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new Dictionary<string, HashCacheEntry>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(path);
            var cache = JsonSerializer.Deserialize<Dictionary<string, HashCacheEntry>>(json);
            return cache ?? new Dictionary<string, HashCacheEntry>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, HashCacheEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveHashCache(string? path, Dictionary<string, HashCacheEntry> cache)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best-effort caching; ignore persistence errors
        }
    }

    private sealed class HashCacheEntry
    {
        public required string Hash { get; init; }
        public required long FileSize { get; init; }
        public required DateTime LastWriteUtc { get; init; }
        public required string Algorithm { get; init; }
    }
}
