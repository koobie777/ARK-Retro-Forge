using System.CommandLine;
using ARK.Core.Diagnostics;
using ARK.Core.Instances;
using Serilog;

// Composition root. Phase 0 exposes only --version and --help; all logic lives in Core.
// Serilog is wired here from the first commit so every later command inherits it.
var paths = new InstancePaths();
Log.Logger = ArkLog.Create(paths);

try
{
    var root = new RootCommand(
        "ARK Retro Forge — universal ROM management: identify, verify, dedupe, curate, rename, and organize.");
    return root.Parse(args).Invoke();
}
finally
{
    Log.CloseAndFlush();
}
