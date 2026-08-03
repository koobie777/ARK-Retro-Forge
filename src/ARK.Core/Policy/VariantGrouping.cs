using System.Text;
using ARK.Core.Naming;

namespace ARK.Core.Policy;

/// <summary>
/// Builds the key that decides which releases are variants of the same game.
/// </summary>
/// <remarks>
/// <para>
/// Built from <see cref="ParsedName"/> <b>token sets, never formatted strings</b>. Canonical token
/// order is a partial order, so two names carrying identical tokens can format differently and
/// still be the same release; comparing strings would split them.
/// </para>
/// <para>
/// The key is the base title plus whichever axes the policy treats as identity. That makes
/// grouping a policy choice rather than a fixed rule: with region as identity,
/// <c>Game (USA)</c> and <c>Game (Japan)</c> are different games; with region as variance they are
/// two variants of one. The report has to state which was used, because the same collection
/// produces different decisions under each.
/// </para>
/// </remarks>
public static class VariantGrouping
{
    /// <summary>
    /// True when a name is non-game content and must not be grouped at all.
    /// </summary>
    /// <remarks>
    /// A Bonus Disc is not a revision of the game it shipped beside, and DLC is not a release
    /// candidate. Comparing either against retail is a category error, so they never enter a
    /// group — 202 files on the reference corpus.
    /// </remarks>
    public static bool IsNonGameContent(ParsedName parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        return parsed.TokensOf(TokenCategory.NonGame).Count > 0;
    }

    /// <summary>
    /// Builds the grouping key for a parsed name under a policy.
    /// </summary>
    public static string KeyFor(ParsedName parsed, TokenVocabulary vocabulary, VariantPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(policy);

        var builder = new StringBuilder(vocabulary.NormalizeForGrouping(parsed.Title));

        // Ordinal sort makes the key independent of emission order while keeping the category
        // significant, so a language token can never collide with a region of the same text.
        foreach (var token in parsed.Tokens
            .Where(token => IsIdentity(token.Category, policy))
            .Select(token => (int)token.Category + ":" + vocabulary.NormalizeForGrouping(token.Value))
            .OrderBy(text => text, StringComparer.Ordinal))
        {
            builder.Append('|').Append(token);
        }

        return builder.ToString();
    }

    /// <summary>Whether a token category contributes to identity under this policy.</summary>
    public static bool IsIdentity(TokenCategory category, VariantPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        // Hardware and date never participate — one describes cartridge capability, the other is
        // evidence for ordering pre-release builds rather than something to curate on.
        if (VariantAxes.IsNeverCurated(category))
        {
            return false;
        }

        var axis = VariantAxes.For(category);
        if (axis is null)
        {
            // Not a curation axis at all — disc number, serial, publisher, compilation, or an
            // unknown token. Treated as identity so units differing on it are never collapsed
            // together. Conservative on purpose: this can only ever reduce removals.
            return true;
        }

        return policy.RuleFor(axis.Value).Role == AxisRole.Identity;
    }
}
