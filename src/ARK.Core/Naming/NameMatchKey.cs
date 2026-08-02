using System.Text;

namespace ARK.Core.Naming;

/// <summary>
/// An order-independent identity for a parsed name: the grouping-normalized title plus the
/// <i>set</i> of its metadata tokens.
/// </summary>
/// <remarks>
/// <para>
/// Canonical token order is a <b>partial</b> order, so two names carrying identical tokens in
/// different orders are both canonical and neither is wrong. Comparing formatted strings would
/// therefore report two spellings of one release as two different releases. Every grouping,
/// matching and comparison in ARK runs through this key instead.
/// </para>
/// <para>
/// The key folds article inversion (<c>The Legend of Zelda</c> and <c>Legend of Zelda, The</c>
/// are one release) and ignores token order, but never ignores a token's presence — an extra
/// <c>(Rev 1)</c> is a different release and yields a different key.
/// </para>
/// <para>
/// Bracket flags are deliberately excluded. <c>[b]</c> marks a bad dump of a release, not a
/// different release; whether the bytes are actually good is verification's job, not naming's.
/// </para>
/// </remarks>
public static class NameMatchKey
{
    /// <summary>
    /// Builds the match key. A name that could not be tokenized yields an empty key, which never
    /// matches anything — a flagged name is reported, never identified by guesswork.
    /// </summary>
    public static string For(ParsedName parsed, TokenVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(vocabulary);

        if (!parsed.IsTokenizable)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(vocabulary.NormalizeForGrouping(parsed.Title));

        // Ordinal sort over "category:value" makes the key independent of emission order while
        // keeping category significant: (No) as a language must not collide with a region.
        foreach (var token in parsed.Tokens
            .Select(token => (int)token.Category + ":" + vocabulary.NormalizeForGrouping(token.Value))
            .OrderBy(text => text, StringComparer.Ordinal))
        {
            builder.Append('|').Append(token);
        }

        return builder.ToString();
    }
}
