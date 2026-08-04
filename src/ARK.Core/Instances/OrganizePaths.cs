namespace ARK.Core.Instances;

/// <summary>
/// Resolves where organized units go, and is the sole authority for that layout.
/// </summary>
/// <remarks>
/// A path-resolution type beside <see cref="InstancePaths"/>, <see cref="QuarantinePaths"/> and
/// <see cref="SortPaths"/>, for the same reason: path composition stays centralized so "nothing
/// composes paths outside the resolver" remains enforceable.
/// </remarks>
public sealed class OrganizePaths
{
    private readonly string _root;

    /// <summary>Creates a resolver rooted at <paramref name="root"/>.</summary>
    public OrganizePaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
        Volume = Path.GetPathRoot(Path.GetFullPath(root));
    }

    /// <summary>Volume the root sits on.</summary>
    public string? Volume { get; }

    /// <summary>True when <paramref name="path"/> is on the same volume as the root.</summary>
    public bool IsSameVolume(string path) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(path)), Volume, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where a unit belongs under <paramref name="folder"/>.
    /// </summary>
    /// <remarks>
    /// The default folder is the DAT name, which real collections already use — the reference
    /// drive's directories are byte-identical to their DAT names.
    /// </remarks>
    public string DestinationFor(string folder, string fileName) =>
        Path.Combine(_root, Sanitize(folder), fileName);

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

    // A DAT name is user-facing data, not a path, so anything the filesystem rejects is replaced
    // rather than allowed to compose something that escapes the root.
    private static string Sanitize(string folder)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(folder.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
    }
}
