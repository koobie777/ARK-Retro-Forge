using System.Globalization;

namespace ARK.Core.Naming;

/// <summary>Which revision scheme a <see cref="RevisionValue"/> belongs to.</summary>
public enum RevisionScheme
{
    /// <summary>No revision tag. Means Rev 0 — the original, and the oldest.</summary>
    Absent = 0,

    /// <summary><c>Rev 1</c>, <c>Rev 10</c>. Ordered numerically, never as strings.</summary>
    Numeric,

    /// <summary><c>Rev A</c>, <c>Rev B</c>. A separate scheme, never compared against numeric.</summary>
    Alphabetic,
}

/// <summary>
/// A parsed revision. Three traps are encoded here rather than left to callers:
/// an absent tag is Rev 0 and therefore the OLDEST (the likeliest policy-engine bug);
/// <c>Rev 10</c> is newer than <c>Rev 9</c> and must not be string-sorted; and numeric and
/// alphabetic revisions are separate schemes that are never ranked against one another.
/// </summary>
public readonly record struct RevisionValue(RevisionScheme Scheme, int Number, char Letter)
{
    /// <summary>The absent revision: Rev 0, the original, the oldest.</summary>
    public static RevisionValue Absent => new(RevisionScheme.Absent, 0, '\0');

    /// <summary>Builds a numeric revision.</summary>
    public static RevisionValue Numeric(int number) => new(RevisionScheme.Numeric, number, '\0');

    /// <summary>Builds an alphabetic revision.</summary>
    public static RevisionValue Alphabetic(char letter) => new(RevisionScheme.Alphabetic, 0, char.ToUpperInvariant(letter));

    /// <summary>
    /// Orders two revisions. Returns <c>false</c> when the two belong to different schemes and
    /// are therefore incomparable — the caller must flag rather than guess. An absent revision
    /// is comparable against either scheme, because Rev 0 precedes everything.
    /// </summary>
    public bool TryCompareTo(RevisionValue other, out int comparison)
    {
        if (Scheme == RevisionScheme.Absent || other.Scheme == RevisionScheme.Absent)
        {
            // Absent is Rev 0: older than any tagged revision, equal only to another absent.
            comparison = (Scheme == RevisionScheme.Absent ? 0 : 1) - (other.Scheme == RevisionScheme.Absent ? 0 : 1);
            return true;
        }

        if (Scheme != other.Scheme)
        {
            comparison = 0;
            return false;
        }

        comparison = Scheme == RevisionScheme.Numeric
            ? Number.CompareTo(other.Number)
            : Letter.CompareTo(other.Letter);
        return true;
    }

    /// <summary>Renders the canonical token text, or an empty string when absent.</summary>
    public override string ToString() => Scheme switch
    {
        RevisionScheme.Numeric => "Rev " + Number.ToString(CultureInfo.InvariantCulture),
        RevisionScheme.Alphabetic => "Rev " + Letter,
        _ => string.Empty,
    };
}
