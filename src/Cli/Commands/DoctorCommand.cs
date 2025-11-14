using System.CommandLine;
using ARK.Cli.Infrastructure;
using ARK.Core.Tools;

namespace ARK.Cli.Commands;

/// <summary>
/// Doctor command - checks for missing external tools and validates environment
/// </summary>
public static class DoctorCommand
{
    public static Command Create()
    {
        var command = new Command("doctor", "Check for missing external tools and validate environment");
        
        var jsonOption = new Option<bool>("--json", "Output results in JSON format");
        command.AddOption(jsonOption);

        command.SetHandler(async (bool json, bool verbose) =>
        {
            await ExecuteAsync(json, verbose);
        }, jsonOption, new Option<bool>("--verbose"));

        return command;
    }

    private static async Task<int> ExecuteAsync(bool json, bool verbose)
    {
        var toolManager = new ToolManager();
        var results = toolManager.CheckAllTools().ToList();

        if (json)
        {
            var jsonOutput = System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            Console.WriteLine(jsonOutput);
        }
        else
        {
            Console.WriteLine("External Tools Check");
            Console.WriteLine("══════════════════════════════════════════════════════════════════════");
            Console.WriteLine($"{"Tool",-15} {"Found",-8} {"Version",-15} {"Min Ver",-10} {"Path",-40}");
            Console.WriteLine("──────────────────────────────────────────────────────────────────────");

            foreach (var result in results)
            {
                var found = result.IsFound ? "✓" : "✗";
                var version = result.Version ?? "N/A";
                var minVersion = result.MinimumVersion ?? "-";
                var path = result.Path ?? result.ErrorMessage ?? "Not found";
                
                if (path.Length > 40)
                {
                    path = "..." + path[^37..];
                }

                Console.WriteLine($"{result.Name,-15} {found,-8} {version,-15} {minVersion,-10} {path,-40}");
            }

            Console.WriteLine("══════════════════════════════════════════════════════════════════════");
            
            var missingRequired = results.Where(r => !r.IsFound).ToList();
            var foundCount = results.Count(r => r.IsFound);
            
            Console.WriteLine($"\nSummary: {foundCount}/{results.Count} tools found");
            
            if (missingRequired.Any())
            {
                Console.WriteLine("\n⚠️  Missing required tools:");
                foreach (var missing in missingRequired)
                {
                    Console.WriteLine($"   - {missing.Name}: {missing.ErrorMessage}");
                }
                Console.WriteLine("\n💡 Next step: Download missing tools and place them in .\\tools\\ directory");
                return (int)ExitCode.ToolMissing;
            }

            Console.WriteLine("\n✓ All tools found and ready");
            Console.WriteLine("\n💡 Next step: Run 'scan --root <path>' to discover ROMs");
        }

        await Task.CompletedTask;
        return (int)ExitCode.OK;
    }
}
