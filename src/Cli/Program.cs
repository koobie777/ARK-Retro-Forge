using ARK.Cli.Infrastructure;
using ARK.Cli.Commands.PSX;
using ARK.Core.Tools;
using ARK.Core.Hashing;

namespace ARK.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        PrintBanner();

        if (args.Length == 0)
        {
            PrintHelp();
            return (int)ExitCode.OK;
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "doctor" => await RunDoctorAsync(args),
                "scan" => await RunScanAsync(args),
                "verify" => await RunVerifyAsync(args),
                "rename" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await RenamePsxCommand.RunAsync(args),
                "convert" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await ConvertPsxCommand.RunAsync(args),
                "duplicates" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await DuplicatesPsxCommand.RunAsync(args),
                "dupes" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await DuplicatesPsxCommand.RunAsync(args),
                "--help" or "-h" or "help" => ShowHelp(),
                "--version" or "-v" => ShowVersion(),
                _ => ShowUnknownCommand(command)
            };
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nOperation cancelled by user.");
            return (int)ExitCode.UserCancelled;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n☄️ [IMPACT] | Component: {command} | Context: {ex.Message}");
            return (int)ExitCode.GeneralError;
        }
    }

    private static async Task<int> RunDoctorAsync(string[] args)
    {
        var json = args.Contains("--json");
        
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

            Console.WriteLine("\n✨ All tools found and ready");
            Console.WriteLine("\n💡 Next step: Run 'scan --root <path>' to discover ROMs");
        }

        await Task.CompletedTask;
        return (int)ExitCode.OK;
    }

    private static async Task<int> RunScanAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrEmpty(root))
        {
            Console.WriteLine("☄️ [IMPACT] | Component: scan | Context: Missing --root argument | Fix: Specify --root <path>");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            Console.WriteLine($"☄️ [IMPACT] | Component: scan | Context: Directory not found: {root} | Fix: Verify the --root path exists");
            return (int)ExitCode.InvalidArgs;
        }

        Console.WriteLine($"🛰️ [SCAN] Scanning: {root}");
        Console.WriteLine();

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

        var files = new List<string>();
        var startTime = DateTime.UtcNow;

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (supportedExtensions.Contains(Path.GetExtension(file)))
            {
                files.Add(file);
            }
        }

        var duration = DateTime.UtcNow - startTime;

        Console.WriteLine($"✨ [DOCKED] Scan complete");
        Console.WriteLine($"  Files found: {files.Count}");
        Console.WriteLine($"  Duration: {duration.TotalSeconds:F2}s");
        Console.WriteLine($"\n➡️ Next step: verify --root {root}");

        await Task.CompletedTask;
        return (int)ExitCode.OK;
    }

    private static async Task<int> RunVerifyAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrEmpty(root))
        {
            Console.WriteLine("☄️ [IMPACT] | Component: verify | Context: Missing --root argument | Fix: Specify --root <path>");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            Console.WriteLine($"☄️ [IMPACT] | Component: verify | Context: Directory not found: {root} | Fix: Verify the --root path exists");
            return (int)ExitCode.InvalidArgs;
        }

        Console.WriteLine($"🛰️ [VERIFY] Hashing files in: {root}");
        Console.WriteLine();

        var hasher = new FileHasher();
        var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".bin", ".iso", ".chd", ".cso" };
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("⚠️ [ANOMALY] No files found to verify");
            return (int)ExitCode.OK;
        }

        var processed = 0;
        var totalBytes = 0L;
        var startTime = DateTime.UtcNow;

        foreach (var file in files)
        {
            processed++;
            Console.Write($"\r🔥 [BURN] [{processed}/{files.Count}] {Path.GetFileName(file)}".PadRight(80));

            var result = await hasher.ComputeHashesAsync(file);
            totalBytes += result.FileSize;
        }

        Console.WriteLine();
        var duration = DateTime.UtcNow - startTime;
        var throughputMBps = totalBytes / 1024.0 / 1024.0 / Math.Max(duration.TotalSeconds, 0.001);

        Console.WriteLine($"\n✨ [DOCKED] Verification complete");
        Console.WriteLine($"  Files processed: {processed}");
        Console.WriteLine($"  Total size: {totalBytes / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"  Duration: {duration.TotalSeconds:F2}s");
        Console.WriteLine($"  Throughput: {throughputMBps:F2} MB/s");
        Console.WriteLine($"\n➡️ Next step: Hashes computed successfully");

        return (int)ExitCode.OK;
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static int ShowHelp()
    {
        PrintHelp();
        return (int)ExitCode.OK;
    }

    private static int ShowVersion()
    {
        Console.WriteLine($"ARK-Retro-Forge v{GetVersion()}");
        return (int)ExitCode.OK;
    }

    private static int ShowUnknownCommand(string command)
    {
        Console.WriteLine($"☄️ [IMPACT] Unknown command: {command}");
        Console.WriteLine();
        PrintHelp();
        return (int)ExitCode.InvalidArgs;
    }

    private static void PrintBanner()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("  🚀 ARK-Retro-Forge v{0}", GetVersion());
        Console.WriteLine("  Spaceflight Toolchain — Portable • Deterministic");
        Console.WriteLine("  No ROMs/BIOS/Keys included • User-supplied tools");
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: ark-retro-forge <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  doctor              Check for missing external tools and validate environment");
        Console.WriteLine("    --json            Output results in JSON format");
        Console.WriteLine();
        Console.WriteLine("  scan                Scan directories for ROM files");
        Console.WriteLine("    --root <path>     Root directory to scan (required)");
        Console.WriteLine();
        Console.WriteLine("  verify              Verify ROM integrity with hash checking");
        Console.WriteLine("    --root <path>     Root directory to verify (required)");
        Console.WriteLine();
        Console.WriteLine("  rename psx          Rename PSX files to standard format");
        Console.WriteLine("    --root <path>     Root directory (required)");
        Console.WriteLine("    --recursive       Scan subdirectories");
        Console.WriteLine("    --apply           Apply rename operations (default is dry-run)");
        Console.WriteLine("    --verbose         Show full paths");
        Console.WriteLine("    --debug           Show debug information");
        Console.WriteLine("    --playlists <mode> Playlist handling: create (default), update, or off");
        Console.WriteLine();
        Console.WriteLine("  convert psx         Convert PSX CUE files to CHD format");
        Console.WriteLine("    --root <path>     Root directory (required)");
        Console.WriteLine("    --recursive       Scan subdirectories");
        Console.WriteLine("    --apply           Apply conversions (default is dry-run)");
        Console.WriteLine("    --delete-source   Delete source files after conversion (requires --apply)");
        Console.WriteLine("    --rebuild         Force reconversion even if CHD exists");
        Console.WriteLine("    --playlist-mode <mode> Playlist handling: chd (default), bin, or off");
        Console.WriteLine();
        Console.WriteLine("  duplicates psx      Detect duplicate PSX disc images");
        Console.WriteLine("  dupes psx           (alias for duplicates psx)");
        Console.WriteLine("    --root <path>     Root directory (required)");
        Console.WriteLine("    --recursive       Scan subdirectories");
        Console.WriteLine("    --hash <algo>     Hash algorithm: SHA1 (default), MD5");
        Console.WriteLine("    --json            Write detailed report to logs/ directory");
        Console.WriteLine();
        Console.WriteLine("  --help, -h          Show this help message");
        Console.WriteLine("  --version, -v       Show version information");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ark-retro-forge doctor");
        Console.WriteLine("  ark-retro-forge scan --root C:\\ROMs");
        Console.WriteLine("  ark-retro-forge verify --root C:\\ROMs");
        Console.WriteLine("  ark-retro-forge rename psx --root C:\\PSX --recursive");
        Console.WriteLine("  ark-retro-forge rename psx --root C:\\PSX --recursive --apply");
        Console.WriteLine("  ark-retro-forge convert psx --root C:\\PSX --recursive --apply");
        Console.WriteLine("  ark-retro-forge duplicates psx --root C:\\PSX --recursive --json");
        Console.WriteLine();
        Console.WriteLine("💡 Run 'doctor' first to check your environment");
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
