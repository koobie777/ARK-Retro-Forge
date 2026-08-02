namespace ARK.Core.Dat;

/// <summary>How a ROM-set directory was tied to a DAT.</summary>
public enum DatScopeSource
{
    /// <summary>The directory name is byte-identical to an indexed DAT name.</summary>
    DirectoryName = 0,

    /// <summary>The directory name resolved to a system and qualifier, which named the DAT.</summary>
    SystemQualifier,
}

/// <summary>
/// The single DAT a ROM-set directory's files are identified against.
/// </summary>
/// <remarks>
/// <para>
/// Identification is scoped to one DAT, never to the whole catalog. A real catalog holds 1.5M
/// entries across 334 DATs, and a catalog-wide name search finds matches that mean nothing: with
/// no Redump PlayStation DAT imported, 308 Redump disc images matched entries from
/// <c>Non-Redump - Sony - PlayStation</c> and <c>Sony - PlayStation (PS one Classics) (PSN)</c>
/// instead. Those share a title with the real release and are entirely different artifacts with
/// different hashes.
/// </para>
/// <para>
/// Left unfixed, verification would hash all 308 against the wrong entry, every one would fail,
/// and they would report as corrupt — false alarms on healthy files, which is the alarm fatigue
/// the whole verification model exists to avoid. A cross-DAT name collision is a guess wearing a
/// confident label.
/// </para>
/// </remarks>
/// <param name="DatName">The DAT identification is confined to.</param>
/// <param name="System">System code the DAT resolved to, or null when it never resolved.</param>
/// <param name="Qualifier">Format qualifier of that DAT variant, or null.</param>
/// <param name="Source">How the tie was made.</param>
public sealed record DatScope(string DatName, string? System, string? Qualifier, DatScopeSource Source)
{
    /// <summary>
    /// Human-readable label for the report. The qualifier is appended only when the DAT name does
    /// not already carry it, so <c>Nintendo - Nintendo Entertainment System (Headered)</c> does
    /// not render as <c>… (Headered) [Headered]</c>.
    /// </summary>
    public override string ToString() =>
        Qualifier is null || DatName.Contains(Qualifier, StringComparison.OrdinalIgnoreCase)
            ? DatName
            : $"{DatName} [{Qualifier}]";
}

/// <summary>
/// Ties a ROM-set directory to the one DAT its contents should be compared against, and supplies
/// an index over just that DAT.
/// </summary>
public interface IDatScopeResolver
{
    /// <summary>
    /// Resolves a directory name to a DAT, or null when none applies. Null is a real answer and
    /// must not be softened into a catalog-wide search: no matching DAT means the honest result is
    /// that nothing was identified.
    /// </summary>
    DatScope? Resolve(string directoryName);

    /// <summary>An index over that DAT's entries alone.</summary>
    DatNameIndex IndexFor(DatScope scope);
}
