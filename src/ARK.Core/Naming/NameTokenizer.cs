using System.Text;
using System.Text.RegularExpressions;

namespace ARK.Core.Naming;

/// <summary>
/// Decomposes a filename into a <see cref="ParsedName"/> by applying the boundary rule and then
/// classifying what follows. Pure string work: no filesystem access, no DAT lookups, no
/// system-specific behaviour.
/// </summary>
/// <remarks>
/// <para>
/// <b>The boundary rule.</b> The first recognized region token separates title from metadata.
/// Everything before it is title, parentheses included; everything after it is metadata. Region
/// sits at index 0 in 9,917 of 9,928 reference names, and all eleven exceptions are titles that
/// legitimately contain parentheses. This is the only rule that can tell
/// <c>(Version 3.5)</c> — title — from <c>(Rev 1)</c> — metadata — in
/// <c>Interactive CD Sampler Pack Volume Three (Version 3.5) (USA) (Rev 1)</c>, since the two
/// are indistinguishable by shape.
/// </para>
/// <para>
/// A name with no recognized region cannot be safely tokenized, so it is flagged
/// (<see cref="NameParseFlag.NoRegion"/>) rather than guessed at.
/// </para>
/// <para>
/// This type never matches a regex against a whole filename positionally. That approach parses
/// <c>Tekken 3 (USA) (En,Fr,De)</c> into region <c>En,Fr,De</c>, corrupting a clean No-Intro name
/// on first contact, and no patch fixes it.
/// </para>
/// </remarks>
public sealed class NameTokenizer
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    private readonly TokenVocabulary _vocabulary;

    /// <summary>Creates a tokenizer over the supplied vocabulary.</summary>
    public NameTokenizer(TokenVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        _vocabulary = vocabulary;
    }

    /// <summary>Decomposes a name. Never throws on malformed input — it flags instead.</summary>
    public ParsedName Parse(string name)
    {
        var cleaned = PreNormalize(name ?? string.Empty);
        var segments = SplitSegments(cleaned);

        var classified = new NameToken?[segments.Count];
        var boundary = -1;
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Kind != SegmentKind.Paren)
            {
                continue;
            }

            var token = _vocabulary.Classify(segments[i].Inner);
            classified[i] = token;
            if (boundary < 0 && token.Category == TokenCategory.Region)
            {
                boundary = i;
            }
        }

        if (boundary < 0)
        {
            // No region: the title/metadata split is undefined. Flag, do not guess.
            return new ParsedName(cleaned, Array.Empty<NameToken>(), Array.Empty<string>(), NameParseFlag.NoRegion);
        }

        var title = new StringBuilder();
        var pulled = new List<NameToken>();

        for (var i = 0; i < boundary; i++)
        {
            var segment = segments[i];
            if (segment.Kind == SegmentKind.Paren)
            {
                var token = classified[i]!;
                // Only closed, structurally-decidable categories may be lifted out of a title.
                // Open forms stay put, which is what preserves "(Version 3.5)" and "(Racing)".
                if (_vocabulary.CanPullBeforeBoundary(token.Category))
                {
                    pulled.Add(token);
                    continue;
                }
            }

            title.Append(segment.Raw);
        }

        var metadata = new List<NameToken>(segments.Count) { classified[boundary]! };
        metadata.AddRange(pulled);

        var bracketFlags = new List<string>();
        for (var i = boundary + 1; i < segments.Count; i++)
        {
            var segment = segments[i];
            switch (segment.Kind)
            {
                case SegmentKind.Paren:
                    metadata.Add(classified[i]!);
                    break;
                case SegmentKind.Bracket when !_vocabulary.IsStrippedBracketFlag(segment.Inner):
                    bracketFlags.Add(segment.Inner);
                    break;
                default:
                    break;
            }
        }

        var titleText = _vocabulary.InvertLeadingArticle(WhitespaceRun.Replace(title.ToString(), " ").Trim());

        return new ParsedName(titleText, Order(Deduplicate(metadata)), bracketFlags);
    }

    /// <summary>
    /// Collapses repeated tokens and applies the canonical rank order. Exposed so the formatter
    /// can canonicalize a <see cref="ParsedName"/> assembled by hand, not only one this
    /// tokenizer produced.
    /// </summary>
    public IReadOnlyList<NameToken> Canonicalize(IEnumerable<NameToken> tokens) =>
        Order(Deduplicate(tokens));

    // Stacking is defeated here: a token is emitted at most once, so a name rebuilt from a
    // parse can never accumulate "(USA) (USA) (USA)" however many times it is round-tripped.
    private static List<NameToken> Deduplicate(IEnumerable<NameToken> tokens)
    {
        var seen = new HashSet<NameToken>();
        var result = new List<NameToken>();
        foreach (var token in tokens)
        {
            if (seen.Add(token))
            {
                result.Add(token);
            }
        }

        return result;
    }

    // OrderBy is a stable sort, which is the whole mechanism: categories that real No-Intro data
    // orders inconsistently share a rank and therefore keep the order they arrived in.
    private IReadOnlyList<NameToken> Order(List<NameToken> tokens) =>
        tokens.OrderBy(token => _vocabulary.RankOf(token.Category)).ToArray();

    private static string PreNormalize(string name)
    {
        var text = name;

        // A scene rename replaces every separator with an underscore. Only treat underscores as
        // separators when the name has no spaces at all — "Watch_Dogs (USA) (En,Fr,Es,Pt)" is a
        // real title whose underscore must survive.
        if (text.IndexOf(' ') < 0 && text.IndexOf('_') >= 0)
        {
            text = text.Replace('_', ' ');
        }

        return WhitespaceRun.Replace(text, " ").Trim();
    }

    private static List<Segment> SplitSegments(string text)
    {
        var segments = new List<Segment>();
        var run = new StringBuilder();

        void FlushText()
        {
            if (run.Length > 0)
            {
                segments.Add(new Segment(SegmentKind.Text, run.ToString(), run.ToString()));
                run.Clear();
            }
        }

        for (var i = 0; i < text.Length; i++)
        {
            var open = text[i];
            var close = open switch { '(' => ')', '[' => ']', _ => '\0' };
            if (close == '\0')
            {
                run.Append(open);
                continue;
            }

            var depth = 1;
            var j = i + 1;
            for (; j < text.Length && depth > 0; j++)
            {
                if (text[j] == open)
                {
                    depth++;
                }
                else if (text[j] == close)
                {
                    depth--;
                }
            }

            if (depth > 0)
            {
                // Unbalanced delimiter: it is ordinary title text, not a token.
                run.Append(open);
                continue;
            }

            FlushText();
            var raw = text[i..j];
            segments.Add(new Segment(
                open == '(' ? SegmentKind.Paren : SegmentKind.Bracket,
                raw[1..^1],
                raw));
            i = j - 1;
        }

        FlushText();
        return segments;
    }

    private enum SegmentKind
    {
        Text,
        Paren,
        Bracket,
    }

    private readonly record struct Segment(SegmentKind Kind, string Inner, string Raw);
}
