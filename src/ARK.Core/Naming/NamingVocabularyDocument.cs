namespace ARK.Core.Naming;

/// <summary>
/// One <c>config/naming/*.json</c> file, as data. Every file deserializes into this same shape
/// and contributes whichever fields it carries, so a new vocabulary file — or a new term in an
/// existing one — is picked up with no code change.
/// </summary>
/// <remarks>
/// This is a plain data carrier. It performs no I/O; reading the files is the loader's job,
/// which keeps <c>Core/Naming</c> free of filesystem access and therefore unit-testable with
/// nothing attached.
/// </remarks>
public sealed class NamingVocabularyDocument
{
    /// <summary>Category these <see cref="Terms"/> belong to, matching a <see cref="TokenCategory"/> name.</summary>
    public string? Category { get; set; }

    /// <summary>Closed-vocabulary terms for <see cref="Category"/>.</summary>
    public string[]? Terms { get; set; }

    /// <summary>Prefixes that may carry an ordinal, e.g. <c>Beta</c> matching <c>Beta 12</c>.</summary>
    public string[]? OrdinalPrefixes { get; set; }

    /// <summary>When true, a comma list whose first element is a term matches, e.g. <c>(Kiosk, E3 2003)</c>.</summary>
    public bool? CompoundHeads { get; set; }

    /// <summary>Bracket flags dropped from canonical output.</summary>
    public string[]? Strip { get; set; }

    /// <summary>Articles that are moved to the end of the first title segment on format.</summary>
    public string[]? InvertOnFormat { get; set; }

    /// <summary>Articles used only to build a grouping key, never to rewrite a title.</summary>
    public string[]? GroupingOnly { get; set; }

    /// <summary>Emission ranks. Categories sharing a group keep their input order under a stable sort.</summary>
    public string[][]? RankGroups { get; set; }

    /// <summary>Categories that may be lifted out of a title when they appear before the region boundary.</summary>
    public string[]? PullBeforeBoundary { get; set; }

    /// <summary>Words that mark an open-vocabulary compilation token.</summary>
    public string[]? CompilationHints { get; set; }
}
