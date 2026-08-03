using ARK.Core.Units;

namespace ARK.Tests;

/// <summary>
/// One configurable archive inspector for every test that needs one, so adding a member to
/// <see cref="IArchiveInspector"/> does not mean editing four near-identical private doubles.
/// </summary>
internal sealed class FakeArchiveInspector : IArchiveInspector
{
    /// <summary>Entry lists, keyed by archive path.</summary>
    public Dictionary<string, IReadOnlyList<ArchiveEntry>> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Decompressed content for the single entry, keyed by archive path.</summary>
    public Dictionary<string, byte[]> Contents { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Archives that fail to open.</summary>
    public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When set, every archive fails to open with this message.</summary>
    public string? Error { get; set; }

    /// <summary>Extensions this inspector claims. Defaults to <c>.zip</c>.</summary>
    public HashSet<string> Extensions { get; } = new(new[] { ".zip" }, StringComparer.OrdinalIgnoreCase);

    /// <summary>When false, claims nothing — the resolver then treats every file as a bare ROM.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Invoked just before an entry stream is handed out, to simulate a concurrent write.</summary>
    public Action<string>? OnOpenEntry { get; set; }

    public bool Handles(string extension) => Enabled && Extensions.Contains(extension);

    public bool TryReadEntries(string path, out IReadOnlyList<ArchiveEntry> entries, out string? error)
    {
        if (Error is not null || Unreadable.Contains(path))
        {
            entries = Array.Empty<ArchiveEntry>();
            error = Error ?? "archive could not be read";
            return false;
        }

        error = null;
        entries = Entries.TryGetValue(path, out var found) ? found : Array.Empty<ArchiveEntry>();
        return true;
    }

    public bool TryOpenSingleEntry(string path, out Stream stream, out long size, out string? error)
    {
        if (Error is not null || Unreadable.Contains(path))
        {
            stream = Stream.Null;
            size = 0;
            error = Error ?? "archive could not be read";
            return false;
        }

        if (Entries.TryGetValue(path, out var entries) && entries.Count != 1)
        {
            stream = Stream.Null;
            size = 0;
            error = entries.Count == 0 ? "archive contains no files" : "archive contains more than one entry";
            return false;
        }

        if (!Contents.TryGetValue(path, out var bytes))
        {
            stream = Stream.Null;
            size = 0;
            error = "archive contains no files";
            return false;
        }

        OnOpenEntry?.Invoke(path);

        stream = new MemoryStream(bytes, writable: false);
        size = bytes.Length;
        error = null;
        return true;
    }
}
