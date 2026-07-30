using System.CommandLine;
using ARK.Cli.Commands;
using ARK.Core.Diagnostics;
using ARK.Core.Instances;
using ARK.Core.Tools;
using Serilog;
using Spectre.Console;

// Composition root. Serilog is wired here from the first commit so every command inherits it.
// Core composition (services, resolvers) lives here; command files stay presentation-only.
var paths = new InstancePaths();
Log.Logger = ArkLog.Create(paths);

try
{
    var root = new RootCommand(
        "ARK Retro Forge — universal ROM management: identify, verify, dedupe, curate, rename, and organize.");
    root.Add(MedicalBayCommand.Build(AnsiConsole.Console, BuildMedicalBayReport));
    return root.Parse(args).Invoke();
}
finally
{
    Log.CloseAndFlush();
}

MedicalBayReport BuildMedicalBayReport()
{
    var tools = new ToolManager(new ToolLocator(paths.ToolsRoot));
    var dat = new DatStatusReporter(paths);
    return new MedicalBayService(paths, tools, dat).Generate(MedicalBayContext.Empty);
}
