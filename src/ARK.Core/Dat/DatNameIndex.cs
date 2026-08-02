using ARK.Core.Naming;

namespace ARK.Core.Dat;

/// <summary>
/// An in-memory index of catalog entries keyed by <see cref="NameMatchKey"/>, so a scanned file
/// is identified by comparing <b>token sets</b> rather than strings.
/// </summary>
/// <remarks>
/// <para>
/// String comparison is wrong here and would be wrong quietly. Canonical token order is a partial
/// order, so a release can be spelled more than one way and every spelling is canonical —
/// <c>(Unl) (v2.35)</c> and <c>(v2.35) (Unl)</c> are the same release. Comparing formatted names
/// reports those as two different games; comparing token sets does not.
/// </para>
/// <para>
/// A DAT entry whose own name cannot be tokenized is skipped rather than indexed under an empty
/// key, which would make it match every other unparseable name.
/// </para>
/// </remarks>
public sealed class DatNameIndex
{
    private readonly Dictionary<string, List<CatalogEntry>> _byKey;
    private readonly NameTokenizer _tokenizer;
    private readonly TokenVocabulary _vocabulary;

    private DatNameIndex(
        Dictionary<string, List<CatalogEntry>> byKey,
        NameTokenizer tokenizer,
        TokenVocabulary vocabulary)
    {
        _byKey = byKey;
        _tokenizer = tokenizer;
        _vocabulary = vocabulary;
    }

    /// <summary>Number of distinct token-set keys indexed.</summary>
    public int Count => _byKey.Count;

    /// <summary>Builds an index over the supplied entries.</summary>
    public static DatNameIndex Build(
        IEnumerable<CatalogEntry> entries,
        NameTokenizer tokenizer,
        TokenVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(vocabulary);

        var byKey = new Dictionary<string, List<CatalogEntry>>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var name = entry.GameName is { Length: > 0 } gameName ? gameName : StripExtension(entry.RomName);
            var key = NameMatchKey.For(tokenizer.Parse(name), vocabulary);
            if (key.Length == 0)
            {
                continue;
            }

            if (!byKey.TryGetValue(key, out var bucket))
            {
                bucket = new List<CatalogEntry>();
                byKey[key] = bucket;
            }

            bucket.Add(entry);
        }

        return new DatNameIndex(byKey, tokenizer, vocabulary);
    }

    /// <summary>
    /// Finds the entry matching a parsed name, or null. When several DATs carry the same release
    /// the first indexed wins — they describe the same thing, and picking between DAT sources is
    /// not this phase's decision.
    /// </summary>
    public CatalogEntry? Find(ParsedName parsed)
    {
        var key = NameMatchKey.For(parsed, _vocabulary);
        return key.Length > 0 && _byKey.TryGetValue(key, out var bucket) ? bucket[0] : null;
    }

    /// <summary>Finds the entry matching a raw name by tokenizing it first.</summary>
    public CatalogEntry? Find(string name) => Find(_tokenizer.Parse(name));

    private static string StripExtension(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot > 0 && value.Length - dot <= 5 ? value[..dot] : value;
    }
}
