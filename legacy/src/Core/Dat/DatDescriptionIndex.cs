using System.Collections.Concurrent;

namespace ARK.Core.Dat;

public sealed class DatDescriptionIndex
{
    private readonly ConcurrentDictionary<string, byte> _entries = new(StringComparer.OrdinalIgnoreCase);

    private DatDescriptionIndex()
    {
    }

    public IEnumerable<string> Descriptions => _entries.Keys;

    public bool Contains(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return _entries.ContainsKey(title.Trim());
    }

    public static async Task<DatDescriptionIndex> LoadAsync(IEnumerable<string> datPaths, CancellationToken cancellationToken = default)
    {
        var index = new DatDescriptionIndex();
        foreach (var path in datPaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            using var stream = File.OpenRead(path);
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync();
                if (line == null)
                {
                    continue;
                }

                line = line.Trim();
                if (!line.StartsWith("description", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var description = ExtractQuotedValue(line);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    index._entries.TryAdd(description.Trim(), 0);
                }
            }
        }

        return index;
    }

    private static string? ExtractQuotedValue(string line)
    {
        var firstQuote = line.IndexOf('"');
        if (firstQuote < 0)
        {
            return null;
        }

        var secondQuote = line.IndexOf('"', firstQuote + 1);
        if (secondQuote < 0)
        {
            return null;
        }

        return line[(firstQuote + 1)..secondQuote];
    }
}
