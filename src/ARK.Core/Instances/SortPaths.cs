namespace ARK.Core.Instances;

/// <summary>
/// Resolves where sorted variants go, and is the sole authority for that layout.
/// </summary>
/// <remarks>
/// A path-resolution type beside <see cref="InstancePaths"/> and <see cref="QuarantinePaths"/>,
/// for the same reason: path composition stays centralized so "nothing composes paths outside the
/// resolver" remains enforceable.
/// </remarks>
public sealed class SortPaths
{
    private readonly string _root;

    /// <summary>Creates a resolver for a subfolder beneath <paramref name="root"/>.</summary>
    public SortPaths(string root, string subfolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(subfolder);

        _root = root;
        SubfolderRoot = Path.Combine(root, subfolder);
        Volume = Path.GetPathRoot(Path.GetFullPath(root));
    }

    /// <summary>The subfolder variants are sorted into.</summary>
    public string SubfolderRoot { get; }

    /// <summary>Volume the subfolder sits on.</summary>
    public string? Volume { get; }

    /// <summary>True when <paramref name="path"/> is on the same volume as the subfolder.</summary>
    public bool IsSameVolume(string path) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(path)), Volume, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where a unit goes, keeping its path relative to the root so two files sharing a name cannot
    /// collide and the original layout stays legible.
    /// </summary>
    public string DestinationFor(string originalPath)
    {
        var relative = Path.GetRelativePath(_root, originalPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            relative = Path.GetFileName(originalPath);
        }

        return Path.Combine(SubfolderRoot, relative);
    }

    /// <summary>
    /// Directories that must be created for <paramref name="destination"/> to exist, outermost
    /// first, stopping at the root and skipping anything already present.
    /// </summary>
    public IReadOnlyList<string> DirectoriesToCreate(string destination)
    {
        var chain = new List<string>();
        var rootFull = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar);

        for (var current = Path.GetDirectoryName(destination); current is not null; current = Path.GetDirectoryName(current))
        {
            var full = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar);
            if (full.Equals(rootFull, StringComparison.OrdinalIgnoreCase) || Directory.Exists(current))
            {
                break;
            }

            chain.Add(current);
        }

        chain.Reverse();
        return chain;
    }
}
