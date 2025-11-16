using System.Text.RegularExpressions;
using ARK.Cli.Infrastructure;
using ARK.Cli.Commands.PSX;
using ARK.Cli.Commands.Archives;
using ARK.Core.Tools;
using ARK.Core.Hashing;
using ARK.Core.Database;
using Spectre.Console;

namespace ARK.Cli;

public class Program
{
    private static bool _menuDryRun = true;
    private static string? _rememberedRomRoot;
    private static string _instanceName = Environment.GetEnvironmentVariable("ARKRF_INSTANCE") ?? "default";
    public static async Task<int> Main(string[] args)
    {
        ParseGlobalArgs(ref args);
        PrintBanner();

        if (args.Length == 0)
        {
            if (!HasInteractiveConsole())
            {
                Console.WriteLine("Interactive menu requires a terminal. Run `ark-retro-forge --help` for usage.");
                return (int)ExitCode.InvalidArgs;
            }

            return await ShowMenuAsync();
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
                "merge" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await MergePsxCommand.RunAsync(args),
                "duplicates" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await DuplicatesPsxCommand.RunAsync(args),
                "dupes" when args.Length > 1 && args[1].Equals("psx", StringComparison.OrdinalIgnoreCase) => await DuplicatesPsxCommand.RunAsync(args),
                "extract" when args.Length > 1 && args[1].Equals("archives", StringComparison.OrdinalIgnoreCase) => await ExtractArchivesCommand.RunAsync(args),
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
            Console.WriteLine($"\nÎ“Ã¿Ã¤âˆ©â••Ã… [IMPACT] | Component: {command} | Context: {ex.Message}");
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
            Console.WriteLine("Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰");
            Console.WriteLine($"{"Tool",-15} {"Found",-8} {"Version",-15} {"Min Ver",-10} {"Path",-40}");
            Console.WriteLine("Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡Î“Ã¶Ã‡");

            foreach (var result in results)
            {
                var found = result.IsFound ? "Î“Â£Ã´" : "Î“Â£Ã¹";
                var version = result.Version ?? "N/A";
                var minVersion = result.MinimumVersion ?? "-";
                var path = result.Path ?? result.ErrorMessage ?? "Not found";
                
                if (path.Length > 40)
                {
                    path = "..." + path[^37..];
                }

                Console.WriteLine($"{result.Name,-15} {found,-8} {version,-15} {minVersion,-10} {path,-40}");
            }

            Console.WriteLine("Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰Î“Ã²Ã‰");
            
            var missingRequired = results.Where(r => !r.IsFound).ToList();
            var foundCount = results.Count(r => r.IsFound);
            
            Console.WriteLine($"\nSummary: {foundCount}/{results.Count} tools found");
            
            if (missingRequired.Any())
            {
                Console.WriteLine("\nÎ“ÃœÃ¡âˆ©â••Ã…  Missing required tools:");
                foreach (var missing in missingRequired)
                {
                    Console.WriteLine($"   - {missing.Name}: {missing.ErrorMessage}");
                }
                Console.WriteLine("\nâ‰¡Æ’Ã†Ã­ Next step: Download missing tools and place them in .\\tools\\ directory");
                return (int)ExitCode.ToolMissing;
            }

            Console.WriteLine("\nÎ“Â£Â¿ All tools found and ready");
            Console.WriteLine("\nâ‰¡Æ’Ã†Ã­ Next step: Run 'scan --root <path>' to discover ROMs");
        }

        await Task.CompletedTask;
        return (int)ExitCode.OK;
    }

    private static async Task<int> RunScanAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrEmpty(root))
        {
            Console.WriteLine("Î“Ã¿Ã¤âˆ©â••Ã… [IMPACT] | Component: scan | Context: Missing --root argument | Fix: Specify --root <path>");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            Console.WriteLine($"Î“Ã¿Ã¤âˆ©â••Ã… [IMPACT] | Component: scan | Context: Directory not found: {root} | Fix: Verify the --root path exists");
            return (int)ExitCode.InvalidArgs;
        }

        Console.WriteLine($"â‰¡Æ’Â¢â–‘âˆ©â••Ã… [SCAN] Scanning: {root}");
        Console.WriteLine();

        var dbPath = Path.Combine(GetInstanceRoot(), "db");
        await using var dbManager = new DatabaseManager(dbPath);
        await dbManager.InitializeAsync();
        var romRepository = new RomRepository(dbManager.GetConnection());
        var scanTimestamp = DateTime.UtcNow;

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
                var romRecord = BuildRomRecord(file, scanTimestamp);
                await romRepository.UpsertRomAsync(romRecord);
            }
        }

        var duration = DateTime.UtcNow - startTime;

        Console.WriteLine($"Î“Â£Â¿ [DOCKED] Scan complete");
        Console.WriteLine($"  Files found: {files.Count}");
        Console.WriteLine($"  Duration: {duration.TotalSeconds:F2}s");
        Console.WriteLine("  ROM cache updated");
        Console.WriteLine($"\nÎ“â‚§Ã­âˆ©â••Ã… Next step: verify --root {root}");

        return (int)ExitCode.OK;
    }

    private static async Task<int> RunVerifyAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrEmpty(root))
        {
            Console.WriteLine("Î“Ã¿Ã¤âˆ©â••Ã… [IMPACT] | Component: verify | Context: Missing --root argument | Fix: Specify --root <path>");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            Console.WriteLine($"Î“Ã¿Ã¤âˆ©â••Ã… [IMPACT] | Component: verify | Context: Directory not found: {root} | Fix: Verify the --root path exists");
            return (int)ExitCode.InvalidArgs;
        }

        Console.WriteLine($"â‰¡Æ’Â¢â–‘âˆ©â••Ã… [VERIFY] Hashing files in: {root}");
        Console.WriteLine();

        var dbPath = Path.Combine(GetInstanceRoot(), "db");
        await using var dbManager = new DatabaseManager(dbPath);
        await dbManager.InitializeAsync();
        var romRepository = new RomRepository(dbManager.GetConnection());

        var hasher = new FileHasher();
        var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".bin", ".iso", ".chd", ".cso" };
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("Î“ÃœÃ¡âˆ©â••Ã… [ANOMALY] No files found to verify");
            return (int)ExitCode.OK;
        }

        var processed = 0;
        var totalBytes = 0L;
        var startTime = DateTime.UtcNow;

        foreach (var file in files)
        {
            processed++;
            Console.Write($"\râ‰¡Æ’Ã¶Ã‘ [BURN] [{processed}/{files.Count}] {Path.GetFileName(file)}".PadRight(80));

            var metadataRecord = BuildRomRecord(file, DateTime.UtcNow);
            await romRepository.UpsertRomAsync(metadataRecord);

            var result = await hasher.ComputeHashesAsync(file);
            totalBytes += result.FileSize;
            await romRepository.UpdateHashesAsync(new RomHashUpdate(
                result.FilePath,
                result.Crc32,
                result.Md5,
                result.Sha1,
                result.FileSize,
                DateTime.UtcNow));
        }

        Console.WriteLine();
        var duration = DateTime.UtcNow - startTime;
        var throughputMBps = totalBytes / 1024.0 / 1024.0 / Math.Max(duration.TotalSeconds, 0.001);

        Console.WriteLine($"\nÎ“Â£Â¿ [DOCKED] Verification complete");
        Console.WriteLine($"  Files processed: {processed}");
        Console.WriteLine($"  Total size: {totalBytes / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"  Duration: {duration.TotalSeconds:F2}s");
        Console.WriteLine($"  Throughput: {throughputMBps:F2} MB/s");
        Console.WriteLine("  ROM cache updated with latest hash values");
        Console.WriteLine($"\nÎ“â‚§Ã­âˆ©â••Ã… Next step: Hashes computed successfully");

        return (int)ExitCode.OK;
    }

    private static async Task<int> ShowMenuAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            RenderMenuHeader();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold cyan]Select an operation[/] [grey](Use arrow keys and Enter)[/]")
                    .PageSize(10)
                    .HighlightStyle(Style.Parse("cyan bold"))
                    .AddChoices(
                        "Doctor - Tool Check",
                        "Scan - Directory Scan",
                        "Verify - Hash Integrity",
                        "Rename - PSX",
                        "Convert - PSX",
                        "Merge - PSX BIN Tracks",
                        "Duplicates - PSX",
                        "Extract - Archives",
                        "Set ROM Root Directory",
                        "Set Instance Profile",
                        _menuDryRun ? "Switch to APPLY mode" : "Switch to DRY-RUN mode",
                        "Exit"
                    ));

            switch (choice)
            {
                case "Doctor - Tool Check":
                    await RunDoctorAsync(new[] { "doctor" });
                    Pause();
                    break;
                case "Scan - Directory Scan":
                    await RunMenuScanAsync();
                    Pause();
                    break;
                case "Verify - Hash Integrity":
                    await RunMenuVerifyAsync();
                    Pause();
                    break;
                case "Rename - PSX":
                    await RunMenuRenamePsxAsync();
                    Pause();
                    break;
                case "Convert - PSX":
                    await RunMenuConvertPsxAsync();
                    Pause();
                    break;
                case "Merge - PSX BIN Tracks":
                    await RunMenuMergePsxAsync();
                    Pause();
                    break;
                case "Duplicates - PSX":
                    await RunMenuDuplicatesPsxAsync();
                    Pause();
                    break;
                case "Extract - Archives":
                    await RunMenuExtractArchivesAsync();
                    Pause();
                    break;
                case "Set ROM Root Directory":
                    SetRomRoot();
                    Pause();
                    break;
                case "Set Instance Profile":
                    SetInstanceProfile();
                    Pause();
                    break;
                case "Switch to APPLY mode":
                case "Switch to DRY-RUN mode":
                    _menuDryRun = !_menuDryRun;
                    var mode = _menuDryRun ? "[yellow]DRY-RUN[/]" : "[green]APPLY[/]";
                    AnsiConsole.MarkupLine($"\n[bold]Mode updated:[/] {mode}");
                    Pause();
                    break;
                case "Exit":
                    return (int)ExitCode.OK;
            }
        }
    }

    private static void RenderMenuHeader()
    {
        var modeText = _menuDryRun
            ? "[yellow]DRY-RUN (preview only)[/]"
            : "[green]APPLY (changes will be written)[/]";

        var rootText = string.IsNullOrWhiteSpace(_rememberedRomRoot)
            ? "[red]ROM root: Not set[/]"
            : $"[dim]ROM root:[/] {_rememberedRomRoot.EscapeMarkup()}";

        var instanceText = $"[dim]Instance:[/] {SanitizeInstanceName(_instanceName).EscapeMarkup()}";
        var tip = "[grey]Tip: Use arrow keys to navigate and Enter to run. Toggle DRY-RUN/APPLY, set ROM root, or switch instance any time.[/]";
        var version = GetVersion();

        var header = new Panel(
            new Markup($"[bold silver]ARK-Retro-Forge v{version}[/]\n[dim]Interactive Operations Menu[/]\n\nMode: {modeText}\n{rootText}\n{instanceText}\n\n{tip}")
        )
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("cyan"),
            Padding = new Padding(1, 1),
            Expand = true
        };

        AnsiConsole.Write(header);
        AnsiConsole.Write(new Rule("[dim]Operations[/]") { Style = Style.Parse("grey") });
        AnsiConsole.WriteLine();
    }

    private static async Task RunMenuScanAsync()
    {
        var root = EnsureRomRoot("Enter root folder to scan");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        await RunScanAsync(new[] { "scan", "--root", root });
    }

    private static async Task RunMenuVerifyAsync()
    {
        var root = EnsureRomRoot("Enter root folder to verify");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        await RunVerifyAsync(new[] { "verify", "--root", root });
    }

    private static async Task RunMenuRenamePsxAsync()
    {
        var root = EnsureRomRoot("Enter PSX root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var recursive = PromptYesNo("Scan recursively?", true);
        var apply = !_menuDryRun;
        var playlistMode = PromptForPlaylistMode();

        var args = new List<string> { "rename", "psx", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (apply)
        {
            args.Add("--apply");
        }
        if (!string.IsNullOrWhiteSpace(playlistMode) && !playlistMode.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--playlists");
            args.Add(playlistMode);
        }

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]Global dry-run mode: rename will preview changes only.[/]");
        }

        await RenamePsxCommand.RunAsync(args.ToArray());
    }

    private static async Task RunMenuConvertPsxAsync()
    {
        var root = EnsureRomRoot("Enter PSX root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var recursive = PromptYesNo("Scan recursively?", true);
        var apply = !_menuDryRun;
        var rebuild = PromptYesNo("Force rebuild existing CHDs?", false);
        var deleteSource = apply && PromptYesNo("Delete source files after conversion?", false);

        var args = new List<string> { "convert", "psx", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (apply)
        {
            args.Add("--apply");
        }
        if (rebuild)
        {
            args.Add("--rebuild");
        }
        if (deleteSource)
        {
            args.Add("--delete-source");
        }

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]Global dry-run mode: conversion plans will not create CHDs.[/]");
        }

        await ConvertPsxCommand.RunAsync(args.ToArray());
    }

    private static async Task RunMenuMergePsxAsync()
    {
        var root = EnsureRomRoot("Enter PSX root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var recursive = PromptYesNo("Scan recursively?", true);
        var apply = !_menuDryRun;
        var deleteSources = apply && PromptYesNo("Delete original BIN/CUE files after merge?", false);

        var args = new List<string> { "merge", "psx", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (apply)
        {
            args.Add("--apply");
        }
        if (deleteSources)
        {
            args.Add("--delete-source");
        }

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]Global dry-run mode: merge will only show planned operations.[/]");
        }

        await MergePsxCommand.RunAsync(args.ToArray());
    }

    private static async Task RunMenuDuplicatesPsxAsync()
    {
        var root = EnsureRomRoot("Enter PSX root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var recursive = PromptYesNo("Scan recursively?", true);
        var exportJson = PromptYesNo("Write JSON report to logs/?", false);

        var args = new List<string> { "duplicates", "psx", "--root", root };
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (exportJson)
        {
            args.Add("--json");
        }

        await DuplicatesPsxCommand.RunAsync(args.ToArray());
    }

    private static async Task RunMenuExtractArchivesAsync()
    {
        var root = EnsureRomRoot("Enter archive root folder");
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var output = PromptForOptional("Enter output directory (blank for root)");
        var recursive = PromptYesNo("Scan recursively?", true);
        var apply = !_menuDryRun;
        var deleteSource = apply && PromptYesNo("Delete source archives after extraction?", false);

        var args = new List<string> { "extract", "archives", "--root", root };
        if (!string.IsNullOrWhiteSpace(output))
        {
            args.Add("--output");
            args.Add(output);
        }
        if (recursive)
        {
            args.Add("--recursive");
        }
        if (apply)
        {
            args.Add("--apply");
        }
        if (deleteSource)
        {
            args.Add("--delete-source");
        }

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]Global dry-run mode: extraction will only preview work.[/]");
        }

        await ExtractArchivesCommand.RunAsync(args.ToArray());
    }

    private static string PromptForPath(string prompt)
    {
        var response = AnsiConsole.Prompt(
            new TextPrompt<string>($"[white]{prompt}[/]\n[bold]>[/] ")
                .AllowEmpty()
                .PromptStyle("green")
        );
        return response.Trim();
    }

    private static string PromptForOptional(string prompt)
    {
        var response = AnsiConsole.Prompt(
            new TextPrompt<string>($"[white]{prompt}[/]\n[bold]>[/] ")
                .AllowEmpty()
                .PromptStyle("green")
        );
        return response.Trim();
    }

    private static bool PromptYesNo(string prompt, bool defaultValue)
    {
        return AnsiConsole.Confirm($"[white]{prompt}[/]", defaultValue);
    }

    private static string PromptForPlaylistMode()
    {
        var response = AnsiConsole.Prompt(
            new TextPrompt<string>("Playlist mode (create/update/off) [create]:")
                .AllowEmpty()
                .PromptStyle("green")
        ).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(response) ? "create" : response;
    }

    private static void Pause()
    {
        AnsiConsole.MarkupLine("\n[grey]Press ENTER to return to the menu...[/]");
        Console.ReadLine();
    }

    private static string? EnsureRomRoot(string prompt)
    {
        if (!string.IsNullOrWhiteSpace(_rememberedRomRoot))
        {
            AnsiConsole.MarkupLine($"[dim]Using saved ROM root:[/] {_rememberedRomRoot.EscapeMarkup()}");
            return _rememberedRomRoot;
        }

        var root = PromptForPath(prompt);
        if (string.IsNullOrWhiteSpace(root))
        {
            AnsiConsole.MarkupLine("[red]No root directory provided.[/]");
            return null;
        }

        _rememberedRomRoot = root;
        return root;
    }

    private static void SetRomRoot()
    {
        var newRoot = PromptForOptional("Enter ROM root directory (leave blank to clear)");
        if (string.IsNullOrWhiteSpace(newRoot))
        {
            _rememberedRomRoot = null;
            AnsiConsole.MarkupLine("[yellow]Cleared saved ROM root.[/]");
        }
        else
        {
            _rememberedRomRoot = newRoot;
            AnsiConsole.MarkupLine($"[green]ROM root set to {newRoot.EscapeMarkup()}[/]");
        }
    }

    private static void SetInstanceProfile()
    {
        var newInstance = PromptForOptional("Enter instance name (leave blank for 'default')");
        if (string.IsNullOrWhiteSpace(newInstance))
        {
            _instanceName = "default";
            AnsiConsole.MarkupLine("[yellow]Switched to default instance profile.[/]");
        }
        else
        {
            _instanceName = newInstance;
            AnsiConsole.MarkupLine($"[green]Instance profile set to {SanitizeInstanceName(_instanceName).EscapeMarkup()}[/]");
        }
    }

    private static readonly Regex TitleRegionPattern = new(@"^(?<title>.+?)\s*\((?<region>[^)]+)\)", RegexOptions.Compiled);

    private static RomRecord BuildRomRecord(string filePath, DateTime timestamp)
    {
        var info = new FileInfo(filePath);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        var match = TitleRegionPattern.Match(nameWithoutExt);

        string? title = null;
        string? region = null;
        if (match.Success)
        {
            title = match.Groups["title"].Value.Trim();
            region = match.Groups["region"].Value.Trim();
        }

        var systemId = GuessSystemId(info.Extension);

        return new RomRecord(
            filePath,
            info.Length,
            timestamp,
            systemId,
            title,
            region,
            Path.GetFileName(filePath));
    }

    private static string? GuessSystemId(string extension)
    {
        var ext = extension.ToLowerInvariant();
        if (new[] { ".bin", ".cue", ".chd", ".pbp", ".iso" }.Contains(ext))
        {
            return "PSX";
        }
        if (new[] { ".wbfs", ".rvz", ".wux", ".gcm" }.Contains(ext))
        {
            return "Nintendo";
        }
        if (new[] { ".xci", ".nsp", ".nsz" }.Contains(ext))
        {
            return "Switch";
        }
        if (new[] { ".nes", ".smc", ".sfc", ".gb", ".gbc", ".gba" }.Contains(ext))
        {
            return "Retro-Nintendo";
        }
        if (new[] { ".z64", ".n64", ".v64" }.Contains(ext))
        {
            return "N64";
        }
        return null;
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
        Console.WriteLine($"Î“Ã¿Ã¤âˆ©â••Ã… [IMPACT] Unknown command: {command}");
        Console.WriteLine();
        PrintHelp();
        return (int)ExitCode.InvalidArgs;
    }

    private static void PrintBanner()
    {
        var border = new string('=', 59);
        Console.WriteLine(border);
        Console.WriteLine("  ARK-Retro-Forge v{0}", GetVersion());
        Console.WriteLine("  Spaceflight Toolchain - Portable - Deterministic");
        Console.WriteLine("  No ROMs/BIOS/Keys included - User-supplied tools");
        Console.WriteLine(border);
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
        Console.WriteLine("â‰¡Æ’Ã†Ã­ Run 'doctor' first to check your environment");
    }

    private static string GetVersion()
    {
        var version = typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "0.1.0-dev";
        return version;
    }

    private static bool HasInteractiveConsole()
    {
        try
        {
            if (Console.IsOutputRedirected || Console.IsInputRedirected)
            {
                return false;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }

        return AnsiConsole.Profile.Capabilities.Interactive;
    }

    private static string GetInstanceRoot()
    {
        var sanitized = SanitizeInstanceName(_instanceName);
        var path = Path.Combine(AppContext.BaseDirectory, "instances", sanitized);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string SanitizeInstanceName(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "default" : name.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(ch, '_');
        }
        return string.IsNullOrWhiteSpace(trimmed) ? "default" : trimmed;
    }

    private static void ParseGlobalArgs(ref string[] args)
    {
        if (args.Length == 0)
        {
            return;
        }

        var filtered = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--instance", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    _instanceName = args[++i];
                }
                continue;
            }

            filtered.Add(args[i]);
        }

        args = filtered.ToArray();
    }
}








