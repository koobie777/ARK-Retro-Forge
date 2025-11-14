using ARK.Core.PSX;
using System.Text.Json;

namespace ARK.Cli.Commands;

/// <summary>
/// Handles PSX rename commands
/// </summary>
public class PsxRenameCommand
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var json = args.Contains("--json");
        var cheatModeStr = GetArgValue(args, "--cheats") ?? "standalone";

        if (string.IsNullOrEmpty(root))
        {
            Console.WriteLine("☄️ [IMPACT] | Component: rename psx | Context: Missing --root argument | Fix: Specify --root <path>");
            return 1;
        }

        if (!Directory.Exists(root))
        {
            Console.WriteLine($"☄️ [IMPACT] | Component: rename psx | Context: Directory not found: {root} | Fix: Verify the --root path exists");
            return 1;
        }

        // Parse cheat handling mode
        if (!Enum.TryParse<CheatHandlingMode>(cheatModeStr, true, out var cheatMode))
        {
            Console.WriteLine($"☄️ [IMPACT] | Component: rename psx | Context: Invalid --cheats mode: {cheatModeStr} | Fix: Use omit, standalone, or as-disc");
            return 1;
        }

        // Create planner and generate plan
        var planner = new PsxRenamePlanner(cheatMode);
        var operations = await planner.PlanRenamesAsync(root, recursive);

        // Output plan
        if (json)
        {
            OutputJson(operations);
        }
        else
        {
            OutputTable(operations, cheatMode, apply);
        }

        // Apply renames if requested
        if (apply)
        {
            return await ApplyRenamesAsync(operations);
        }

        return 0;
    }

    private static void OutputJson(List<PsxRenameOperation> operations)
    {
        var jsonOutput = JsonSerializer.Serialize(operations, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
        Console.WriteLine(jsonOutput);
    }

    private static void OutputTable(List<PsxRenameOperation> operations, CheatHandlingMode cheatMode, bool apply)
    {
        var mode = apply ? "APPLY" : "DRY RUN";
        Console.WriteLine($"🛰️ [PSX RENAME - {mode}]");
        Console.WriteLine($"Cheat handling mode: {cheatMode}");
        Console.WriteLine();

        if (operations.Count == 0)
        {
            Console.WriteLine("⚠️ [ANOMALY] No PSX files found to rename");
            return;
        }

        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"{"Current",-40} {"New",-40} {"Status",-15}");
        Console.WriteLine("───────────────────────────────────────────────────────────────────────────────");

        var renamed = 0;
        var alreadyNamed = 0;
        var warnings = 0;
        var cheatDiscs = 0;
        var eduDiscs = 0;
        var missingSerials = 0;

        foreach (var op in operations)
        {
            var currentName = Path.GetFileName(op.SourcePath);
            var newName = op.DestinationFileName;
            var status = op.IsAlreadyNamed ? "✓ OK" : "→ RENAME";

            if (currentName.Length > 40)
            {
                currentName = currentName[..37] + "...";
            }
            if (newName.Length > 40)
            {
                newName = newName[..37] + "...";
            }

            Console.WriteLine($"{currentName,-40} {newName,-40} {status,-15}");

            if (!string.IsNullOrWhiteSpace(op.Warning))
            {
                Console.WriteLine($"  ⚠️  {op.Warning}");
                warnings++;
            }

            if (op.IsCheatDisc)
            {
                cheatDiscs++;
            }
            if (op.IsEducationalDisc)
            {
                eduDiscs++;
            }
            if (string.IsNullOrWhiteSpace(op.Serial))
            {
                missingSerials++;
            }

            if (op.IsAlreadyNamed)
            {
                alreadyNamed++;
            }
            else
            {
                renamed++;
            }
        }

        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"\nSummary:");
        Console.WriteLine($"  Total files: {operations.Count}");
        Console.WriteLine($"  Already named: {alreadyNamed}");
        Console.WriteLine($"  To rename: {renamed}");
        if (warnings > 0)
        {
            Console.WriteLine($"  ⚠️  Warnings: {warnings}");
        }
        if (cheatDiscs > 0)
        {
            Console.WriteLine($"  🎮 Cheat discs: {cheatDiscs}");
        }
        if (eduDiscs > 0)
        {
            Console.WriteLine($"  📚 Educational discs: {eduDiscs}");
        }
        if (missingSerials > 0)
        {
            Console.WriteLine($"  ⚠️  Missing serials: {missingSerials}");
        }

        if (!apply && renamed > 0)
        {
            Console.WriteLine($"\n➡️ Next step: Add --apply to execute renames");
        }
    }

    private static async Task<int> ApplyRenamesAsync(List<PsxRenameOperation> operations)
    {
        var toRename = operations.Where(op => !op.IsAlreadyNamed && string.IsNullOrEmpty(op.Warning)).ToList();
        
        if (toRename.Count == 0)
        {
            Console.WriteLine("\n✨ [DOCKED] No renames to apply");
            return 0;
        }

        Console.WriteLine($"\n🔥 [BURN] Applying {toRename.Count} renames...");
        
        var success = 0;
        var failed = 0;

        foreach (var op in toRename)
        {
            try
            {
                File.Move(op.SourcePath, op.DestinationPath);
                success++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ☄️ Failed to rename {Path.GetFileName(op.SourcePath)}: {ex.Message}");
                failed++;
            }
        }

        Console.WriteLine($"\n✨ [DOCKED] Rename complete");
        Console.WriteLine($"  Success: {success}");
        if (failed > 0)
        {
            Console.WriteLine($"  Failed: {failed}");
        }

        await Task.CompletedTask;
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
