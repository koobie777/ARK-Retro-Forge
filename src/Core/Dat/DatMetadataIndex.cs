using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ARK.Core.Dat;

/// <summary>
/// Aggregates metadata from DAT files (serials, disc counts, etc.) for quick lookup.
/// </summary>
public sealed class DatMetadataIndex
{
    private static readonly Regex DescriptionPattern = new(@"^\s*description\s+""(?<value>.+?)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RomNamePattern = new(@"^\s*rom\s*\(\s*name\s+""(?<value>.+?)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly Regex DiscPattern = new(@"\(Disc\s*(?<disc>\d+)(?:\s*of\s*(?<count>\d+))?\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TitleRegionPattern = new(@"^(?<title>.+?)\s*\((?<region>[^)]+)\)", RegexOptions.Compiled);
    internal static readonly Regex SerialPattern = new(@"(?<serial>[A-Z]{4}-\d{5})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, DatTitleMetadata> _entries = new(StringComparer.OrdinalIgnoreCase);

    private DatMetadataIndex()
    {
    }

    public static DatMetadataIndex Load(string system)
    {
        var index = new DatMetadataIndex();
        foreach (var file in DatFileLocator.Enumerate(system))
        {
            index.ProcessFile(file);
        }

        return index;
    }

    public bool TryGet(string? title, string? region, [NotNullWhen(true)] out DatTitleMetadata? metadata)
    {
        var key = BuildKey(title, region);
        if (string.IsNullOrWhiteSpace(key))
        {
            metadata = null;
            return false;
        }

        return _entries.TryGetValue(key, out metadata);
    }

    public static string BuildKey(string? title, string? region)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(title.Trim());
        if (!string.IsNullOrWhiteSpace(region))
        {
            builder.Append(" (").Append(region.Trim()).Append(')');
        }

        return Normalize(builder.ToString());
    }

    public IReadOnlyList<DatTitleMetadata> FindSimilar(string? title, int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(title) || maxResults <= 0)
        {
            return Array.Empty<DatTitleMetadata>();
        }

        var normalized = Normalize(title);
        return _entries
            .Select(entry => new
            {
                Metadata = entry.Value,
                Distance = CalculateDistance(normalized, entry.Key)
            })
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Metadata.Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(x => x.Metadata)
            .ToList();
    }

    private void ProcessFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new StreamReader(stream);
            string? currentKey = null;

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line == null)
                {
                    continue;
                }

                var descMatch = DescriptionPattern.Match(line);
                if (descMatch.Success)
                {
                    var description = descMatch.Groups["value"].Value.Trim();
                    currentKey = RegisterDescription(description);
                    continue;
                }

                if (currentKey == null)
                {
                    continue;
                }

                var romMatch = RomNamePattern.Match(line);
                if (romMatch.Success && _entries.TryGetValue(currentKey, out var metadata))
                {
                    metadata.RegisterRomName(romMatch.Groups["value"].Value);
                }
            }
        }
        catch (IOException)
        {
            // Ignore IO errors; operations will continue with partial data.
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore permission errors; operations will continue with partial data.
        }
        catch (FormatException)
        {
            // Ignore format errors; operations will continue with partial data.
        }
    }

    private string? RegisterDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var (title, region) = ExtractTitleRegion(description);
        var key = BuildKey(title, region);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var metadata = _entries.GetOrAdd(key, _ => new DatTitleMetadata(title ?? description, region));

        foreach (Match match in DiscPattern.Matches(description))
        {
            var disc = ParseInt(match.Groups["disc"].Value);
            var count = ParseInt(match.Groups["count"].Value);
            metadata.RegisterDiscInfo(disc, count);
        }

        foreach (Match match in SerialPattern.Matches(description))
        {
            metadata.RegisterSerial(match.Groups["serial"].Value);
        }

        return key;
    }

    private static (string? Title, string? Region) ExtractTitleRegion(string description)
    {
        var match = TitleRegionPattern.Match(description);
        if (!match.Success)
        {
            return (description, null);
        }

        return (match.Groups["title"].Value.Trim(), match.Groups["region"].Value.Trim());
    }

    private static int? ParseInt(string value)
        => int.TryParse(value, out var result) ? result : null;

    private static string Normalize(string value)
    {
        var withoutDisc = DiscPattern.Replace(value, string.Empty);
        var collapsedWhitespace = Regex.Replace(withoutDisc, @"\s+", " ");
        return collapsedWhitespace.Trim().ToUpperInvariant();
    }

    private static int CalculateDistance(string source, string target)
    {
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var n = source.Length;
        var m = target.Length;
        if (n == 0 || m == 0)
        {
            return Math.Max(n, m);
        }

        var dp = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++)
        {
            dp[i, 0] = i;
        }
        for (var j = 0; j <= m; j++)
        {
            dp[0, j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = char.ToUpperInvariant(source[i - 1]) == char.ToUpperInvariant(target[j - 1]) ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[n, m];
    }
}

public sealed class DatTitleMetadata
{
    private readonly HashSet<string> _serials = new(StringComparer.OrdinalIgnoreCase);

    public DatTitleMetadata(string title, string? region)
    {
        Title = title;
        Region = region;
    }

    public string Title { get; }
    public string? Region { get; }
    public int? DiscCount { get; private set; }
    public IReadOnlyCollection<string> Serials => _serials;

    public void RegisterDiscInfo(int? discNumber, int? discCount)
    {
        if (discCount.HasValue)
        {
            DiscCount = DiscCount.HasValue ? Math.Max(DiscCount.Value, discCount.Value) : discCount.Value;
        }
        else if (discNumber.HasValue && discNumber.Value > 1)
        {
            DiscCount = DiscCount.HasValue ? Math.Max(DiscCount.Value, discNumber.Value) : discNumber.Value;
        }
    }

    public void RegisterSerial(string? serial)
    {
        if (!string.IsNullOrWhiteSpace(serial))
        {
            _serials.Add(serial.Trim().ToUpperInvariant());
        }
    }

    public void RegisterRomName(string romName)
    {
        if (string.IsNullOrWhiteSpace(romName))
        {
            return;
        }

        foreach (Match match in DatMetadataIndex.SerialPattern.Matches(romName))
        {
            RegisterSerial(match.Groups["serial"].Value);
        }

        foreach (Match match in DatMetadataIndex.DiscPattern.Matches(romName))
        {
            var disc = int.TryParse(match.Groups["disc"].Value, out var number) ? number : (int?)null;
            var count = int.TryParse(match.Groups["count"].Value, out var total) ? total : (int?)null;
            RegisterDiscInfo(disc, count);
        }
    }
}

internal static class DatMetadataCache
{
    private static readonly ConcurrentDictionary<string, Lazy<DatMetadataIndex>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static DatMetadataIndex ForSystem(string system)
    {
        var lazy = Cache.GetOrAdd(system, key => new Lazy<DatMetadataIndex>(() => DatMetadataIndex.Load(key)));
        return lazy.Value;
    }
}

internal static class DatFileLocator
{
    public static IEnumerable<string> Enumerate(string system)
    {
        if (string.IsNullOrWhiteSpace(system))
        {
            yield break;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "config", "dat", "local", system),
            Path.Combine(AppContext.BaseDirectory, "config", "dat", system),
            Path.Combine(ArkEnvironment.GetInstanceRoot(), "dat", system),
            Path.Combine(AppContext.BaseDirectory, "dat", system)
        };

        foreach (var directory in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.dat", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }
}
