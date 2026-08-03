using System.Globalization;
using ARK.Core.Naming;

namespace ARK.Core.Policy;

/// <summary>The outcome of comparing two releases along one axis.</summary>
public enum AxisComparison
{
    /// <summary>Equal on this axis.</summary>
    Equal = 0,

    /// <summary>The left one ranks higher.</summary>
    LeftWins,

    /// <summary>The right one ranks higher.</summary>
    RightWins,

    /// <summary>
    /// Genuinely not orderable. The group is reported and skipped rather than resolved — a guess
    /// wearing a confident label is worse than an unanswered question.
    /// </summary>
    Incomparable,
}

/// <summary>
/// Ranks two releases along a single axis, and refuses where no ranking exists.
/// </summary>
/// <remarks>
/// Every refusal here is deliberate. Alpha, Beta and Proto have no consistent cross-publisher
/// timeline, and numeric and alphabetic revisions are separate schemes; inventing an order for
/// either would delete a real release on the strength of a coin flip.
/// </remarks>
public static class AxisOrdering
{
    /// <summary>Why two releases could not be ordered.</summary>
    public sealed record Refusal(VariantAxis Axis, string Reason);

    /// <summary>Compares along <paramref name="axis"/>, reporting a reason when it refuses.</summary>
    public static AxisComparison Compare(
        VariantAxis axis,
        ParsedName left,
        ParsedName right,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        reason = null;
        return axis switch
        {
            VariantAxis.Revision => CompareRevision(left, right, out reason),
            VariantAxis.Version => CompareVersion(left, right, out reason),
            VariantAxis.DevStatus => CompareDevStatus(left, right, out reason),
            _ => AxisComparison.Equal,
        };
    }

    // Absent means Rev 0 — the ORIGINAL release, and therefore the OLDEST. "Keep latest" that
    // silently keeps the untagged copy is exactly backwards and is the likeliest bug in curation.
    private static AxisComparison CompareRevision(ParsedName left, ParsedName right, out string? reason)
    {
        reason = null;
        var a = left.Revision;
        var b = right.Revision;

        if (!a.TryCompareTo(b, out var comparison))
        {
            // Rev A and Rev 1 are separate schemes. Both appear in real data, and neither is a
            // position in the other's sequence.
            reason = $"revision schemes do not compare: '{a}' against '{b}'";
            return AxisComparison.Incomparable;
        }

        return comparison == 0 ? AxisComparison.Equal
            : comparison > 0 ? AxisComparison.LeftWins
            : AxisComparison.RightWins;
    }

    // v1.10 is newer than v1.9. String sort inverts that, so segments are parsed numerically.
    private static AxisComparison CompareVersion(ParsedName left, ParsedName right, out string? reason)
    {
        reason = null;
        var a = VersionOf(left);
        var b = VersionOf(right);

        if (a is null && b is null)
        {
            return AxisComparison.Equal;
        }

        // An untagged release is the original, so a tagged version outranks it.
        if (a is null)
        {
            return AxisComparison.RightWins;
        }

        if (b is null)
        {
            return AxisComparison.LeftWins;
        }

        var comparison = CompareSegments(a, b);
        return comparison == 0 ? AxisComparison.Equal
            : comparison > 0 ? AxisComparison.LeftWins
            : AxisComparison.RightWins;
    }

    private static AxisComparison CompareDevStatus(ParsedName left, ParsedName right, out string? reason)
    {
        reason = null;
        var a = left.TokensOf(TokenCategory.DevStatus);
        var b = right.TokensOf(TokenCategory.DevStatus);

        var leftRetail = a.Count == 0;
        var rightRetail = b.Count == 0;

        // Retail outranks every pre-release build. Unambiguous and safe to enforce.
        if (leftRetail && rightRetail)
        {
            return AxisComparison.Equal;
        }

        if (leftRetail)
        {
            return AxisComparison.LeftWins;
        }

        if (rightRetail)
        {
            return AxisComparison.RightWins;
        }

        if (string.Equals(Join(a), Join(b), StringComparison.OrdinalIgnoreCase))
        {
            return AxisComparison.Equal;
        }

        // Both pre-release. A date tag is the only reliable signal between them.
        var leftDate = DateOf(left);
        var rightDate = DateOf(right);
        if (leftDate is not null && rightDate is not null && leftDate != rightDate)
        {
            return string.CompareOrdinal(leftDate, rightDate) > 0
                ? AxisComparison.LeftWins
                : AxisComparison.RightWins;
        }

        reason = $"pre-release builds have no reliable order: '{Join(a)}' against '{Join(b)}', and no date tag separates them";
        return AxisComparison.Incomparable;
    }

    /// <summary>The version token's text, or null when untagged.</summary>
    public static string? VersionOf(ParsedName parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        return parsed.TokensOf(TokenCategory.Version).FirstOrDefault()?.Value;
    }

    /// <summary>The date token's text, or null.</summary>
    public static string? DateOf(ParsedName parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        return parsed.TokensOf(TokenCategory.Date).FirstOrDefault()?.Value;
    }

    /// <summary>
    /// Compares two version strings segment by segment, numerically where a segment is numeric.
    /// <c>v1.10</c> ranks above <c>v1.9</c>; a string sort would invert that.
    /// </summary>
    public static int CompareSegments(string left, string right)
    {
        var a = Segments(left);
        var b = Segments(right);

        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var x = i < a.Count ? a[i] : null;
            var y = i < b.Count ? b[i] : null;

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var leftNumeric = int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var xn);
            var rightNumeric = int.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yn);

            // Numeric where both segments are numbers — this is what puts v1.10 above v1.9.
            var comparison = leftNumeric && rightNumeric ? xn.CompareTo(yn) : string.CompareOrdinal(x, y);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static IReadOnlyList<string> Segments(string version) => version
        .TrimStart('v', 'V')
        .Replace("Version ", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("Ver. ", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Join(IReadOnlyList<NameToken> tokens) =>
        string.Join(", ", tokens.Select(token => token.Value));
}
