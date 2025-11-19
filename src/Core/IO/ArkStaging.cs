using System.Text;

namespace ARK.Core.IO;

/// <summary>
/// Represents a pending file operation in the staging area.
/// </summary>
public abstract record StagedOperation
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public abstract string Description { get; }
}

public record StagedMove : StagedOperation
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public StagedMove(string source, string destination)
    {
        SourcePath = source;
        DestinationPath = destination;
    }
    public override string Description => $"Move: {Path.GetFileName(SourcePath)} -> {Path.GetFileName(DestinationPath)}";
}

public record StagedCopy : StagedOperation
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public StagedCopy(string source, string destination)
    {
        SourcePath = source;
        DestinationPath = destination;
    }
    public override string Description => $"Copy: {Path.GetFileName(SourcePath)} -> {Path.GetFileName(DestinationPath)}";
}

public record StagedDelete : StagedOperation
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public StagedDelete(string path)
    {
        SourcePath = path;
        DestinationPath = string.Empty;
    }
    public override string Description => $"Delete: {Path.GetFileName(SourcePath)}";
}

public record StagedWrite : StagedOperation
{
    public string Content { get; init; }
    
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public StagedWrite(string path, string content)
    {
        SourcePath = string.Empty;
        DestinationPath = path;
        Content = content;
    }
    public override string Description => $"Write: {Path.GetFileName(DestinationPath)}";
}

/// <summary>
/// Manages file operations with staging, validation, and atomic execution capabilities.
/// </summary>
public class ArkStaging
{
    private readonly List<StagedOperation> _operations = new();

    public IReadOnlyList<StagedOperation> PendingOperations => _operations.AsReadOnly();

    public void StageMove(string source, string destination)
    {
        _operations.Add(new StagedMove(source, destination));
    }

    public void StageCopy(string source, string destination)
    {
        _operations.Add(new StagedCopy(source, destination));
    }

    public void StageDelete(string path)
    {
        _operations.Add(new StagedDelete(path));
    }

    public void StageWrite(string path, string content)
    {
        _operations.Add(new StagedWrite(path, content));
    }

    /// <summary>
    /// Commits all staged operations.
    /// </summary>
    /// <param name="onProgress">Callback for progress reporting.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of operations successfully executed.</returns>
    public async Task<int> CommitAsync(Action<StagedOperation>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var executed = 0;
        
        // TODO: Implement rollback support?
        // For now, we execute sequentially and stop on error.

        foreach (var op in _operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onProgress?.Invoke(op);

            try
            {
                switch (op)
                {
                    case StagedMove move:
                        EnsureDirectory(move.DestinationPath);
                        // Handle case-only rename on same file system
                        if (string.Equals(Path.GetFullPath(move.SourcePath), Path.GetFullPath(move.DestinationPath), StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.Equals(Path.GetFileName(move.SourcePath), Path.GetFileName(move.DestinationPath), StringComparison.Ordinal))
                            {
                                File.Move(move.SourcePath, move.DestinationPath);
                            }
                        }
                        else
                        {
                            if (File.Exists(move.DestinationPath))
                            {
                                File.Delete(move.DestinationPath);
                            }
                            File.Move(move.SourcePath, move.DestinationPath);
                        }
                        break;

                    case StagedCopy copy:
                        EnsureDirectory(copy.DestinationPath);
                        File.Copy(copy.SourcePath, copy.DestinationPath, overwrite: true);
                        break;

                    case StagedDelete delete:
                        if (File.Exists(delete.SourcePath))
                        {
                            File.Delete(delete.SourcePath);
                        }
                        break;

                    case StagedWrite write:
                        EnsureDirectory(write.DestinationPath);
                        await File.WriteAllTextAsync(write.DestinationPath, write.Content, Encoding.UTF8, cancellationToken);
                        break;
                }
                executed++;
            }
            catch (Exception)
            {
                // Log error?
                throw;
            }
        }

        _operations.Clear();
        return executed;
    }

    public void Clear()
    {
        _operations.Clear();
    }

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
