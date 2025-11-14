using System.CommandLine;
using ARK.Cli.Commands;
using ARK.Cli.Infrastructure;

namespace ARK.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        PrintBanner();

        var rootCommand = new RootCommand("ARK-Retro-Forge - Portable .NET 8 ROM toolkit");

        // Global options
        var dryRunOption = new Option<bool>(
            "--dry-run",
            getDefaultValue: () => true,
            description: "Preview changes without applying them (default: true)");
        var applyOption = new Option<bool>(
            "--apply",
            getDefaultValue: () => false,
            description: "Apply changes to files");
        var forceOption = new Option<bool>(
            "--force",
            getDefaultValue: () => false,
            description: "Force operation even with warnings (requires --apply)");
        var workersOption = new Option<int>(
            "--workers",
            getDefaultValue: () => Environment.ProcessorCount,
            description: "Number of parallel workers");
        var verboseOption = new Option<bool>(
            "--verbose",
            getDefaultValue: () => false,
            description: "Verbose output");
        var reportOption = new Option<string?>(
            "--report",
            description: "Directory for reports");
        var themeOption = new Option<string>(
            "--theme",
            getDefaultValue: () => "dark",
            description: "Color theme (dark|light)");

        rootCommand.AddGlobalOption(dryRunOption);
        rootCommand.AddGlobalOption(applyOption);
        rootCommand.AddGlobalOption(forceOption);
        rootCommand.AddGlobalOption(workersOption);
        rootCommand.AddGlobalOption(verboseOption);
        rootCommand.AddGlobalOption(reportOption);
        rootCommand.AddGlobalOption(themeOption);

        // Add commands
        rootCommand.Add(ScanCommand.Create());
        rootCommand.Add(VerifyCommand.Create());
        rootCommand.Add(RenameCommand.Create());
        rootCommand.Add(DoctorCommand.Create());

        try
        {
            return await rootCommand.InvokeAsync(args);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nOperation cancelled by user.");
            return (int)ExitCode.UserCancelled;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nUnhandled error: {ex.Message}");
            if (args.Contains("--verbose"))
            {
                Console.WriteLine(ex.StackTrace);
            }
            return (int)ExitCode.GeneralError;
        }
    }

    private static void PrintBanner()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("  ARK-Retro-Forge v{0}", GetVersion());
        Console.WriteLine("  Portable .NET 8 ROM Manager - No ROMs/BIOS included");
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine();
    }

    private static string GetVersion()
    {
        var version = typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "0.1.0-dev";
        return version;
    }
}
