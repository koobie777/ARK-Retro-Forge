namespace ARK.Core.Diagnostics;

/// <summary>
/// Runtime context Medical Bay reports against: the current ROM root and selected system code.
/// These are session/runtime state supplied by the caller. Phase 1 has no session store yet
/// (v1's file-writing, PSX-specific session manager is deliberately not ported into Core), so the
/// CLI passes <see cref="Empty"/>.
/// </summary>
public sealed record MedicalBayContext(string? RomRoot, string? SystemCode)
{
    /// <summary>No ROM root and no selected system — the Phase 1 default.</summary>
    public static MedicalBayContext Empty { get; } = new(RomRoot: null, SystemCode: null);
}
