namespace ARK.Core.Diagnostics;

/// <summary>
/// How the active system code resolves against the system registry. An unrecognized code is
/// reported as such — never substituted for a default (the Phase 1.5 defect this fixes was v1's
/// silent fallback to <c>psx</c>).
/// </summary>
public sealed record ActiveSystemStatus
{
    /// <summary>The configured system code, or null when none is set.</summary>
    public required string? Code { get; init; }

    /// <summary>True when <see cref="Code"/> resolves to a known system definition.</summary>
    public required bool Recognized { get; init; }

    /// <summary>Display name of the resolved system, or null when unset or unrecognized.</summary>
    public required string? DisplayName { get; init; }
}
