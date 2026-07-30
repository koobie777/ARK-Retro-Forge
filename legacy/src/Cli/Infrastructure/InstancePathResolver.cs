using ARK.Core;

namespace ARK.Cli.Infrastructure;

internal static class InstancePathResolver
{
    public static string CurrentInstance => ArkEnvironment.CurrentInstance;

    public static void SetInstanceName(string? instanceName)
        => ArkEnvironment.SetInstanceName(instanceName);

    public static string GetInstanceRoot()
        => ArkEnvironment.GetInstanceRoot();

    public static string Sanitize(string? value)
        => ArkEnvironment.Sanitize(value);
}
