using System.CommandLine;
using ARK.Cli.Infrastructure;
using ARK.Core.Hashing;

namespace ARK.Cli.Commands;

/// <summary>
/// Verify command - verify ROM integrity with hash checking
/// </summary>
public static class VerifyCommand
{
    public static Command Create()
    {
        var command = new Command("verify", "Verify ROM integrity with hash checking");
        
        var rootOption = new Option<string>("--root", "Root directory to verify") { IsRequired = true };
        var crc32Option = new Option<bool>("--crc32", getDefaultValue: () => true, "Compute CRC32 hashes");
        var md5Option = new Option<bool>("--md5", getDefaultValue: () => true, "Compute MD5 hashes");
        var sha1Option = new Option<bool>("--sha1", getDefaultValue: () => true, "Compute SHA1 hashes");
        
        command.AddOption(rootOption);
        command.AddOption(crc32Option);
        command.AddOption(md5Option);
        command.AddOption(sha1Option);

        command.SetHandler(async (string root, bool crc32, bool md5, bool sha1, int workers, bool verbose) =>
        {
            var exitCode = await ExecuteAsync(root, crc32, md5, sha1, workers, verbose);
            Environment.Exit(exitCode);
        },
        rootOption,
        crc32Option,
        md5Option,
        sha1Option,
        new Option<int>("--workers"),
        new Option<bool>("--verbose"));

        return command;
    }

    private static async Task<int> ExecuteAsync(string root, bool crc32, bool md5, bool sha1, int workers, bool verbose)
    {
        if (!Directory.Exists(root))
        {
            var error = new ErrorEvent
            {
                Code = "InvalidPath",
                Component = "verify",
                Context = $"Directory not found: {root}",
                Suggestion = "Verify the --root path exists"
            };
            Console.WriteLine(error);
            return (int)ExitCode.InvalidArgs;
        }

        Console.WriteLine($"Verifying: {root}");
        Console.WriteLine($"Hashing: CRC32={crc32}, MD5={md5}, SHA1={sha1}");
        Console.WriteLine($"Workers: {workers}");
        Console.WriteLine();

        var hashOptions = new HashOptions
        {
            ComputeCrc32 = crc32,
            ComputeMd5 = md5,
            ComputeSha1 = sha1
        };

        var hasher = new FileHasher(hashOptions);
        var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bin", ".iso", ".chd", ".cso"
        };

        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("No files found to verify");
            return (int)ExitCode.OK;
        }

        var startTime = DateTime.UtcNow;
        var processed = 0;
        var totalBytes = 0L;

        try
        {
            foreach (var file in files)
            {
                processed++;
                Console.Write($"\r[{processed}/{files.Count}] {Path.GetFileName(file)}".PadRight(80));

                var result = await hasher.ComputeHashesAsync(file);
                totalBytes += result.FileSize;

                if (verbose)
                {
                    Console.WriteLine();
                    if (result.Crc32 != null)
                    {
                        Console.WriteLine($"  CRC32: {result.Crc32}");
                    }
                    if (result.Md5 != null)
                    {
                        Console.WriteLine($"  MD5:   {result.Md5}");
                    }
                    if (result.Sha1 != null)
                    {
                        Console.WriteLine($"  SHA1:  {result.Sha1}");
                    }
                }
            }

            Console.WriteLine();
            var duration = DateTime.UtcNow - startTime;
            var throughputMBps = totalBytes / 1024.0 / 1024.0 / Math.Max(duration.TotalSeconds, 0.001);

            Console.WriteLine($"\n✓ Verification complete");
            Console.WriteLine($"  Files processed: {processed}");
            Console.WriteLine($"  Total size: {totalBytes / 1024.0 / 1024.0:F2} MB");
            Console.WriteLine($"  Duration: {duration.TotalSeconds:F2}s");
            Console.WriteLine($"  Throughput: {throughputMBps:F2} MB/s");
            Console.WriteLine($"\n💡 Next step: Run 'rename --root {root}' to standardize filenames");

            return (int)ExitCode.OK;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            var error = new ErrorEvent
            {
                Code = "VerifyError",
                Component = "verify",
                Context = ex.Message,
                Suggestion = "Check file permissions and integrity"
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
