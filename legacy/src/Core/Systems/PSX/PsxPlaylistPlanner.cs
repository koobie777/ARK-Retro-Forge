namespace ARK.Core.Systems.PSX;

/// <summary>
/// Represents the type of playlist operation
/// </summary>
public enum PlaylistOperationType
{
    /// <summary>
    /// Create a new playlist
    /// </summary>
    Create,
    
    /// <summary>
    /// Update an existing playlist
    /// </summary>
    Update,
    
    /// <summary>
    /// No operation needed
    /// </summary>
    None
}

/// <summary>
/// Represents a PSX playlist operation (.m3u creation or update)
/// </summary>
public record PsxPlaylistOperation
{
    public required string PlaylistPath { get; init; }
    public required string Title { get; init; }
    public required string Region { get; init; }
    public required List<string> DiscFilenames { get; init; }
    public required PlaylistOperationType OperationType { get; init; }
    public string? ExistingContent { get; init; }
}

/// <summary>
/// Plans .m3u playlist operations for multi-disc PSX titles
/// </summary>
public class PsxPlaylistPlanner
{
    private readonly PsxNameParser _parser;
    
    public PsxPlaylistPlanner(PsxNameParser? parser = null)
    {
        _parser = parser ?? new PsxNameParser();
    }
    
    /// <summary>
    /// Plan playlist operations for a directory
    /// </summary>
    /// <param name="rootPath">Root directory to scan</param>
    /// <param name="recursive">Whether to scan recursively</param>
    /// <param name="preferredExtension">Preferred file extension for playlist entries (.cue, .chd, etc.)</param>
    /// <param name="createNew">Whether to create new playlists for multi-disc titles</param>
    /// <param name="updateExisting">Whether to update existing playlists</param>
    public List<PsxPlaylistOperation> PlanPlaylists(
        string rootPath, 
        bool recursive = false,
        string? preferredExtension = null,
        bool createNew = true,
        bool updateExisting = true)
    {
        var operations = new List<PsxPlaylistOperation>();
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        
        // Group all disc images by directory
        var directoriesWithDiscs = new Dictionary<string, List<PsxDiscInfo>>();
        
        var psxExtensions = new[] { ".bin", ".cue", ".chd", ".pbp", ".iso" };
        
        foreach (var ext in psxExtensions)
        {
            var files = Directory.GetFiles(rootPath, $"*{ext}", searchOption);
            
            foreach (var file in files)
            {
                var discInfo = _parser.Parse(file);
                
                // Skip audio track BIN files - they shouldn't be in playlists
                if (discInfo.IsAudioTrack)
                {
                    continue;
                }
                
                var directory = Path.GetDirectoryName(file) ?? string.Empty;
                
                if (!directoriesWithDiscs.ContainsKey(directory))
                {
                    directoriesWithDiscs[directory] = new List<PsxDiscInfo>();
                }
                
                directoriesWithDiscs[directory].Add(discInfo);
            }
        }
        
        // Process each directory
        foreach (var (directory, discs) in directoriesWithDiscs)
        {
            // Group discs by title and region (multi-disc sets)
            var multiDiscSets = discs
                .Where(d => !string.IsNullOrWhiteSpace(d.Title))
                .GroupBy(d => (d.Title, d.Region))
                .Where(g => g.Count() > 1 || g.Any(d => d.IsMultiDisc))
                .ToList();
            
            foreach (var set in multiDiscSets)
            {
                var title = set.Key.Title!;
                var region = set.Key.Region;
                var playlistName = !string.IsNullOrWhiteSpace(region) 
                    ? $"{title} ({region}).m3u" 
                    : $"{title}.m3u";
                var playlistPath = Path.Combine(directory, playlistName);
                
                // Determine which files to include in playlist
                var discList = set.OrderBy(d => d.DiscNumber ?? 1).ToList();
                
                // Select the preferred file for each disc
                var discFilenames = new List<string>();
                
                // If preferredExtension is specified, use that; otherwise prefer CHD > CUE > BIN
                var extensionPriority = preferredExtension != null 
                    ? new[] { preferredExtension, ".chd", ".cue", ".bin" }
                    : new[] { ".chd", ".cue", ".bin" };
                
                foreach (var discNumber in discList.Select(d => d.DiscNumber ?? 1).Distinct().OrderBy(n => n))
                {
                    var discsForNumber = discList.Where(d => (d.DiscNumber ?? 1) == discNumber).ToList();
                    
                    // Pick the best file based on extension priority
                    var selectedDisc = discsForNumber
                        .OrderBy(d => Array.IndexOf(extensionPriority, d.Extension?.ToLowerInvariant() ?? "") switch
                        {
                            -1 => int.MaxValue,
                            var idx => idx
                        })
                        .FirstOrDefault();
                    
                    if (selectedDisc != null)
                    {
                        discFilenames.Add(Path.GetFileName(selectedDisc.FilePath));
                    }
                }
                
                // Skip single-disc playlists unless explicitly requested or it's a known multi-disc title with missing discs
                // User requested: "play list creation is for mulit disc games not singles too"
                if (discFilenames.Count < 2)
                {
                    continue;
                }

                // Check if playlist exists
                var existingContent = File.Exists(playlistPath) ? File.ReadAllText(playlistPath) : null;
                
                PlaylistOperationType operationType;
                
                if (existingContent == null)
                {
                    operationType = createNew ? PlaylistOperationType.Create : PlaylistOperationType.None;
                }
                else
                {
                    // Check if update is needed
                    var currentLines = existingContent.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !l.StartsWith("#"))
                        .ToList();
                    
                    var needsUpdate = !currentLines.SequenceEqual(discFilenames);
                    
                    operationType = needsUpdate && updateExisting 
                        ? PlaylistOperationType.Update 
                        : PlaylistOperationType.None;
                }
                
                if (operationType != PlaylistOperationType.None)
                {
                    operations.Add(new PsxPlaylistOperation
                    {
                        PlaylistPath = playlistPath,
                        Title = title,
                        Region = region ?? string.Empty,
                        DiscFilenames = discFilenames,
                        OperationType = operationType,
                        ExistingContent = existingContent
                    });
                }
            }
        }
        
        return operations;
    }
    
    /// <summary>
    /// Apply a playlist operation
    /// </summary>
    public void ApplyOperation(PsxPlaylistOperation operation, bool createBackup = true)
    {
        if (operation.OperationType == PlaylistOperationType.None)
        {
            return;
        }
        
        // Create backup if updating and backup requested
        if (operation.OperationType == PlaylistOperationType.Update && 
            createBackup && 
            File.Exists(operation.PlaylistPath))
        {
            var backupPath = operation.PlaylistPath + ".bak";
            File.Copy(operation.PlaylistPath, backupPath, overwrite: true);
        }
        
        // Write new playlist content
        var content = string.Join(Environment.NewLine, operation.DiscFilenames);
        File.WriteAllText(operation.PlaylistPath, content);
    }

    /// <summary>
    /// Plan playlist operations based on planned rename operations
    /// </summary>
    public List<PsxPlaylistOperation> PlanPlaylistsFromRenames(
        IEnumerable<PsxRenameOperation> renameOps,
        bool createNew = true,
        bool updateExisting = true)
    {
        var operations = new List<PsxPlaylistOperation>();
        
        // Group by destination directory
        var byDirectory = renameOps
            .GroupBy(op => Path.GetDirectoryName(op.DestinationPath) ?? string.Empty);

        foreach (var dirGroup in byDirectory)
        {
            var directory = dirGroup.Key;
            
            // Group by Title/Region
            var sets = dirGroup
                .Where(op => !op.DiscInfo.IsAudioTrack) // Skip audio tracks
                .GroupBy(op => (Title: op.DiscInfo.Title, Region: op.DiscInfo.Region))
                .Where(g => !string.IsNullOrWhiteSpace(g.Key.Title));

            foreach (var set in sets)
            {
                var title = set.Key.Title!;
                var region = set.Key.Region;
                
                // Skip single-disc sets
                if (set.Count() < 2)
                {
                    continue;
                }

                var playlistName = !string.IsNullOrWhiteSpace(region) 
                    ? $"{title} ({region}).m3u" 
                    : $"{title}.m3u";
                
                var playlistPath = Path.Combine(directory, playlistName);
                
                // Group by disc number to handle multiple files per disc (e.g. CUE + BINs)
                var discGroups = set
                    .GroupBy(op => op.DiscInfo.DiscNumber ?? 1)
                    .OrderBy(g => g.Key);

                var discFilenames = new List<string>();
                var extensionPriority = new[] { ".chd", ".cue", ".bin", ".iso", ".pbp" };

                foreach (var discGroup in discGroups)
                {
                    // Pick the best file for this disc
                    var selectedOp = discGroup
                        .OrderBy(op => Array.IndexOf(extensionPriority, Path.GetExtension(op.DestinationPath).ToLowerInvariant()) switch
                        {
                            -1 => int.MaxValue,
                            var idx => idx
                        })
                        .FirstOrDefault();

                    if (selectedOp != null)
                    {
                        discFilenames.Add(Path.GetFileName(selectedOp.DestinationPath));
                    }
                }

                // Skip single-disc sets (unless user wants single disc playlists, but usually not for rename side-effect)
                if (discFilenames.Count < 2)
                {
                    continue;
                }

                // Check existing
                var existingContent = File.Exists(playlistPath) ? File.ReadAllText(playlistPath) : null;
                PlaylistOperationType operationType;

                if (existingContent == null)
                {
                    operationType = createNew ? PlaylistOperationType.Create : PlaylistOperationType.None;
                }
                else
                {
                    // Check if update is needed
                    var currentLines = existingContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !l.StartsWith("#"))
                        .ToList();
                    
                    var needsUpdate = !currentLines.SequenceEqual(discFilenames);
                    
                    operationType = needsUpdate && updateExisting 
                        ? PlaylistOperationType.Update 
                        : PlaylistOperationType.None;
                }

                if (operationType != PlaylistOperationType.None)
                {
                    operations.Add(new PsxPlaylistOperation
                    {
                        PlaylistPath = playlistPath,
                        Title = title,
                        Region = region ?? string.Empty,
                        DiscFilenames = discFilenames,
                        OperationType = operationType,
                        ExistingContent = existingContent
                    });
                }
            }
        }

        return operations;
    }
}
