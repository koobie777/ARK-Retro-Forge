using ARK.Core.PSX;
using ARK.Core.Tools;
using System.Text.Json;

namespace ARK.Cli.Commands;

/// <summary>
/// Handles PSX convert commands
/// </summary>
public class PsxConvertCommand
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var deleteSource = args.Contains("--delete-source");
        var json = args.Contains("--json");
        var cheatModeStr = GetArgValue(args, "--cheats") ?? "standalone";

        if (string.IsNullOrEmpty(root))
        {
            Console.WriteLine("☄️ [IMPACT] | Component: convert psx | Context: Missing --root argument | Fix: Specify --root <path>");
            return 1;
        }

        if (!Directory.Exists(root))
        {
            Console.WriteLine($"☄️ [IMPACT] | Component: convert psx | Context: Directory not found: {root} | Fix: Verify the --root path exists");
            return 1;
        }

        if (deleteSource && !apply)
        {
            Console.WriteLine("☄️ [IMPACT] | Component: convert psx | Context: --delete-source requires --apply | Fix: Add --apply flag");
            return 1;
        }

        // Check for chdman tool
        var toolManager = new ToolManager();
        var chdmanCheck = toolManager.CheckTool("chdman");
        if (!chdmanCheck.IsFound)
        {
            Console.WriteLine("☄️ [IMPACT] | Component: convert psx | Context: chdman tool not found | Fix: Download chdman and place in .\\tools\\ directory");
            return 1;
        }

        // Parse cheat handling mode
        if (!Enum.TryParse<CheatHandlingMode>(cheatModeStr, true, out var cheatMode))
        {
            Console.WriteLine($"☄️ [IMPACT] | Component: convert psx | Context: Invalid --cheats mode: {cheatModeStr} | Fix: Use omit, standalone, or as-disc");
            return 1;
        }

        // Create planner and generate plan
        var planner = new PsxConvertPlanner(cheatMode);
        var operations = await planner.PlanConversionsAsync(root, recursive);

        // Output plan
        if (json)
        {
            OutputJson(operations);
        }
        else
        {
            OutputTable(operations, cheatMode, apply, deleteSource);
        }

        // Apply conversions if requested
        if (apply)
        {
            return await ApplyConversionsAsync(operations, deleteSource, chdmanCheck.Path!);
        }

        return 0;
    }

    private static void OutputJson(List<PsxConvertOperation> operations)
    {
        var jsonOutput = JsonSerializer.Serialize(operations, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
        Console.WriteLine(jsonOutput);
    }

    private static void OutputTable(List<PsxConvertOperation> operations, CheatHandlingMode cheatMode, bool apply, bool deleteSource)
    {
        var mode = apply ? "APPLY" : "DRY RUN";
        Console.WriteLine($"🛰️ [PSX CONVERT - {mode}]");
        Console.WriteLine($"Cheat handling mode: {cheatMode}");
        if (deleteSource)
        {
            Console.WriteLine("⚠️  Delete source enabled: BIN/CUE files will be deleted after conversion");
        }
        Console.WriteLine();

        if (operations.Count == 0)
        {
            Console.WriteLine("⚠️ [ANOMALY] No CUE files found to convert");
            return;
        }

        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"{"Title",-40} {"Disc",-8} {"Status",-20}");
        Console.WriteLine("───────────────────────────────────────────────────────────────────────────────");

        var toConvert = 0;
        var warnings = 0;
        var missingSerials = 0;

        foreach (var op in operations)
        {
            var title = op.Title ?? Path.GetFileNameWithoutExtension(op.SourceCuePath);
            var disc = op.DiscNumber.HasValue ? $"Disc {op.DiscNumber.Value}" : "-";
            var status = string.IsNullOrWhiteSpace(op.Warning) ? "→ CONVERT" : "⚠️  SKIP";

            if (title.Length > 40)
            {
                title = title[..37] + "...";
            }

            Console.WriteLine($"{title,-40} {disc,-8} {status,-20}");

            if (!string.IsNullOrWhiteSpace(op.Warning))
            {
                Console.WriteLine($"  ⚠️  {op.Warning}");
                warnings++;
            }
            else
            {
                toConvert++;
            }

            if (string.IsNullOrWhiteSpace(op.Serial))
            {
                missingSerials++;
            }
        }

        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"\nSummary:");
        Console.WriteLine($"  Total CUE files: {operations.Count}");
        Console.WriteLine($"  To convert: {toConvert}");
        if (warnings > 0)
        {
            Console.WriteLine($"  ⚠️  Warnings/skipped: {warnings}");
        }
        if (missingSerials > 0)
        {
            Console.WriteLine($"  ⚠️  Missing serials: {missingSerials}");
        }

        if (!apply && toConvert > 0)
        {
            Console.WriteLine($"\n➡️ Next step: Add --apply to execute conversions");
            if (!deleteSource)
            {
                Console.WriteLine($"💡 Tip: Add --delete-source to remove BIN/CUE files after conversion");
            }
        }
    }

    private static async Task<int> ApplyConversionsAsync(List<PsxConvertOperation> operations, bool deleteSource, string chdmanPath)
    {
        var toConvert = operations.Where(op => string.IsNullOrEmpty(op.Warning)).ToList();
        
        if (toConvert.Count == 0)
        {
            Console.WriteLine("\n✨ [DOCKED] No conversions to apply");
            return 0;
        }

        Console.WriteLine($"\n🔥 [BURN] Converting {toConvert.Count} CUE files to CHD...");
        
        var success = 0;
        var failed = 0;

        for (int i = 0; i < toConvert.Count; i++)
        {
            var op = toConvert[i];
            var title = op.Title ?? Path.GetFileNameWithoutExtension(op.SourceCuePath);
            
            Console.Write($"\r[{i + 1}/{toConvert.Count}] {title}".PadRight(80));

            try
            {
                // Run chdman
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = chdmanPath,
                    Arguments = $"createcd -i \"{op.SourceCuePath}\" -o \"{op.DestinationChdPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    Console.WriteLine($"\n  ☄️ Failed to start chdman for {Path.GetFileName(op.SourceCuePath)}");
                    failed++;
                    continue;
                }

                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    success++;
                    
                    // Delete source files if requested
                    if (deleteSource)
                    {
                        try
                        {
                            File.Delete(op.SourceCuePath);
                            foreach (var binPath in op.SourceBinPaths)
                            {
                                if (File.Exists(binPath))
                                {
                                    File.Delete(binPath);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"\n  ⚠️  Failed to delete source files for {Path.GetFileName(op.SourceCuePath)}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"\n  ☄️ chdman failed for {Path.GetFileName(op.SourceCuePath)} (exit code {process.ExitCode})");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n  ☄️ Failed to convert {Path.GetFileName(op.SourceCuePath)}: {ex.Message}");
                failed++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"\n✨ [DOCKED] Conversion complete");
        Console.WriteLine($"  Success: {success}");
        if (failed > 0)
        {
            Console.WriteLine($"  Failed: {failed}");
        }

        return failed > 0 ? 1 : 0;
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
}
