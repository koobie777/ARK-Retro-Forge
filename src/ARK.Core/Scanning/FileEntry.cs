namespace ARK.Core.Scanning;

/// <summary>
/// One file as seen by a directory listing: metadata only, never contents.
/// </summary>
/// <remarks>
/// Directory profiling decides what a directory is from listings alone — no file is opened to
/// classify it. This record is what "a listing" means, and it is deliberately the entire surface
/// the profiler gets, so opening a file to reach a verdict is not possible by accident.
/// </remarks>
/// <param name="FullPath">Absolute path.</param>
/// <param name="Name">File name including extension.</param>
/// <param name="Extension">Extension including the leading dot, lower-cased; empty when absent.</param>
/// <param name="Size">Length in bytes.</param>
/// <param name="ModifiedUtc">Last write time.</param>
public sealed record FileEntry(string FullPath, string Name, string Extension, long Size, DateTimeOffset ModifiedUtc)
{
    /// <summary>File name with its extension removed — the parseable name.</summary>
    public string NameWithoutExtension =>
        Extension.Length > 0 && Name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            ? Name[..^Extension.Length]
            : Name;
}
