using System.Text;

namespace ARK.Core.Naming;

/// <summary>
/// Reassembles a <see cref="ParsedName"/> into a canonical filename. The name is always rebuilt
/// from scratch and every token is emitted at most once, so appending to an existing name — the
/// mechanism behind v1's <c>(USA) (USA) (USA)</c> — is not something this type can do.
/// </summary>
/// <remarks>
/// <para>
/// Emission order comes from <c>config/naming/order.json</c>, expressed as rank <i>groups</i>
/// rather than a total order, and the sort is stable. Where every real name agrees on the order
/// of two categories, that order is imposed. Where real No-Intro data contains both directions —
/// 12 names carry <c>(Unl) (v2.35)</c> against 54 carrying <c>(v1.1) (Unl)</c> — the categories
/// share a rank and the input order survives.
/// </para>
/// <para>
/// That distinction matters beyond tidiness: no single total order can reproduce all 9,363
/// reference names, so imposing one would rewrite 29 correctly-named files into names their own
/// DAT entry no longer matches.
/// </para>
/// </remarks>
public sealed class NameFormatter
{
    private readonly NameTokenizer _tokenizer;

    /// <summary>Creates a formatter that canonicalizes through the supplied tokenizer.</summary>
    public NameFormatter(NameTokenizer tokenizer)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        _tokenizer = tokenizer;
    }

    /// <summary>
    /// Renders the canonical name. Returns <c>false</c> for a name that could not be tokenized —
    /// a flagged name yields no output at all rather than a guessed one.
    /// </summary>
    public bool TryFormat(ParsedName parsed, out string formatted)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        if (!parsed.IsTokenizable)
        {
            formatted = string.Empty;
            return false;
        }

        var builder = new StringBuilder(parsed.Title);
        foreach (var token in _tokenizer.Canonicalize(parsed.Tokens))
        {
            builder.Append(" (").Append(token.Value).Append(')');
        }

        foreach (var flag in parsed.BracketFlags)
        {
            builder.Append(" [").Append(flag).Append(']');
        }

        formatted = builder.ToString();
        return true;
    }

    /// <summary>Renders the canonical name, throwing when the name was flagged.</summary>
    /// <exception cref="InvalidOperationException">The name carries a parse flag.</exception>
    public string Format(ParsedName parsed)
    {
        if (!TryFormat(parsed, out var formatted))
        {
            ArgumentNullException.ThrowIfNull(parsed);
            throw new InvalidOperationException(
                $"Cannot format a name flagged '{parsed.Flag}'. Flagged names are reported, never renamed.");
        }

        return formatted;
    }
}
