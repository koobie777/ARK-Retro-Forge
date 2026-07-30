using System.CommandLine;
using ARK.Cli.Rendering;
using ARK.Core.Execution;
using ARK.Core.Settings;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// Wires the <c>config</c> verb (<c>show</c>, <c>set rom-root</c>, <c>set system</c>). Presentation
/// and wiring only: settings mutation and plan-building live in <see cref="SettingsStore"/> (Core),
/// and the write plan is handed to the <see cref="Executor"/> — the command makes no decision from
/// settings contents.
/// </summary>
public static class ConfigCommand
{
    /// <summary>Builds the command tree over the given store, executor, and console.</summary>
    public static Command Build(IAnsiConsole console, SettingsStore store, Executor executor)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(executor);

        var config = new Command("config", "View and change ARK settings.");
        config.Add(BuildShow(console, store));
        config.Add(BuildSet(console, store, executor));
        return config;
    }

    private static Command BuildShow(IAnsiConsole console, SettingsStore store)
    {
        var show = new Command("show", "Show the current settings.");
        show.SetAction(_ =>
        {
            ConfigRenderer.RenderSettings(console, store.Read());
            return 0;
        });
        return show;
    }

    private static Command BuildSet(IAnsiConsole console, SettingsStore store, Executor executor)
    {
        var set = new Command("set", "Change a setting.");
        set.Add(BuildSetField(console, executor, "rom-root", "path", "Filesystem path to the ROM root.", store.PlanSetRomRoot, "ROM root"));
        set.Add(BuildSetField(console, executor, "system", "code", "System code (e.g. n64, snes, nes).", store.PlanSetActiveSystem, "Active system"));
        return set;
    }

    private static Command BuildSetField(
        IAnsiConsole console,
        Executor executor,
        string commandName,
        string argumentName,
        string argumentDescription,
        Func<string, Plan> planFactory,
        string label)
    {
        var argument = new Argument<string>(argumentName) { Description = argumentDescription };
        var command = new Command(commandName, $"Set the {label.ToLowerInvariant()}.");
        command.Add(argument);
        command.SetAction(parseResult =>
        {
            var value = parseResult.GetValue(argument)!;
            executor.Execute(planFactory(value), apply: true);
            ConfigRenderer.RenderSet(console, label, value);
            return 0;
        });
        return command;
    }
}
