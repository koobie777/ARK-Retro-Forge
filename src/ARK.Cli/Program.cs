using System.CommandLine;
using ARK.Cli.Commands;
using ARK.Core.Diagnostics;
using ARK.Core.Execution;
using ARK.Core.Instances;
using ARK.Core.Settings;
using ARK.Core.Tools;
using Serilog;
using Spectre.Console;

// Composition root. Serilog is wired here from the first commit so every command inherits it.
// Core composition (services, resolvers) lives here; command files stay presentation-only.
var paths = new InstancePaths();
Log.Logger = ArkLog.Create(paths);
var settingsStore = new SettingsStore(paths);
var executor = new Executor(paths);

try
{
    var root = new RootCommand(
        "ARK Retro Forge — universal ROM management: identify, verify, dedupe, curate, rename, and organize.");
    root.Add(MedicalBayCommand.Build(AnsiConsole.Console, BuildMedicalBayReport));
    root.Add(ConfigCommand.Build(AnsiConsole.Console, settingsStore, executor));

    // Handle exceptions here (see catch below) rather than letting System.CommandLine dump a raw
    // stack trace for a user-fixable condition like a malformed settings file.
    var invocation = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
    return root.Parse(args).Invoke(invocation);
}
catch (SettingsFormatException ex)
{
    // Fail loud, but cleanly: a malformed settings file is a user-fixable error, not a crash.
    AnsiConsole.MarkupLineInterpolated($"[red]Settings error:[/] {ex.Message}");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

MedicalBayReport BuildMedicalBayReport()
{
    var settings = settingsStore.Read();
    var tools = new ToolManager(new ToolLocator(paths.ToolsRoot));
    var dat = new DatStatusReporter(paths);
    return new MedicalBayService(paths, tools, dat)
        .Generate(new MedicalBayContext(settings.RomRoot, settings.ActiveSystem));
}
