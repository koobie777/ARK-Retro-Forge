using ARK.Core.Scanning;

namespace ARK.Core.Verification;

/// <summary>
/// Decides whether a file is still being written, using the cheap signals before any hashing.
/// </summary>
/// <remarks>
/// The dangerous case is not the obviously-partial transfer — it is the one stalled at 99.9%. It
/// looks finished, it will sit that way indefinitely, and its few incomplete files are
/// indistinguishable from complete ones by name and size. Nothing here settles that; only the
/// hash does. These signals exist to avoid reporting an in-flight file as corrupt, not to certify
/// that anything is complete.
/// </remarks>
public sealed class InProgressDetector
{
    private readonly HashSet<string> _extensions;
    private readonly string[] _declaredDirectories;
    private readonly TimeSpan _recentWindow;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>Creates a detector.</summary>
    /// <param name="incompleteExtensions">Client extensions for a transfer in flight.</param>
    /// <param name="declaredDirectories">Directories the user declared as incomplete-download locations.</param>
    /// <param name="recentWindow">How recent a write has to be to count as activity.</param>
    /// <param name="now">Clock, injectable so the recent-write window is testable.</param>
    public InProgressDetector(
        IEnumerable<string>? incompleteExtensions = null,
        IEnumerable<string>? declaredDirectories = null,
        TimeSpan? recentWindow = null,
        Func<DateTimeOffset>? now = null)
    {
        _extensions = new HashSet<string>(
            incompleteExtensions ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        _declaredDirectories = (declaredDirectories ?? Array.Empty<string>())
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => directory.TrimEnd('\\', '/'))
            .ToArray();
        _recentWindow = recentWindow ?? TimeSpan.FromMinutes(5);
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Evaluates the cheap signals, in cost order. Returns <see cref="InProgressSignal.None"/>
    /// when none fires — which is not a statement that the file is complete.
    /// </summary>
    public InProgressSignal Inspect(FileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);

        // Free, but only present when the client is configured to append one.
        if (_extensions.Contains(file.Extension))
        {
            return InProgressSignal.IncompleteExtension;
        }

        foreach (var directory in _declaredDirectories)
        {
            if (file.FullPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                file.FullPath.StartsWith(directory + '/', StringComparison.OrdinalIgnoreCase))
            {
                return InProgressSignal.DeclaredDirectory;
            }
        }

        if (file.ModifiedUtc != default && _now() - file.ModifiedUtc < _recentWindow)
        {
            return InProgressSignal.RecentWrite;
        }

        return InProgressSignal.None;
    }

    /// <summary>Human-readable statement of a signal, for the report.</summary>
    public static string Explain(InProgressSignal signal) => signal switch
    {
        InProgressSignal.IncompleteExtension => "incomplete-download extension",
        InProgressSignal.DeclaredDirectory => "inside a declared incomplete-download directory",
        InProgressSignal.RecentWrite => "written to recently",
        InProgressSignal.ChangedDuringRead => "changed while being read",
        _ => "not in progress",
    };
}
