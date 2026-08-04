using System.Text;

namespace ARK.Core.Units;

/// <summary>One <c>FILE</c> entry in a cue sheet.</summary>
/// <param name="Name">The file name exactly as the sheet spells it.</param>
/// <param name="Format">The declared format, e.g. <c>BINARY</c> or <c>WAVE</c>.</param>
public sealed record CueFile(string Name, string Format);

/// <summary>
/// A parsed cue sheet: the manifest that says which files make up one disc.
/// </summary>
/// <remarks>
/// <para>
/// <b>Membership comes from parsing this, never from filename similarity.</b> Guessing which BINs
/// belong to a CUE by comparing names is how a track from one game gets attached to another, and
/// it is the assumption v1 built on.
/// </para>
/// <para>
/// <b>Track count is not disc count.</b> A sheet with twelve <c>TRACK</c> entries describes one
/// disc whose audio is split across twelve files. Reading that as twelve discs — or as one disc
/// per FILE — is the specific confusion that turned a thousand-game PSX set into a re-download.
/// </para>
/// </remarks>
/// <param name="Files">Files the sheet references, in declaration order.</param>
/// <param name="TrackCount">Total <c>TRACK</c> entries across every file.</param>
public sealed record CueSheet(IReadOnlyList<CueFile> Files, int TrackCount)
{
    /// <summary>An empty sheet, used when a cue could not be read.</summary>
    public static CueSheet Empty { get; } = new([], 0);

    /// <summary>
    /// Parses a cue sheet's text.
    /// </summary>
    /// <remarks>
    /// Deliberately tolerant of the real formatting variance — quoted and unquoted names, varying
    /// indentation, <c>CATALOG</c> and <c>REM</c> lines — and deliberately narrow about what it
    /// extracts. Nothing here interprets track layout, because nothing in ARK needs to: the DAT
    /// hashes the files, and this only has to say which files those are.
    /// </remarks>
    public static CueSheet Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var files = new List<CueFile>();
        var tracks = 0;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
            {
                var (name, format) = SplitFile(line["FILE ".Length..].Trim());
                if (name.Length > 0)
                {
                    files.Add(new CueFile(name, format));
                }
            }
            else if (line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase))
            {
                tracks++;
            }
        }

        return new CueSheet(files, tracks);
    }

    /// <summary>Reads and parses a cue sheet from a stream.</summary>
    public static CueSheet Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Cue sheets are ASCII in practice but occasionally carry a BOM or a stray high byte in a
        // title; UTF-8 with fallback reads both without throwing.
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd());
    }

    private static (string Name, string Format) SplitFile(string remainder)
    {
        if (remainder.StartsWith('"'))
        {
            var close = remainder.IndexOf('"', 1);
            return close < 0
                ? (remainder[1..], string.Empty)
                : (remainder[1..close], remainder[(close + 1)..].Trim());
        }

        // Unquoted: the format is the last whitespace-separated token, everything before it is the
        // name — which can itself contain spaces.
        var lastSpace = remainder.LastIndexOf(' ');
        return lastSpace < 0
            ? (remainder, string.Empty)
            : (remainder[..lastSpace].Trim(), remainder[(lastSpace + 1)..].Trim());
    }
}
