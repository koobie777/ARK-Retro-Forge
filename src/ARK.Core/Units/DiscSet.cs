using System.Text;
using ARK.Core.Naming;

namespace ARK.Core.Units;

/// <summary>
/// Several disc units that belong to one game.
/// </summary>
/// <remarks>
/// <para>
/// <b>A set is a grouping over units, never a unit itself.</b> Disc 1 and Disc 2 stay two separate
/// units with two separate hashes and two separate DAT entries; the set only records that they
/// travel together. Merging them into one unit is the mistake that made v1 read a multi-disc game
/// as a multi-track disc.
/// </para>
/// <para>
/// <b>Disc number remains an identity axis</b> (Phase 8). Membership in a set is not variance:
/// <c>(Disc 1)</c> and <c>(Disc 2)</c> can never be ranked against each other or collapsed, and the
/// curation engine is not involved here at all.
/// </para>
/// </remarks>
/// <param name="Key">Grouping key — the units' shared identity with the disc token removed.</param>
/// <param name="Title">Display title of the game the discs belong to.</param>
/// <param name="Units">The discs, in the order they were resolved.</param>
public sealed record DiscSet(string Key, string Title, IReadOnlyList<GameUnit> Units)
{
    /// <summary>True when the set holds more than one disc.</summary>
    public bool IsMultiDisc => Units.Count > 1;

    /// <summary>Every file across every disc — what "move the set whole" means concretely.</summary>
    public IReadOnlyList<string> AllFiles =>
        Units.SelectMany(unit => unit.Files).Select(file => file.FullPath).ToArray();
}

/// <summary>
/// Groups resolved disc units into multi-disc sets.
/// </summary>
public static class DiscSetGrouper
{
    /// <summary>
    /// Groups units by their identity with the disc token removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed on <see cref="ParsedName"/> token sets minus the disc token — never on filename
    /// similarity, and never on a formatted string. Two names carrying the same tokens in different
    /// orders are both canonical, so a string comparison would split one set in two.
    /// </para>
    /// <para>
    /// Every other token still counts. <c>Game (USA) (Disc 1)</c> and <c>Game (Japan) (Disc 2)</c>
    /// are two different releases that happen to share a title, and they must not form a set.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<DiscSet> Group(IEnumerable<GameUnit> units, TokenVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(vocabulary);

        var sets = new List<DiscSet>();

        // A flagged name yields an empty key, and an empty key must never gather units together —
        // that would be a set assembled out of the units ARK specifically could not identify.
        foreach (var group in units
            .Where(unit => unit.Kind == GameUnitKind.Disc && unit.Name.IsTokenizable)
            .GroupBy(unit => KeyFor(unit.Name, vocabulary), StringComparer.Ordinal))
        {
            sets.Add(new DiscSet(group.Key, group.First().Name.Title, group.ToArray()));
        }

        return sets;
    }

    /// <summary>Identity of a disc unit's name with the disc token excluded.</summary>
    public static string KeyFor(ParsedName parsed, TokenVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(vocabulary);

        if (!parsed.IsTokenizable)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(vocabulary.NormalizeForGrouping(parsed.Title));

        foreach (var token in parsed.Tokens
            .Where(token => token.Category != TokenCategory.Disc)
            .Select(token => (int)token.Category + ":" + vocabulary.NormalizeForGrouping(token.Value))
            .OrderBy(text => text, StringComparer.Ordinal))
        {
            builder.Append('|').Append(token);
        }

        return builder.ToString();
    }
}
