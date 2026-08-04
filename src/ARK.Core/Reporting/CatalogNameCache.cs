using ARK.Core.Naming;

namespace ARK.Core.Reporting;

/// <summary>
/// Memoizes tokenization of catalog entry names.
/// </summary>
/// <remarks>
/// <para>
/// A real catalog holds over 1.5M entries across 334 DATs. Tokenizing all of them on every run is
/// unacceptable; tokenizing only the DATs actually in scope is not, and doing it once per name is
/// what makes even that affordable — DAT names repeat heavily across revisions and regions.
/// </para>
/// <para>
/// Deliberately in-memory and scoped to a run rather than persisted. The alternative is a cache
/// file the size of the catalog whose invalidation depends on the vocabulary, which is a defect
/// waiting to happen: change a term in <c>config/naming</c> and every stored parse is silently
/// wrong.
/// </para>
/// </remarks>
public sealed class CatalogNameCache
{
    private readonly NameTokenizer _tokenizer;
    private readonly Dictionary<string, ParsedName> _parsed = new(StringComparer.Ordinal);

    /// <summary>Creates a cache over a tokenizer.</summary>
    public CatalogNameCache(NameTokenizer tokenizer)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        _tokenizer = tokenizer;
    }

    /// <summary>Names tokenized this session.</summary>
    public int Tokenized { get; private set; }

    /// <summary>Lookups served from the cache.</summary>
    public int Hits { get; private set; }

    /// <summary>Distinct names held.</summary>
    public int Count => _parsed.Count;

    /// <summary>Parses a name, or returns the parse already held for it.</summary>
    public ParsedName Parse(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_parsed.TryGetValue(name, out var cached))
        {
            Hits++;
            return cached;
        }

        var parsed = _tokenizer.Parse(StripExtension(name));
        _parsed[name] = parsed;
        Tokenized++;
        return parsed;
    }

    private static string StripExtension(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot > 0 && value.Length - dot <= 5 ? value[..dot] : value;
    }
}
