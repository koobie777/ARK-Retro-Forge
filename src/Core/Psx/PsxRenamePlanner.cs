namespace ARK.Core.Psx;

/// <summary>
/// Represents a planned file system operation.
/// </summary>
public class FileOperation
{
    public FileOperationType Type { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string? TargetPath { get; set; }
    public string Description { get; set; } = string.Empty;
}

public enum FileOperationType
{
    Rename,
    Move,
    DeleteFolder
}

/// <summary>
/// Plans rename operations for PSX titles.
/// </summary>
public class PsxRenamePlanner
{
    private readonly PsxNamingService _namingService = new();

    /// <summary>
    /// Creates a plan for renaming PSX files.
    /// </summary>
    public List<FileOperation> PlanRenames(List<PsxTitleGroup> groups, bool flattenMultidisc)
    {
        var operations = new List<FileOperation>();

        foreach (var group in groups)
        {
            if (group.IsMultiDisc)
            {
                PlanMultiDiscRenames(group, flattenMultidisc, operations);
            }
            else
            {
                PlanSingleDiscRename(group, operations);
            }
        }

        return operations;
    }

    private void PlanSingleDiscRename(PsxTitleGroup group, List<FileOperation> operations)
    {
        var disc = group.Discs.FirstOrDefault();
        if (disc == null)
        {
            return;
        }

        var newBaseName = _namingService.GenerateSingleDiscName(group);

        // Rename CUE file
        if (!string.IsNullOrEmpty(disc.CuePath))
        {
            var newCuePath = Path.Combine(
                Path.GetDirectoryName(disc.CuePath) ?? string.Empty,
                newBaseName + ".cue");

            if (!disc.CuePath.Equals(newCuePath, StringComparison.OrdinalIgnoreCase))
            {
                operations.Add(new FileOperation
                {
                    Type = FileOperationType.Rename,
                    SourcePath = disc.CuePath,
                    TargetPath = newCuePath,
                    Description = $"Rename CUE: {Path.GetFileName(disc.CuePath)} -> {Path.GetFileName(newCuePath)}"
                });
            }
        }

        // Rename BIN files
        for (int i = 0; i < disc.BinPaths.Count; i++)
        {
            var binPath = disc.BinPaths[i];
            var binExt = disc.BinPaths.Count == 1 ? ".bin" : $" (Track {i + 1:D2}).bin";
            var newBinPath = Path.Combine(
                Path.GetDirectoryName(binPath) ?? string.Empty,
                newBaseName + binExt);

            if (!binPath.Equals(newBinPath, StringComparison.OrdinalIgnoreCase))
            {
                operations.Add(new FileOperation
                {
                    Type = FileOperationType.Rename,
                    SourcePath = binPath,
                    TargetPath = newBinPath,
                    Description = $"Rename BIN: {Path.GetFileName(binPath)} -> {Path.GetFileName(newBinPath)}"
                });
            }
        }

        // Rename CHD file
        if (!string.IsNullOrEmpty(disc.ChdPath))
        {
            var newChdPath = Path.Combine(
                Path.GetDirectoryName(disc.ChdPath) ?? string.Empty,
                newBaseName + ".chd");

            if (!disc.ChdPath.Equals(newChdPath, StringComparison.OrdinalIgnoreCase))
            {
                operations.Add(new FileOperation
                {
                    Type = FileOperationType.Rename,
                    SourcePath = disc.ChdPath,
                    TargetPath = newChdPath,
                    Description = $"Rename CHD: {Path.GetFileName(disc.ChdPath)} -> {Path.GetFileName(newChdPath)}"
                });
            }
        }
    }

    private void PlanMultiDiscRenames(PsxTitleGroup group, bool flatten, List<FileOperation> operations)
    {
        var gameDir = group.GameDirectory;
        var parentDir = Path.GetDirectoryName(gameDir) ?? gameDir;

        // Rename each disc
        foreach (var disc in group.Discs)
        {
            var newBaseName = _namingService.GenerateMultiDiscName(group, disc);
            var targetDir = flatten ? parentDir : gameDir;

            // Handle CUE file
            if (!string.IsNullOrEmpty(disc.CuePath))
            {
                var newCuePath = Path.Combine(targetDir, newBaseName + ".cue");

                if (!disc.CuePath.Equals(newCuePath, StringComparison.OrdinalIgnoreCase))
                {
                    var opType = flatten && Path.GetDirectoryName(disc.CuePath) != targetDir
                        ? FileOperationType.Move
                        : FileOperationType.Rename;

                    operations.Add(new FileOperation
                    {
                        Type = opType,
                        SourcePath = disc.CuePath,
                        TargetPath = newCuePath,
                        Description = $"{opType} CUE: {Path.GetFileName(disc.CuePath)} -> {newBaseName}.cue"
                    });
                }
            }

            // Handle BIN files
            for (int i = 0; i < disc.BinPaths.Count; i++)
            {
                var binPath = disc.BinPaths[i];
                var binExt = disc.BinPaths.Count == 1 ? ".bin" : $" (Track {i + 1:D2}).bin";
                var newBinPath = Path.Combine(targetDir, newBaseName + binExt);

                if (!binPath.Equals(newBinPath, StringComparison.OrdinalIgnoreCase))
                {
                    var opType = flatten && Path.GetDirectoryName(binPath) != targetDir
                        ? FileOperationType.Move
                        : FileOperationType.Rename;

                    operations.Add(new FileOperation
                    {
                        Type = opType,
                        SourcePath = binPath,
                        TargetPath = newBinPath,
                        Description = $"{opType} BIN: {Path.GetFileName(binPath)} -> {Path.GetFileName(newBinPath)}"
                    });
                }
            }

            // Handle CHD file
            if (!string.IsNullOrEmpty(disc.ChdPath))
            {
                var newChdPath = Path.Combine(targetDir, newBaseName + ".chd");

                if (!disc.ChdPath.Equals(newChdPath, StringComparison.OrdinalIgnoreCase))
                {
                    var opType = flatten && Path.GetDirectoryName(disc.ChdPath) != targetDir
                        ? FileOperationType.Move
                        : FileOperationType.Rename;

                    operations.Add(new FileOperation
                    {
                        Type = opType,
                        SourcePath = disc.ChdPath,
                        TargetPath = newChdPath,
                        Description = $"{opType} CHD: {Path.GetFileName(disc.ChdPath)} -> {newBaseName}.chd"
                    });
                }
            }
        }

        // Handle playlist
        if (!string.IsNullOrEmpty(group.PlaylistPath))
        {
            var playlistName = _namingService.GeneratePlaylistName(group);
            var targetDir = flatten ? parentDir : gameDir;
            var newPlaylistPath = Path.Combine(targetDir, playlistName + ".m3u");

            if (!group.PlaylistPath.Equals(newPlaylistPath, StringComparison.OrdinalIgnoreCase))
            {
                var opType = flatten && Path.GetDirectoryName(group.PlaylistPath) != targetDir
                    ? FileOperationType.Move
                    : FileOperationType.Rename;

                operations.Add(new FileOperation
                {
                    Type = opType,
                    SourcePath = group.PlaylistPath,
                    TargetPath = newPlaylistPath,
                    Description = $"{opType} Playlist: {Path.GetFileName(group.PlaylistPath)} -> {playlistName}.m3u"
                });
            }
        }

        // If flattening, mark the game directory for deletion (if it becomes empty)
        if (flatten && Directory.Exists(gameDir))
        {
            operations.Add(new FileOperation
            {
                Type = FileOperationType.DeleteFolder,
                SourcePath = gameDir,
                Description = $"Delete empty folder: {Path.GetFileName(gameDir)}"
            });
        }
    }
}
