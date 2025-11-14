using System.CommandLine;
using ARK.Cli.Infrastructure;
using ARK.Core.Renaming;

namespace ARK.Cli.Commands;

/// <summary>
/// Rename command - rename ROMs to standard format
/// </summary>
public static class RenameCommand
{
    public static Command Create()
    {
        var command = new Command("rename", "Rename ROMs to standard format 'Title (Region) [ID]'");
        
        var rootOption = new Option<string>("--root", "Root directory to scan for renaming") { IsRequired = true };
        command.AddOption(rootOption);

        command.SetHandler(async (string root, bool dryRun, bool apply, bool force, bool verbose) =>
        {
            var exitCode = await ExecuteAsync(root, dryRun, apply, force, verbose);
            Environment.Exit(exitCode);
        },
        rootOption,
        new Option<bool>("--dry-run"),
        new Option<bool>("--apply"),
        new Option<bool>("--force"),
        new Option<bool>("--verbose"));

        return command;
    }

    private static async Task<int> ExecuteAsync(string root, bool dryRun, bool apply, bool force, bool verbose)
    {
        if (!Directory.Exists(root))
        {
            var error = new ErrorEvent
            {
                Code = "InvalidPath",
                Component = "rename",
                Context = $"Directory not found: {root}",
                Suggestion = "Verify the --root path exists"
            };
            Console.WriteLine(error);
            return (int)ExitCode.InvalidArgs;
        }

        // When --apply is specified, flip dryRun to false
        if (apply)
        {
            dryRun = false;
        }

        Console.WriteLine($"Renaming files in: {root}");
        Console.WriteLine($"Mode: {(dryRun ? "DRY-RUN (preview only)" : "APPLY (will rename files)")}");
        if (!dryRun && !force)
        {
            Console.WriteLine("⚠️  This will modify files. Use --force to confirm.");
            return (int)ExitCode.InvalidArgs;
        }
        Console.WriteLine();

        // Example: scan for files and create rename operations
        var files = Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.iso", SearchOption.AllDirectories))
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("No files found to rename");
            return (int)ExitCode.OK;
        }

        var renameCount = 0;
        var skipCount = 0;

        foreach (var file in files)
        {
            // Example metadata - in real implementation, this would come from DAT parsing or file analysis
            var metadata = new RomMetadata
            {
                OriginalPath = file,
                Title = "Example Game",
                Region = "USA",
                Id = "SLUS-12345",
                Extension = Path.GetExtension(file)
            };

            var operation = RomRenamer.CreateRenameOperation(file, metadata);

            if (operation.IsAlreadyNamed)
            {
                skipCount++;
                if (verbose)
                {
                    Console.WriteLine($"✓ Already named: {Path.GetFileName(file)}");
                }
                continue;
            }

            if (operation.Warning != null)
            {
                Console.WriteLine($"⚠️  {Path.GetFileName(file)}: {operation.Warning}");
                continue;
            }

            Console.WriteLine($"{'→',-3} {Path.GetFileName(operation.SourcePath)}");
            Console.WriteLine($"   {operation.DestinationFileName}");

            if (!dryRun)
            {
                try
                {
                    File.Move(operation.SourcePath, operation.DestinationPath);
                    renameCount++;
                }
                catch (Exception ex)
                {
                    var error = new ErrorEvent
                    {
                        Code = "RenameError",
                        Component = "rename",
                        Context = $"Failed to rename {Path.GetFileName(file)}: {ex.Message}",
                        Suggestion = "Check file permissions"
                    };
                    Console.WriteLine(error);
                }
            }
            else
            {
                renameCount++;
            }
        }

        Console.WriteLine($"\n✓ Rename preview complete");
        Console.WriteLine($"  Files to rename: {renameCount}");
        Console.WriteLine($"  Already named: {skipCount}");
        
        if (dryRun)
        {
            Console.WriteLine($"\n💡 Next step: Run with '--apply --force' to apply these changes");
        }
        else
        {
            Console.WriteLine($"\n✓ {renameCount} files renamed successfully");
        }

        await Task.CompletedTask;
        return (int)ExitCode.OK;
    }
}
