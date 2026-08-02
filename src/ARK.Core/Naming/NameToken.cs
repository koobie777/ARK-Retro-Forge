namespace ARK.Core.Naming;

/// <summary>
/// One classified parenthetical token. <see cref="Value"/> is the normalized text that will be
/// emitted (without its parentheses); <see cref="Raw"/> is what was actually read, kept so that
/// nothing is silently rewritten out of existence.
/// </summary>
/// <param name="Category">Classification assigned by <see cref="TokenVocabulary"/>.</param>
/// <param name="Value">Normalized token text, e.g. <c>Rev 1</c> for a raw <c>Rev1</c>.</param>
/// <param name="Raw">Token text exactly as it appeared in the input.</param>
public sealed record NameToken(TokenCategory Category, string Value, string Raw)
{
    /// <summary>
    /// Identity for de-duplication and for the round-trip invariant. Deliberately excludes
    /// <see cref="Raw"/>: <c>(Rev1)</c> and <c>(Rev 1)</c> are the same token, and a re-parse of
    /// formatted output sees only the normalized form.
    /// </summary>
    public bool Equals(NameToken? other) =>
        other is not null &&
        Category == other.Category &&
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine((int)Category, Value);

    /// <inheritdoc />
    public override string ToString() => $"{Category}:{Value}";
}
