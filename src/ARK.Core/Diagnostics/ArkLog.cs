using ARK.Core.Instances;
using Serilog;
using Serilog.Core;

namespace ARK.Core.Diagnostics;

/// <summary>
/// Central Serilog configuration. Core services log through <see cref="ILogger"/> and never
/// write to the console directly; this wires a console sink plus a rolling daily file sink
/// under the instance's <c>logs/</c> directory.
/// </summary>
public static class ArkLog
{
    /// <summary>
    /// Builds a logger for the given instance. The caller owns the returned logger and is
    /// responsible for disposing it (or flushing via <c>Log.CloseAndFlush()</c> when it is
    /// assigned to <see cref="Log.Logger"/>).
    /// </summary>
    public static Logger Create(InstancePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Instance", paths.InstanceName)
            .WriteTo.Console()
            .WriteTo.File(paths.LogFileTemplate, rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }
}
