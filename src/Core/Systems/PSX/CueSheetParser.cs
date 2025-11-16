using System.Text.RegularExpressions;

namespace ARK.Core.Systems.PSX;

internal sealed class CueSheet
{
    public List<CueFileEntry> Files { get; } = new();
    public IReadOnlyList<CueTrack> Tracks => Files.SelectMany(f => f.Tracks).ToList();
}

internal sealed class CueFileEntry
{
    public CueFileEntry(string fileName)
    {
        FileName = fileName;
    }

    public string FileName { get; }
    public List<CueTrack> Tracks { get; } = new();
}

internal sealed class CueTrack
{
    public CueTrack(int number, string type)
    {
        Number = number;
        Type = type;
    }

    public int Number { get; }
    public string Type { get; }
    public List<CueIndex> Indexes { get; } = new();
}

internal sealed class CueIndex
{
    public CueIndex(int number, string time)
    {
        Number = number;
        Time = time;
    }

    public int Number { get; }
    public string Time { get; }
}

internal static class CueSheetParser
{
    private static readonly Regex TrackRegex = new(@"^TRACK\s+(?<number>\d+)\s+(?<type>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IndexRegex = new(@"^INDEX\s+(?<number>\d+)\s+(?<time>\d{2}:\d{2}:\d{2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static CueSheet Parse(string cuePath)
    {
        var sheet = new CueSheet();
        CueFileEntry? currentFile = null;
        CueTrack? currentTrack = null;

        foreach (var rawLine in File.ReadAllLines(cuePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("REM", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = ParseFileName(line);
                currentFile = new CueFileEntry(fileName);
                sheet.Files.Add(currentFile);
                currentTrack = null;
                continue;
            }

            if (currentFile is null)
            {
                continue;
            }

            var trackMatch = TrackRegex.Match(line);
            if (trackMatch.Success)
            {
                var number = int.Parse(trackMatch.Groups["number"].Value);
                var type = trackMatch.Groups["type"].Value.Trim();
                currentTrack = new CueTrack(number, type);
                currentFile.Tracks.Add(currentTrack);
                continue;
            }

            if (currentTrack is null)
            {
                continue;
            }

            var indexMatch = IndexRegex.Match(line);
            if (indexMatch.Success)
            {
                var indexNumber = int.Parse(indexMatch.Groups["number"].Value);
                var time = indexMatch.Groups["time"].Value;
                currentTrack.Indexes.Add(new CueIndex(indexNumber, time));
            }
        }

        return sheet;
    }

    private static string ParseFileName(string line)
    {
        var remainder = line[4..].Trim();
        if (remainder.StartsWith("\"", StringComparison.Ordinal))
        {
            var endQuote = remainder.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return remainder[1..endQuote];
            }
        }

        var separatorIndex = remainder.IndexOf(' ');
        return separatorIndex > 0 ? remainder[..separatorIndex] : remainder;
    }
}
