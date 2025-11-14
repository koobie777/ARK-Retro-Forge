using System.CommandLine;
using ARK.Cli.Infrastructure;

namespace ARK.Cli.Commands;

/// <summary>
/// Scan command - discover ROM files in a directory
/// </summary>
public static class ScanCommand
{
    public static Command Create()
    {
        var command = new Command("scan", "Scan directories for ROM files");
        
        var rootOption = new Option<string>("--root", "Root directory to scan") { IsRequired = true };
        command.AddOption(rootOption);

        command.SetHandler(async (string root, bool dryRun, bool apply, int workers, bool verbose) =>
        {
            var exitCode = await ExecuteAsync(root, dryRun, apply, workers, verbose);
            Environment.Exit(exitCode);
        }, 
        rootOption, 
        new Option<bool>("--dry-run"),
        new Option<bool>("--apply"),
        new Option<int>("--workers"),
        new Option<bool>("--verbose"));

        return command;
    }

    private static async Task<int> ExecuteAsync(string root, bool dryRun, bool apply, int workers, bool verbose)
    {
        if (!Directory.Exists(root))
        {
            var error = new ErrorEvent
            {
                Code = "InvalidPath",
                Component = "scan",
                Context = $"Directory not found: {root}",
                Suggestion = "Verify the --root path exists"
            };
            Console.WriteLine(error);
            return (int)ExitCode.InvalidArgs;
        }

        Console.WriteLine($"Scanning: {root}");
        Console.WriteLine($"Workers: {workers}");
        Console.WriteLine($"Mode: {(dryRun ? "DRY-RUN (preview only)" : "APPLY (will make changes)")}");
        Console.WriteLine();

        var startTime = DateTime.UtcNow;
        var files = new List<string>();
        var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bin", ".cue", ".iso", ".chd", ".cso", ".pbp",
            ".z64", ".n64", ".v64",
            ".gb", ".gbc", ".gba",
            ".nes", ".smc", ".sfc",
            ".gcm", ".wbfs", ".rvz", ".wux",
            ".xci", ".nsp", ".nsz",
            ".gdi", ".cdi"
        };

        try
        {
            var allFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories);
            
            foreach (var file in allFiles)
            {
                var ext = Path.GetExtension(file);
                if (supportedExtensions.Contains(ext))
                {
                    files.Add(file);
                    if (verbose)
                    {
                        Console.WriteLine($"Found: {Path.GetFileName(file)}");
                    }
                }
            }

            var duration = DateTime.UtcNow - startTime;
            
            Console.WriteLine($"\n✓ Scan complete");
            Console.WriteLine($"  Files found: {files.Count}");
            Console.WriteLine($"  Duration: {duration.TotalSeconds:F2}s");
            Console.WriteLine($"\n💡 Next step: Run 'verify --root {root}' to verify file integrity");

            return (int)ExitCode.OK;
        }
        catch (UnauthorizedAccessException ex)
        {
            var error = new ErrorEvent
            {
                Code = "AccessDenied",
                Component = "scan",
                Context = ex.Message,
                Suggestion = "Check file permissions"
            };
            Console.WriteLine(error);
            return (int)ExitCode.IOError;
        }
        catch (Exception ex)
        {
            var error = new ErrorEvent
            {
                Code = "ScanError",
                Component = "scan",
                Context = ex.Message,
                Suggestion = "Check the path and try again"
            };
            Console.WriteLine(error);
            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
            return (int)ExitCode.IOError;
        }
    }
}
