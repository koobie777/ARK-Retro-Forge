using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace ARK.Core.Dat;

/// <summary>
/// Parses Logiqx XML DAT files, the format both No-Intro and Redump publish. Reads the header
/// (name, description, version, date, author) and every <c>rom</c> entry under each <c>game</c> or
/// <c>machine</c>, capturing whichever hashes are present. DTD processing is disabled, so external
/// entities are never resolved.
/// </summary>
public static class LogiqxParser
{
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = true
    };

    /// <summary>Parses a DAT from a stream.</summary>
    public static LogiqxDat Parse(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        using var reader = XmlReader.Create(xml, ReaderSettings);
        return Parse(XDocument.Load(reader));
    }

    /// <summary>Parses a DAT from an XML string.</summary>
    public static LogiqxDat Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, ReaderSettings);
        return Parse(XDocument.Load(reader));
    }

    private static LogiqxDat Parse(XDocument document)
    {
        var root = document.Root
            ?? throw new FormatException("DAT file has no root element.");

        var headerElement = root.Element("header");
        var header = new LogiqxHeader
        {
            Name = Value(headerElement, "name"),
            Description = Value(headerElement, "description"),
            Version = Value(headerElement, "version"),
            Date = Value(headerElement, "date"),
            Author = Value(headerElement, "author")
        };

        var entries = new List<DatEntry>();
        foreach (var game in root.Elements("game").Concat(root.Elements("machine")))
        {
            var gameName = (string?)game.Attribute("name") ?? string.Empty;
            foreach (var rom in game.Elements("rom"))
            {
                entries.Add(new DatEntry
                {
                    GameName = gameName,
                    RomName = (string?)rom.Attribute("name") ?? string.Empty,
                    Size = ParseSize(rom.Attribute("size")),
                    Crc32 = NormalizeHash(rom.Attribute("crc")),
                    Md5 = NormalizeHash(rom.Attribute("md5")),
                    Sha1 = NormalizeHash(rom.Attribute("sha1"))
                });
            }
        }

        return new LogiqxDat(header, entries);
    }

    private static string? Value(XElement? parent, string name)
    {
        var text = parent?.Element(name)?.Value;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static long? ParseSize(XAttribute? attribute) =>
        long.TryParse(attribute?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : null;

    private static string? NormalizeHash(XAttribute? attribute)
    {
        var value = attribute?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }
}
