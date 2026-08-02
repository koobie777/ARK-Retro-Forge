namespace ARK.Core.Naming;

/// <summary>Why a name could not be safely tokenized.</summary>
public enum NameParseFlag
{
    /// <summary>Tokenized cleanly.</summary>
    None = 0,

    /// <summary>
    /// No recognized region token, so the title/metadata boundary is undefined. The name is
    /// flagged rather than guessed at — a skipped file always beats a wrongly-renamed one.
    /// </summary>
    NoRegion,
}

/// <summary>
/// A decomposed filename: the title, the ordered metadata tokens, and the bracket flags.
/// Equality is structural over exactly what the formatter re-emits, which is what makes the
/// round-trip invariant <c>parse(format(parse(x))) == parse(x)</c> executable.
/// </summary>
public sealed class ParsedName : IEquatable<ParsedName>
{
    /// <summary>Builds a parsed name.</summary>
    public ParsedName(
        string title,
        IReadOnlyList<NameToken> tokens,
        IReadOnlyList<string> bracketFlags,
        NameParseFlag flag = NameParseFlag.None)
    {
        Title = title;
        Tokens = tokens;
        BracketFlags = bracketFlags;
        Flag = flag;
    }

    /// <summary>Everything before the region boundary, parentheses included.</summary>
    public string Title { get; }

    /// <summary>Classified metadata tokens in canonical emission order.</summary>
    public IReadOnlyList<NameToken> Tokens { get; }

    /// <summary>Surviving bracket flags, in input order.</summary>
    public IReadOnlyList<string> BracketFlags { get; }

    /// <summary>Why tokenization was refused, if it was.</summary>
    public NameParseFlag Flag { get; }

    /// <summary>True when the name tokenized cleanly and may be formatted.</summary>
    public bool IsTokenizable => Flag == NameParseFlag.None;

    /// <summary>All tokens of one category, in order.</summary>
    public IReadOnlyList<NameToken> TokensOf(TokenCategory category) =>
        Tokens.Where(token => token.Category == category).ToArray();

    /// <summary>
    /// Unrecognized tokens, preserved and retrievable. This bucket is the input queue for
    /// extending the vocabulary tables — it is never allowed to silently empty.
    /// </summary>
    public IReadOnlyList<NameToken> UnknownTokens => TokensOf(TokenCategory.Unknown);

    /// <summary>Region terms, flattened across every region token.</summary>
    public IReadOnlyList<string> Regions => TokensOf(TokenCategory.Region)
        .SelectMany(token => token.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        .ToArray();

    /// <summary>
    /// Language codes, flattened across every language token for querying. The token's own
    /// <see cref="NameToken.Value"/> keeps the <c>+</c>-separated set structure verbatim.
    /// </summary>
    public IReadOnlyList<string> Languages => TokensOf(TokenCategory.Language)
        .SelectMany(token => token.Value.Split(new[] { ',', '+' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        .ToArray();

    /// <summary>
    /// The parsed revision. An absent tag yields <see cref="RevisionValue.Absent"/> — Rev 0,
    /// the oldest — rather than null, so callers cannot accidentally treat it as "newest".
    /// </summary>
    public RevisionValue Revision
    {
        get
        {
            var token = Tokens.FirstOrDefault(candidate => candidate.Category == TokenCategory.Revision);
            return token is null ? RevisionValue.Absent : ParseRevision(token.Value);
        }
    }

    /// <inheritdoc />
    public bool Equals(ParsedName? other) =>
        other is not null &&
        Flag == other.Flag &&
        string.Equals(Title, other.Title, StringComparison.Ordinal) &&
        Tokens.SequenceEqual(other.Tokens) &&
        BracketFlags.SequenceEqual(other.BracketFlags, StringComparer.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ParsedName);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Title, StringComparer.Ordinal);
        hash.Add((int)Flag);
        foreach (var token in Tokens)
        {
            hash.Add(token);
        }

        foreach (var flag in BracketFlags)
        {
            hash.Add(flag, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Title} [{string.Join(" ", Tokens)}]{(BracketFlags.Count == 0 ? string.Empty : " " + string.Join(" ", BracketFlags))}";

    private static RevisionValue ParseRevision(string value)
    {
        var body = value.AsSpan("Rev".Length).Trim();
        if (body.Length == 0)
        {
            return RevisionValue.Absent;
        }

        return int.TryParse(body, out var number)
            ? RevisionValue.Numeric(number)
            : RevisionValue.Alphabetic(body[0]);
    }
}
