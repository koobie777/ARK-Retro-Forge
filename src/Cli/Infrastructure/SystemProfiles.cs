namespace ARK.Cli.Infrastructure;

internal sealed record SystemProfile(string Code, string Name, string Description);

internal static class SystemProfiles
{
    private static readonly List<SystemProfile> Profiles =
    [
        new("psx", "Sony PlayStation", "PS1 multi-track + PBP/CHD orchestration")
    ];

    public static IReadOnlyList<SystemProfile> All => Profiles;

    public static SystemProfile Default => Profiles[0];

    public static SystemProfile Resolve(string? code)
        => Profiles.FirstOrDefault(p => p.Code.Equals(code ?? string.Empty, StringComparison.OrdinalIgnoreCase))
           ?? Default;
}
