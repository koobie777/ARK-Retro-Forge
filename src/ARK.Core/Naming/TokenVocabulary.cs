using System.Text.RegularExpressions;

namespace ARK.Core.Naming;

/// <summary>
/// The closed vocabularies and the classification rules that consult them. Pure: it reads no
/// files, touches no filesystem and needs no console, so the whole of naming is testable at
/// string speed.
/// </summary>
/// <remarks>
/// Classification is strictly first-match-wins, in the order laid out in the vocabulary
/// document: closed vocabularies first, then open pattern classes, then the unknown bucket.
/// Vocabulary decides and position only breaks ties — <c>(Japan)</c> is a region, <c>(Ja)</c> a
/// language and <c>(No)</c> is Norwegian, and no amount of positional reasoning distinguishes
/// <c>(USA, Europe)</c> from <c>(Virtual Console, Switch Online)</c>.
/// </remarks>
public sealed class TokenVocabulary
{
    private static readonly Regex RevisionPattern = new(@"^Rev\s*(?<body>[0-9]+|[A-Za-z])$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new(@"^(v|V|Ver\.\s*|Version\s+)[0-9]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DiscPattern = new(@"^(?<kind>Disc|Disk|CD|Side|Tape)\s+(?<ordinal>\S+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex DatePattern = new(@"^(19|20)[0-9]{2}([0-9]{4}|([-.]([0-9]{2}|xx)){1,2})?(T[0-9]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SerialPattern = new(@"^([A-Z]{2,4}[- ]?[0-9]{2,6}|[A-Z]{4})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LanguageCodePattern = new(@"^[A-Za-z]{2}(-[A-Za-z]{2,4})?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OrdinalSuffixPattern = new(@"^(?<prefix>.+?)\s+[0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    private readonly Dictionary<TokenCategory, HashSet<string>> _terms;
    private readonly HashSet<TokenCategory> _compoundHeads;
    private readonly HashSet<string> _ordinalPrefixes;
    private readonly Dictionary<TokenCategory, int> _ranks;
    private readonly HashSet<TokenCategory> _pullable;
    private readonly HashSet<string> _strippedBracketFlags;
    private readonly string[] _invertArticles;
    private readonly HashSet<string> _allArticles;
    private readonly Regex? _compilationHints;

    private TokenVocabulary(
        Dictionary<TokenCategory, HashSet<string>> terms,
        HashSet<TokenCategory> compoundHeads,
        HashSet<string> ordinalPrefixes,
        Dictionary<TokenCategory, int> ranks,
        HashSet<TokenCategory> pullable,
        HashSet<string> strippedBracketFlags,
        string[] invertArticles,
        HashSet<string> allArticles,
        Regex? compilationHints)
    {
        _terms = terms;
        _compoundHeads = compoundHeads;
        _ordinalPrefixes = ordinalPrefixes;
        _ranks = ranks;
        _pullable = pullable;
        _strippedBracketFlags = strippedBracketFlags;
        _invertArticles = invertArticles;
        _allArticles = allArticles;
        _compilationHints = compilationHints;
    }

    /// <summary>Articles that are moved to the end of the first title segment on format.</summary>
    public IReadOnlyList<string> InvertingArticles => _invertArticles;

    /// <summary>Builds a vocabulary from already-materialized documents. Performs no I/O.</summary>
    public static TokenVocabulary FromDocuments(IEnumerable<NamingVocabularyDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var terms = new Dictionary<TokenCategory, HashSet<string>>();
        var compoundHeads = new HashSet<TokenCategory>();
        var ordinalPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranks = new Dictionary<TokenCategory, int>();
        var pullable = new HashSet<TokenCategory>();
        var stripped = new HashSet<string>(StringComparer.Ordinal);
        var invert = new List<string>();
        var allArticles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hints = new List<string>();

        foreach (var document in documents)
        {
            if (Enum.TryParse<TokenCategory>(document.Category, ignoreCase: true, out var category) &&
                document.Terms is { Length: > 0 })
            {
                if (!terms.TryGetValue(category, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    terms[category] = set;
                }

                foreach (var term in document.Terms)
                {
                    set.Add(term);
                }

                if (document.CompoundHeads == true)
                {
                    compoundHeads.Add(category);
                }
            }

            foreach (var prefix in document.OrdinalPrefixes ?? Array.Empty<string>())
            {
                ordinalPrefixes.Add(prefix);
            }

            foreach (var flag in document.Strip ?? Array.Empty<string>())
            {
                stripped.Add(flag);
            }

            foreach (var article in document.InvertOnFormat ?? Array.Empty<string>())
            {
                invert.Add(article);
                allArticles.Add(article);
            }

            foreach (var article in document.GroupingOnly ?? Array.Empty<string>())
            {
                allArticles.Add(article);
            }

            hints.AddRange(document.CompilationHints ?? Array.Empty<string>());

            foreach (var name in document.PullBeforeBoundary ?? Array.Empty<string>())
            {
                if (Enum.TryParse<TokenCategory>(name, ignoreCase: true, out var pullableCategory))
                {
                    pullable.Add(pullableCategory);
                }
            }

            var groups = document.RankGroups;
            if (groups is not null)
            {
                for (var rank = 0; rank < groups.Length; rank++)
                {
                    foreach (var name in groups[rank] ?? Array.Empty<string>())
                    {
                        if (Enum.TryParse<TokenCategory>(name, ignoreCase: true, out var ranked))
                        {
                            ranks[ranked] = rank;
                        }
                    }
                }
            }
        }

        var hintPattern = hints.Count == 0
            ? null
            : new Regex(@"\b(" + string.Join("|", hints.Select(Regex.Escape)) + @")\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        return new TokenVocabulary(
            terms, compoundHeads, ordinalPrefixes, ranks, pullable, stripped,
            invert.ToArray(), allArticles, hintPattern);
    }

    /// <summary>Emission rank. Unranked categories sort last; equal ranks keep input order.</summary>
    public int RankOf(TokenCategory category) => _ranks.TryGetValue(category, out var rank) ? rank : int.MaxValue;

    /// <summary>
    /// Whether a token of this category may be lifted out of a title when it sits before the
    /// region boundary. Only closed, structurally-decidable forms qualify; open forms such as
    /// Version stay put, which is what keeps <c>(Version 3.5)</c> inside its title.
    /// </summary>
    public bool CanPullBeforeBoundary(TokenCategory category) => _pullable.Contains(category);

    /// <summary>Whether a bracket flag is dropped from canonical output.</summary>
    public bool IsStrippedBracketFlag(string flag) => _strippedBracketFlags.Contains(flag);

    /// <summary>True when the token is a region or a comma+space list of regions.</summary>
    public bool IsRegion(string token) => AllPartsAreTerms(token, TokenCategory.Region);

    /// <summary>
    /// Classifies a token and returns the normalized text to emit for it. First match wins,
    /// closed vocabularies before open patterns, unknown last.
    /// </summary>
    public NameToken Classify(string raw)
    {
        var token = WhitespaceRun.Replace(raw ?? string.Empty, " ").Trim();

        if (AllPartsAreTerms(token, TokenCategory.Region))
        {
            return new NameToken(TokenCategory.Region, token, raw!);
        }

        if (IsLanguageList(token))
        {
            return new NameToken(TokenCategory.Language, token, raw!);
        }

        var revision = RevisionPattern.Match(token);
        if (revision.Success)
        {
            return new NameToken(TokenCategory.Revision, "Rev " + revision.Groups["body"].Value.ToUpperInvariant(), raw!);
        }

        if (VersionPattern.IsMatch(token))
        {
            return new NameToken(TokenCategory.Version, token, raw!);
        }

        if (IsTerm(token, TokenCategory.NonGame))
        {
            return new NameToken(TokenCategory.NonGame, token, raw!);
        }

        if (IsDevStatus(token))
        {
            return new NameToken(TokenCategory.DevStatus, token, raw!);
        }

        if (IsTerm(token, TokenCategory.Edition))
        {
            return new NameToken(TokenCategory.Edition, token, raw!);
        }

        if (DatePattern.IsMatch(token))
        {
            return new NameToken(TokenCategory.Date, token, raw!);
        }

        var disc = DiscPattern.Match(token);
        if (disc.Success)
        {
            var kind = disc.Groups["kind"].Value;
            // Disk and Disc are the same concept spelled two ways; Side, CD and Tape are not.
            var normalizedKind = kind.Equals("Disk", StringComparison.OrdinalIgnoreCase) ? "Disc" : kind;
            return new NameToken(TokenCategory.Disc, $"{normalizedKind} {disc.Groups["ordinal"].Value}", raw!);
        }

        if (IsTerm(token, TokenCategory.Licensing))
        {
            return new NameToken(TokenCategory.Licensing, token, raw!);
        }

        if (AllPartsAreTerms(token, TokenCategory.Distribution))
        {
            return new NameToken(TokenCategory.Distribution, token, raw!);
        }

        if (IsTerm(token, TokenCategory.Hardware) || HasCompoundHead(token, TokenCategory.Hardware))
        {
            return new NameToken(TokenCategory.Hardware, token, raw!);
        }

        if (IsTerm(token, TokenCategory.Publisher))
        {
            return new NameToken(TokenCategory.Publisher, token, raw!);
        }

        if (SerialPattern.IsMatch(token))
        {
            return new NameToken(TokenCategory.Serial, token, raw!);
        }

        if (_compilationHints?.IsMatch(token) == true)
        {
            return new NameToken(TokenCategory.Compilation, token, raw!);
        }

        return new NameToken(TokenCategory.Unknown, token, raw!);
    }

    /// <summary>
    /// Normalizes a title or token for grouping: article inversion undone, case and whitespace
    /// folded. <c>The Cowabunga Collection</c> and <c>Cowabunga Collection, The</c> land on the
    /// same key, as do <c>The Legend of Zelda</c> and <c>Legend of Zelda, The</c>.
    /// </summary>
    public string NormalizeForGrouping(string value)
    {
        var text = WhitespaceRun.Replace(value ?? string.Empty, " ").Trim();
        var head = FirstSegment(text, out var tail);

        foreach (var article in _allArticles)
        {
            var suffix = ", " + article;
            if (head.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                head = article + " " + head[..^suffix.Length];
                break;
            }
        }

        return (head + tail).ToUpperInvariant();
    }

    /// <summary>
    /// Moves a leading inverting article to the end of the first <c>" - "</c> segment, matching
    /// No-Intro: <c>The Addams Family - Pugsley's Scavenger Hunt</c> becomes
    /// <c>Addams Family, The - Pugsley's Scavenger Hunt</c>.
    /// </summary>
    public string InvertLeadingArticle(string title)
    {
        var head = FirstSegment(title, out var tail);

        foreach (var article in _invertArticles)
        {
            var prefix = article + " ";
            if (head.StartsWith(prefix, StringComparison.Ordinal) && head.Length > prefix.Length)
            {
                return head[prefix.Length..] + ", " + article + tail;
            }
        }

        return title;
    }

    private static string FirstSegment(string text, out string tail)
    {
        var index = text.IndexOf(" - ", StringComparison.Ordinal);
        if (index < 0)
        {
            tail = string.Empty;
            return text;
        }

        tail = text[index..];
        return text[..index];
    }

    private bool IsTerm(string token, TokenCategory category) =>
        _terms.TryGetValue(category, out var set) && set.Contains(token);

    private bool AllPartsAreTerms(string token, TokenCategory category)
    {
        if (!_terms.TryGetValue(category, out var set) || token.Length == 0)
        {
            return false;
        }

        foreach (var part in token.Split(','))
        {
            if (!set.Contains(part.Trim()))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasCompoundHead(string token, TokenCategory category)
    {
        if (!_compoundHeads.Contains(category))
        {
            return false;
        }

        var comma = token.IndexOf(',');
        return comma > 0 && IsTerm(token[..comma].Trim(), category);
    }

    private bool IsDevStatus(string token)
    {
        if (IsTerm(token, TokenCategory.DevStatus) || HasCompoundHead(token, TokenCategory.DevStatus))
        {
            return true;
        }

        // Ordinal forms: Beta 12, Proto 3, Demo 1 — the prefix is vocabulary, the number is not.
        var ordinal = OrdinalSuffixPattern.Match(token);
        return ordinal.Success && _ordinalPrefixes.Contains(ordinal.Groups["prefix"].Value);
    }

    private bool IsLanguageList(string token)
    {
        if (!_terms.TryGetValue(TokenCategory.Language, out var codes) || token.Length == 0)
        {
            return false;
        }

        // ',' separates codes and '+' separates whole language SETS; both are preserved verbatim
        // on output, so for classification purposes every element simply has to be a known code.
        foreach (var part in token.Split(',', '+'))
        {
            var element = part.Trim();
            if (!LanguageCodePattern.IsMatch(element))
            {
                return false;
            }

            var dash = element.IndexOf('-');
            var baseCode = dash < 0 ? element : element[..dash];
            if (!codes.Contains(baseCode))
            {
                return false;
            }
        }

        return true;
    }
}
