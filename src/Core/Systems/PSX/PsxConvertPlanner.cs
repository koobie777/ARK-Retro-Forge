namespace ARK.Core.Systems.PSX;

public record PsxPreflightCheck(string Label, bool Passed, string Detail, string? Fix = null);

public record PsxPreflightResult(
    IReadOnlyList<PsxPreflightCheck> Checks,
    IReadOnlyList<PsxConvertOperation> ReadableOperations)
{
    public bool AllPassed => Checks.All(c => c.Passed);
}

/// <summary>
/// Represents the desired conversion target.
/// </summary>
public enum PsxConversionTarget
{
    Chd,
    BinCue,
    Iso
}

/// <summary>
/// Represents a PSX convert operation (multi-target).
/// Contains only routing/path data — no parser-derived metadata.
/// </summary>
public record PsxConvertOperation
{
    public required string SourcePath { get; init; }
    public string? DestinationPath { get; init; }
    public string? DestinationCuePath { get; init; }
    public string? DestinationBinPath { get; init; }
    public required PsxConversionTarget Target { get; init; }
    public required ChdMediaType MediaType { get; init; }
    public bool AlreadyConverted { get; init; }
    public string? Warning { get; init; }
    public string? GeneratedCueContent { get; init; }
}

/// <summary>
/// Plans convert operations for PSX files.
/// Pure format conversion — no parser, no DAT, no serial probing.
/// </summary>
public class PsxConvertPlanner
{
    
    /// <summary>
    /// Plan convert operations for CUE files in a directory
    /// </summary>
    /// <param name="rootPath">Root directory to scan</param>
    /// <param name="recursive">Whether to scan recursively</param>
    /// <param name="rebuild">Force rebuild even if destination exists</param>
    /// <param name="target">Desired target format</param>
    /// <param name="flatten">Whether to flatten output to the root directory</param>
    public List<PsxConvertOperation> PlanConversions(string rootPath, bool recursive = false, bool rebuild = false, PsxConversionTarget target = PsxConversionTarget.Chd, bool flatten = false)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return target switch
        {
            PsxConversionTarget.Chd => PlanCueToChd(rootPath, searchOption, rebuild, flatten),
            PsxConversionTarget.BinCue => PlanChdToBinCue(rootPath, searchOption, rebuild, flatten),
            PsxConversionTarget.Iso => PlanChdToIso(rootPath, searchOption, rebuild, flatten),
            _ => new List<PsxConvertOperation>()
        };
    }

    private static List<PsxConvertOperation> PlanCueToChd(string rootPath, SearchOption searchOption, bool rebuild, bool flatten)
    {
        var operations = new List<PsxConvertOperation>();
        var cueFiles = Directory.GetFiles(rootPath, "*.cue", searchOption);

        foreach (var cueFile in cueFiles)
        {
            var directory = flatten ? rootPath : (Path.GetDirectoryName(cueFile) ?? string.Empty);
            var baseName = Path.GetFileNameWithoutExtension(cueFile);
            var destinationPath = Path.Combine(directory, baseName + ".chd");
            var mediaType = ChdMediaTypeHelper.DetermineFromFilePath(cueFile, "PSX");
            var alreadyConverted = !rebuild && File.Exists(destinationPath);

            operations.Add(new PsxConvertOperation
            {
                SourcePath = cueFile,
                DestinationPath = destinationPath,
                Target = PsxConversionTarget.Chd,
                MediaType = mediaType,
                AlreadyConverted = alreadyConverted
            });
        }

        return operations;
    }

    private static List<PsxConvertOperation> PlanChdToBinCue(string rootPath, SearchOption searchOption, bool rebuild, bool flatten)
    {
        var operations = new List<PsxConvertOperation>();
        var chdFiles = Directory.GetFiles(rootPath, "*.chd", searchOption);

        foreach (var chdFile in chdFiles)
        {
            var directory = flatten ? rootPath : (Path.GetDirectoryName(chdFile) ?? string.Empty);
            var baseName = Path.GetFileNameWithoutExtension(chdFile);
            var destinationBin = Path.Combine(directory, baseName + ".bin");
            var destinationCue = Path.Combine(directory, baseName + ".cue");
            var mediaType = ChdMediaTypeHelper.DetermineFromFilePath(chdFile);
            var alreadyConverted = !rebuild && File.Exists(destinationCue) && File.Exists(destinationBin);
            var generatedCueContent = GenerateSingleTrackCue(baseName + ".bin");

            var warning = mediaType == ChdMediaType.DVD
                ? "DVD images should be extracted to ISO instead of BIN/CUE"
                : null;

            operations.Add(new PsxConvertOperation
            {
                SourcePath = chdFile,
                DestinationCuePath = destinationCue,
                DestinationBinPath = destinationBin,
                Target = PsxConversionTarget.BinCue,
                MediaType = mediaType,
                AlreadyConverted = alreadyConverted,
                GeneratedCueContent = generatedCueContent,
                Warning = warning
            });
        }

        return operations;
    }

    /// <summary>
    /// Runs all pre-flight checks before executing a batch conversion.
    /// Checks: disk space, source readability, chdman availability, destination writability.
    /// </summary>
    /// <param name="allOperations">All planned operations (including already-converted).</param>
    /// <param name="destinationRoot">Root directory where output files will be written.</param>
    /// <param name="chdmanFound">Whether chdman was located by the tool manager.</param>
    /// <param name="chdmanVersion">Reported version string, if any.</param>
    public PsxPreflightResult RunPreflight(
        IReadOnlyList<PsxConvertOperation> allOperations,
        string destinationRoot,
        bool chdmanFound,
        string? chdmanVersion)
    {
        var pending = allOperations.Where(o => !o.AlreadyConverted).ToList();

        // Check 2 — source readability
        var readable = new List<PsxConvertOperation>();
        var unreadableNames = new List<string>();
        foreach (var op in pending)
        {
            try
            {
                using var fs = new FileStream(op.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                readable.Add(op);
            }
            catch
            {
                unreadableNames.Add(Path.GetFileName(op.SourcePath));
            }
        }
        var sourceCheck = unreadableNames.Count == 0
            ? new PsxPreflightCheck("Source files", true, $"{pending.Count}/{pending.Count} readable")
            : new PsxPreflightCheck("Source files", false,
                $"{readable.Count}/{pending.Count} readable — {unreadableNames.Count} locked/missing",
                "Locked: " + string.Join(", ", unreadableNames.Take(3))
                    + (unreadableNames.Count > 3 ? $" (+{unreadableNames.Count - 3} more)" : ""));

        // Check 1 — disk space (2.5× source size as safe estimate for CHD→BIN expansion)
        const double ExpansionFactor = 2.5;
        long totalSourceBytes = 0;
        foreach (var op in readable)
        {
            try { totalSourceBytes += new FileInfo(op.SourcePath).Length; } catch { /* best effort */ }
        }
        var requiredBytes = (long)(totalSourceBytes * ExpansionFactor);

        PsxPreflightCheck diskCheck;
        try
        {
            var pathRoot = Path.GetPathRoot(Path.GetFullPath(destinationRoot)) ?? destinationRoot;
            var drive = new DriveInfo(pathRoot);
            var available = drive.AvailableFreeSpace;
            diskCheck = available >= requiredBytes || requiredBytes == 0
                ? new PsxPreflightCheck("Disk space", true, FormatBytes(available) + " available")
                : new PsxPreflightCheck("Disk space", false,
                    $"Need {FormatBytes(requiredBytes)}, only {FormatBytes(available)} available",
                    "Free disk space or choose a different destination drive");
        }
        catch (Exception ex)
        {
            diskCheck = new PsxPreflightCheck("Disk space", false, $"Cannot check drive: {ex.Message}",
                "Ensure the destination path is accessible");
        }

        // Check 3 — chdman
        var chdmanCheck = chdmanFound
            ? new PsxPreflightCheck("chdman", true, $"Ready {chdmanVersion ?? ""}".Trim())
            : new PsxPreflightCheck("chdman", false, "Not found",
                "Run medical-bay to diagnose, then place chdman.exe in tools\\");

        // Check 4 — destination writability
        PsxPreflightCheck destCheck;
        try
        {
            Directory.CreateDirectory(destinationRoot);
            var probe = Path.Combine(destinationRoot, $".ark_write_probe_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            destCheck = new PsxPreflightCheck("Destination", true, "Writable");
        }
        catch (UnauthorizedAccessException)
        {
            destCheck = new PsxPreflightCheck("Destination", false, "Permission denied",
                "Run as administrator or choose a writable destination directory");
        }
        catch (Exception ex)
        {
            destCheck = new PsxPreflightCheck("Destination", false, ex.Message,
                "Verify the destination path is valid and accessible");
        }

        // Merge: already-converted ops + readable pending ops
        var readableAll = allOperations
            .Where(o => o.AlreadyConverted)
            .Concat(readable)
            .ToList();

        return new PsxPreflightResult(
            [diskCheck, sourceCheck, chdmanCheck, destCheck],
            readableAll);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L)     return $"{bytes / 1_048_576.0:F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }

    private static string GenerateSingleTrackCue(string binFileName)
    {
        return $"FILE \"{binFileName}\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n";
    }

    private static List<PsxConvertOperation> PlanChdToIso(string rootPath, SearchOption searchOption, bool rebuild, bool flatten)
    {
        var operations = new List<PsxConvertOperation>();
        var chdFiles = Directory.GetFiles(rootPath, "*.chd", searchOption);

        foreach (var chdFile in chdFiles)
        {
            var directory = flatten ? rootPath : (Path.GetDirectoryName(chdFile) ?? string.Empty);
            var baseName = Path.GetFileNameWithoutExtension(chdFile);
            var destinationPath = Path.Combine(directory, baseName + ".iso");
            var mediaType = ChdMediaTypeHelper.DetermineFromFilePath(chdFile);
            var alreadyConverted = !rebuild && File.Exists(destinationPath);

            var warning = mediaType == ChdMediaType.CD
                ? "ISO extraction is intended for DVD media; use --to bin for CD titles"
                : null;

            operations.Add(new PsxConvertOperation
            {
                SourcePath = chdFile,
                DestinationPath = destinationPath,
                Target = PsxConversionTarget.Iso,
                MediaType = mediaType,
                AlreadyConverted = alreadyConverted,
                Warning = warning
            });
        }

        return operations;
    }
}
