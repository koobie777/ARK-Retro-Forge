namespace ARK.Core.Systems;

/// <summary>
/// A DAT name resolved to a system and, where the name carried one, a declared format qualifier.
/// </summary>
/// <remarks>
/// <para>
/// The model is <b>one system, N DAT variants keyed by qualifier</b> — not one system, one DAT.
/// <c>(Headered)</c> and <c>(Headerless)</c> are two distinct hash sets covering the same ~4,500
/// games, and a headered ROM will never match a headerless DAT. N64 carries the same split across
/// byte orders.
/// </para>
/// <para>
/// This is why an alias list is the wrong fix for qualified DAT names: adding the qualified
/// strings as aliases would make them resolve and then silently merge incompatible hash sets
/// under one system, which is worse than not matching at all.
/// </para>
/// </remarks>
/// <param name="System">The resolved system.</param>
/// <param name="Qualifier">
/// The declared format qualifier, or null when the DAT name carried none. Null is a real value
/// here — <c>Nintendo - Nintendo Entertainment System</c> exists alongside its two qualified
/// variants and is a third, distinct set.
/// </param>
public sealed record SystemMatch(SystemDefinition System, string? Qualifier)
{
    /// <summary>The system's code.</summary>
    public string Code => System.Code;

    /// <summary>Stable label for the variant, e.g. <c>nes (Headered)</c>.</summary>
    public override string ToString() => Qualifier is null ? System.Code : $"{System.Code} ({Qualifier})";
}
