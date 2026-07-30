namespace ARK.Core.Systems;

/// <summary>
/// Registry of known system profiles. Ported from v1, which is PlayStation-only; v2's cartridge
/// systems (N64/SNES/NES) are defined in a later phase, not here.
/// </summary>
public static class SystemProfiles
{
    private static readonly IReadOnlyList<SystemProfile> ProfileList =
    [
        new("psx", "Sony PlayStation", "PS1 multi-track + PBP/CHD orchestration")
    ];

    /// <summary>All known profiles.</summary>
    public static IReadOnlyList<SystemProfile> All => ProfileList;

    /// <summary>The default profile used when no system is selected or the code is unknown.</summary>
    public static SystemProfile Default => ProfileList[0];

    /// <summary>Resolves a profile by code, falling back to <see cref="Default"/>.</summary>
    public static SystemProfile Resolve(string? code) =>
        ProfileList.FirstOrDefault(profile => profile.Code.Equals(code ?? string.Empty, StringComparison.OrdinalIgnoreCase))
        ?? Default;
}
