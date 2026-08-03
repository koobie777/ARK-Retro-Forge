namespace ARK.Core.Instances;

/// <summary>
/// Resolves where quarantined units go, and is the sole authority for quarantine layout.
/// </summary>
/// <remarks>
/// <para>
/// A path-resolution type, sitting beside <see cref="InstancePaths"/> for the same reason: path
/// composition is centralized so "nothing composes paths outside the resolver" stays enforceable.
/// </para>
/// <para>
/// Quarantine lives under the deduplicated root rather than under the instance, because
/// <b>a move across volumes is a copy</b>. Instant becomes hours, and hundreds of gigabytes get
/// duplicated before a single byte is freed. Keeping it beneath the root guarantees the same
/// volume whenever the root itself is one volume.
/// </para>
/// <para>
/// Resolution is pure. Creating any of these directories is a filesystem mutation and belongs to
/// the <c>Executor</c> alone.
/// </para>
/// </remarks>
public sealed class QuarantinePaths
{
    /// <summary>Directory name used for quarantine beneath the deduplicated root.</summary>
    public const string DirectoryName = ".ark-quarantine";

    /// <summary>Manifest file name inside a session's quarantine directory.</summary>
    public const string ManifestFileName = "manifest.json";

    private readonly string _root;

    /// <summary>Creates a resolver for one session's quarantine beneath <paramref name="root"/>.</summary>
    public QuarantinePaths(string root, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        _root = root;
        SessionRoot = Path.Combine(root, DirectoryName, sessionId);
        ManifestPath = Path.Combine(SessionRoot, ManifestFileName);
        Volume = Path.GetPathRoot(Path.GetFullPath(root));
    }

    /// <summary>This session's quarantine directory.</summary>
    public string SessionRoot { get; }

    /// <summary>Where this session's manifest is written.</summary>
    public string ManifestPath { get; }

    /// <summary>Volume the quarantine root sits on.</summary>
    public string? Volume { get; }

    /// <summary>True when <paramref name="path"/> is on the same volume as the quarantine root.</summary>
    public bool IsSameVolume(string path) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(path)), Volume, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where a unit goes, keeping its path relative to the root so two files sharing a name cannot
    /// collide and the original layout stays legible.
    /// </summary>
    public string DestinationFor(string originalPath)
    {
        var relative = Path.GetRelativePath(_root, originalPath);

        // A path outside the root would climb out of the quarantine directory; fall back to the
        // bare file name rather than composing something that escapes it.
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            relative = Path.GetFileName(originalPath);
        }

        return Path.Combine(SessionRoot, relative);
    }

    /// <summary>
    /// Directories that must be created for <paramref name="destination"/> to exist, outermost
    /// first, stopping at the root and skipping anything already present.
    /// </summary>
    /// <remarks>
    /// Every level is listed separately because <c>CreateDirectory</c> builds the whole chain in
    /// one call. Journaling only the leaf would leave undo unable to remove the levels above it,
    /// and "restores the set exactly" would quietly stop being true.
    /// </remarks>
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
