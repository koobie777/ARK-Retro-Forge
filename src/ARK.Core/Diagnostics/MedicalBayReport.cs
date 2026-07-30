using ARK.Core.Tools;

namespace ARK.Core.Diagnostics;

/// <summary>
/// The single source of truth for a Medical Bay run. The human render and the <c>--json</c> output
/// are two serializations of this one object; neither may carry a field the other cannot see.
/// It reports status only and deliberately exposes no global pass/fail verdict — whether a given
/// tool or DAT absence matters is the calling operation's judgement (Phase 1 brief, defect 2).
/// </summary>
public sealed record MedicalBayReport
{
    /// <summary>Active instance name.</summary>
    public required string InstanceName { get; init; }

    /// <summary>Configured ROM root, or null when unset.</summary>
    public required string? RomRoot { get; init; }

    /// <summary>True when a ROM root is configured.</summary>
    public required bool RomRootSet { get; init; }

    /// <summary>How the active system code resolves (recognized, unrecognized, or unset).</summary>
    public required ActiveSystemStatus ActiveSystem { get; init; }

    /// <summary>Per-tool status for every known tool.</summary>
    public required IReadOnlyList<ToolCheckResult> Tools { get; init; }

    /// <summary>Per-system DAT status.</summary>
    public required IReadOnlyList<DatStatus> DatCatalogs { get; init; }
}
