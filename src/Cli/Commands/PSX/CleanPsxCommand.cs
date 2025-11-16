using ARK.Cli.Infrastructure;
using ARK.Core.Database;
using ARK.Core.Dat;
using ARK.Core.Systems.PSX;
using Spectre.Console;

namespace ARK.Cli.Commands.PSX;

/// <summary>
/// Cleans PSX directories by corralling multi-track sets, generating missing CUE sheets, and importing staged ROMs.
/// </summary>
public static class CleanPsxCommand
{
    private static readonly string[] PsxExtensions = { ".bin", ".cue", ".iso", ".pbp", ".chd" };

    public static async Task<int> RunAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrWhiteSpace(root))
        {
            AnsiConsole.MarkupLine("[red][IMPACT] | Component: clean psx | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red][IMPACT] | Component: clean psx | Context: Directory not found: {root} | Fix: Verify the --root path exists[/]");
            return (int)ExitCode.InvalidArgs;
        }

        var recursive = args.Contains("--recursive");
        var apply = args.Contains("--apply");
        var ingestRoot = GetArgValue(args, "--ingest-root");
        if (!string.IsNullOrWhiteSpace(ingestRoot) && !Directory.Exists(ingestRoot))
        {
            AnsiConsole.MarkupLine($"[yellow]Ignored ingest root because the path does not exist: {ingestRoot}[/]");
            ingestRoot = null;
        }

        var multiTrackDirName = GetArgValue(args, "--multitrack-dir") ?? "PSX MultiTrack";
        var importDirName = GetArgValue(args, "--import-dir") ?? "PSX Imports";
        var moveMultiTrack = args.Contains("--move-multitrack");
        var generateCues = args.Contains("--generate-cues");
        var moveIngest = args.Contains("--ingest-move");
        var flattenSingles = args.Contains("--flatten");
        var autoYes = args.Contains("--yes");
        var interactive = AnsiConsole.Profile.Capabilities.Interactive;

        var multiTrackPlans = PlanMultiTrackMoves(root, recursive, multiTrackDirName);
        var cuePlans = PlanMissingCueCreations(root, recursive);
        var ingestPlans = await PlanIngestMovesAsync(root, ingestRoot, importDirName);
        var flattenPlans = PlanFlattenMoves(root, recursive);

        RenderSummary(multiTrackPlans, cuePlans, ingestPlans, flattenPlans);

        if (!moveMultiTrack && multiTrackPlans.Count > 0 && apply && interactive)
        {
            moveMultiTrack = autoYes || AnsiConsole.Confirm($"Move {multiTrackPlans.Count} multi-track set(s) into [steelblue]{multiTrackDirName}[/]?");
        }

        if (!generateCues && cuePlans.Count > 0 && apply && interactive)
        {
            generateCues = autoYes || AnsiConsole.Confirm($"Generate {cuePlans.Count} missing CUE file(s)?");
        }

        if (!moveIngest && ingestPlans.Count > 0 && apply && interactive)
        {
            moveIngest = autoYes || AnsiConsole.Confirm($"Move {ingestPlans.Count} imported file(s) into the PSX root?");
        }

        if (!flattenSingles && flattenPlans.Count > 0 && apply && interactive)
        {
            flattenSingles = autoYes || AnsiConsole.Confirm($"Flatten {flattenPlans.Count} single-disc folder(s) into the PSX root?");
        }

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]DRY-RUN:[/] Preview only. Use --apply to make changes.");
            return (int)ExitCode.OK;
        }

        var movedMultiTrack = moveMultiTrack ? ExecuteMultiTrackMoves(multiTrackPlans) : 0;
        var createdCues = generateCues ? ExecuteCueCreations(cuePlans) : 0;
        var movedImports = moveIngest ? ExecuteIngestMoves(ingestPlans) : 0;
        var flattened = flattenSingles ? ExecuteFlattenMoves(flattenPlans, root) : 0;

        AnsiConsole.MarkupLine("[green]Clean-up complete.[/]");
        AnsiConsole.MarkupLine($"  Multi-track sets moved: {movedMultiTrack}");
        AnsiConsole.MarkupLine($"  CUE files generated: {createdCues}");
        AnsiConsole.MarkupLine($"  Imported ROMs moved: {movedImports}");
        AnsiConsole.MarkupLine($"  Single-disc folders flattened: {flattened}");

        if (!apply || ingestPlans.Count == 0)
        {
            return (int)ExitCode.OK;
        }

        return (int)ExitCode.OK;
    }

    private static List<MultiTrackMovePlan> PlanMultiTrackMoves(string root, bool recursive, string multiTrackDirName)
    {
        var planner = new PsxBinMergePlanner();
        var operations = planner.PlanMerges(root, recursive);
        var plans = new List<MultiTrackMovePlan>();
        foreach (var op in operations.Where(op => op.TrackSources.Count > 1))
        {
            var destDir = Path.Combine(root, multiTrackDirName, SanitizePathSegment(op.Title));
            var files = op.TrackSources.Select(t => t.AbsolutePath).Where(File.Exists).ToList();
            if (File.Exists(op.CuePath))
            {
                files.Add(op.CuePath);
            }

            if (files.Count == 0)
            {
                continue;
            }

            plans.Add(new MultiTrackMovePlan(op.Title, destDir, files));
        }

        return plans;
    }

    private static List<CueCreationPlan> PlanMissingCueCreations(string root, bool recursive)
    {
        var bins = Directory.EnumerateFiles(
            root,
            "*.bin",
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        var plans = new List<CueCreationPlan>();
        var plannedCueTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bin in bins)
        {
            var candidate = GetCandidateCuePath(bin);
            if (File.Exists(candidate))
            {
                continue;
            }

            if (!plannedCueTargets.Add(candidate))
            {
                continue;
            }

            plans.Add(new CueCreationPlan(bin, candidate));
        }

        return plans;
    }

    private static List<FlattenMovePlan> PlanFlattenMoves(string root, bool recursive)
    {
        var directories = Directory.GetDirectories(
                root,
                "*",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(dir => !dir.Equals(root, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var parser = new PsxNameParser();
        var plans = new List<FlattenMovePlan>();

        foreach (var directory in directories)
        {
            var files = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(file => PsxExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0)
            {
                continue;
            }

            var discs = files.Select(parser.Parse).ToList();

            // Skip directories containing multi-track layouts or true multi-disc sets
            if (discs.Any(d => d.IsMultiTrack) || ContainsTrueMultiDiscSet(discs))
            {
                continue;
            }

            plans.Add(new FlattenMovePlan(directory, files));
        }

        return plans;
    }

    private static async Task<List<IngestMovePlan>> PlanIngestMovesAsync(
        string root,
        string? ingestRoot,
        string importDirName)
    {
        var plans = new List<IngestMovePlan>();
        if (string.IsNullOrWhiteSpace(ingestRoot))
        {
            return plans;
        }

        var search = Directory.EnumerateFiles(ingestRoot, "*.*", SearchOption.AllDirectories)
            .Where(file => PsxExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (search.Count == 0)
        {
            return plans;
        }

        var dbPath = Path.Combine(InstancePathResolver.GetInstanceRoot(), "db");
        await using var dbManager = new DatabaseManager(dbPath);
        await dbManager.InitializeAsync();

        var romRepository = new RomRepository(dbManager.GetConnection());
        var roms = await romRepository.GetRomsAsync("PSX");
        if (roms.Count == 0 && await TryHydrateRomCacheAsync(root))
        {
            roms = await romRepository.GetRomsAsync("PSX");
        }

        var keySet = new HashSet<string>(
            roms.Select(r => $"{r.RomId ?? string.Empty}|{r.Title ?? string.Empty}|{r.Region ?? string.Empty}"),
            StringComparer.OrdinalIgnoreCase);

        var datFiles = GetDatFiles("psx");
        DatDescriptionIndex? datIndex = null;
        if (datFiles.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No PSX DAT catalog found. Run 'dat sync --system psx' to improve ROM identification.[/]");
        }
        else
        {
            datIndex = await DatDescriptionIndex.LoadAsync(datFiles);
        }

        var parser = new PsxNameParser();
        foreach (var file in search)
        {
            var discInfo = parser.Parse(file);
            var key = $"{discInfo.Serial ?? string.Empty}|{discInfo.Title ?? string.Empty}|{discInfo.Region ?? string.Empty}";
            if (keySet.Contains(key))
            {
                continue;
            }

            if (datIndex != null && !datIndex.Contains(discInfo.Title))
            {
                continue;
            }

            var importDir = Path.Combine(root, importDirName);
            var dest = Path.Combine(importDir, Path.GetFileName(file));
            plans.Add(new IngestMovePlan(file, dest, discInfo.Title, discInfo.Region));
        }

        return plans;
    }

    private static void RenderSummary(
        IReadOnlyCollection<MultiTrackMovePlan> multiTrackPlans,
        IReadOnlyCollection<CueCreationPlan> cuePlans,
        IReadOnlyCollection<IngestMovePlan> ingestPlans,
        IReadOnlyCollection<FlattenMovePlan> flattenPlans)
    {
        var infoTable = new Table().Border(TableBorder.Rounded);
        infoTable.AddColumn("[cyan]Focus[/]");
        infoTable.AddColumn("[green]Count[/]");

        infoTable.AddRow("Multi-track sets", multiTrackPlans.Count.ToString("N0"));
        infoTable.AddRow("Missing CUE files", cuePlans.Count.ToString("N0"));
        infoTable.AddRow("Import candidates", ingestPlans.Count.ToString("N0"));
        infoTable.AddRow("Flatten candidates", flattenPlans.Count.ToString("N0"));

        AnsiConsole.Write(infoTable);

        if (multiTrackPlans.Count > 0)
        {
            var table = new Table { Title = new TableTitle("[steelblue]Multi-Track Sets[/]") };
            table.AddColumn("Title");
            table.AddColumn("Files");
            foreach (var plan in multiTrackPlans.Take(5))
            {
                table.AddRow(plan.Title ?? "PSX", plan.Files.Count.ToString("N0"));
            }
            if (multiTrackPlans.Count > 5)
            {
                table.AddRow("[grey]…[/]", "[grey]…[/]");
            }
            AnsiConsole.Write(table);
        }

        if (cuePlans.Count > 0)
        {
            var table = new Table { Title = new TableTitle("[yellow]Missing CUEs[/]") };
            table.AddColumn("BIN");
            foreach (var plan in cuePlans.Take(5))
            {
                table.AddRow(Truncate(plan.BinPath));
            }
            if (cuePlans.Count > 5)
            {
                table.AddRow("[grey]…[/]");
            }
            AnsiConsole.Write(table);
        }

        if (ingestPlans.Count > 0)
        {
            var table = new Table { Title = new TableTitle("[cyan]Imports[/]") };
            table.AddColumn("Source");
            table.AddColumn("Dest");
            foreach (var plan in ingestPlans.Take(5))
            {
                table.AddRow(
                    Truncate(plan.SourcePath),
                    Truncate(plan.DestinationPath));
            }
            if (ingestPlans.Count > 5)
            {
                table.AddRow("[grey]…[/]", "[grey]…[/]");
            }
            AnsiConsole.Write(table);
        }

        if (flattenPlans.Count > 0)
        {
            var table = new Table { Title = new TableTitle("[green]Flatten[/]") };
            table.AddColumn("Folder");
            table.AddColumn("Files");
            foreach (var plan in flattenPlans.Take(5))
            {
                table.AddRow(Truncate(plan.Directory), plan.Files.Count.ToString("N0"));
            }
            if (flattenPlans.Count > 5)
            {
                table.AddRow("[grey]…[/]", "[grey]…[/]");
            }
            AnsiConsole.Write(table);
        }
    }

    private static int ExecuteMultiTrackMoves(List<MultiTrackMovePlan> plans)
    {
        var moved = 0;
        foreach (var plan in plans)
        {
            var targetDirectory = EnsureUniqueDirectory(plan.DestinationDirectory);
            foreach (var file in plan.Files)
            {
                var destination = EnsureUniquePath(Path.Combine(targetDirectory, Path.GetFileName(file)));
                File.Move(file, destination, overwrite: false);
            }

            moved++;
        }

        return moved;
    }

    private static int ExecuteCueCreations(List<CueCreationPlan> plans)
    {
        var created = 0;
        foreach (var plan in plans)
        {
            var directory = Path.GetDirectoryName(plan.CuePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(plan.CuePath, BuildCueContents(plan.BinPath));
            created++;
        }

        return created;
    }

    private static int ExecuteIngestMoves(List<IngestMovePlan> plans)
    {
        var moved = 0;
        foreach (var plan in plans)
        {
            var directory = Path.GetDirectoryName(plan.DestinationPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            Directory.CreateDirectory(directory);
            var destination = EnsureUniquePath(plan.DestinationPath);
            File.Move(plan.SourcePath, destination, overwrite: false);
            moved++;
        }

        return moved;
    }

    private static int ExecuteFlattenMoves(List<FlattenMovePlan> plans, string root)
    {
        var flattened = 0;
        foreach (var plan in plans)
        {
            foreach (var source in plan.Files)
            {
                var destination = EnsureUniquePath(Path.Combine(root, Path.GetFileName(source)));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination);
            }

            // Remove directory if empty after moves
            if (!Directory.EnumerateFileSystemEntries(plan.Directory).Any())
            {
                Directory.Delete(plan.Directory);
            }

            flattened++;
        }

        return flattened;
    }

    private static string BuildCueContents(string binPath)
    {
        var fileName = Path.GetFileName(binPath);
        return $"""
FILE "{fileName}" BINARY
  TRACK 01 MODE2/2352
    INDEX 01 00:00:00
""";
    }

    private static string Truncate(string path, int length = 60)
    {
        if (path.Length <= length)
        {
            return path.EscapeMarkup();
        }

        return $"{path[..Math.Max(1, length - 3)].EscapeMarkup()}...";
    }

    private static string SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "PSX";
        }

        var segment = value;
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            segment = segment.Replace(ch, '_');
        }

        return segment.Trim();
    }

    private static string GetCandidateCuePath(string binPath)
    {
        var directory = Path.GetDirectoryName(binPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(binPath);
        var sanitized = RemoveTrackSuffix(fileName);
        return Path.Combine(directory, sanitized + ".cue");
    }

    private static string RemoveTrackSuffix(string value)
    {
        var trackIndex = value.IndexOf("(Track", StringComparison.OrdinalIgnoreCase);
        return trackIndex > 0 ? value[..trackIndex].Trim() : value;
    }

    private static string EnsureUniquePath(string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);
        var counter = 1;

        while (true)
        {
            var candidate = Path.Combine(directory, $"{baseName} ({counter}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
            counter++;
        }
    }

    private static string EnsureUniqueDirectory(string destinationDirectory)
    {
        if (!Directory.Exists(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
            return destinationDirectory;
        }

        var counter = 1;
        while (true)
        {
            var candidate = $"{destinationDirectory}-{counter}";
            if (!Directory.Exists(candidate))
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            counter++;
        }
    }

    private static bool ContainsTrueMultiDiscSet(IReadOnlyList<PsxDiscInfo> discs)
    {
        var groups = discs
            .Where(d => !string.IsNullOrWhiteSpace(d.Title) && !string.IsNullOrWhiteSpace(d.Region))
            .GroupBy(d => (Title: d.Title!.Trim(), Region: d.Region!.Trim()), new TitleRegionComparer());

        foreach (var group in groups)
        {
            if (group.Count() <= 1)
            {
                continue;
            }

            if (group.Any(d => d.DiscNumber.HasValue || d.DiscCount.HasValue))
            {
                return true;
            }

            if (group.Select(d => Path.GetFileNameWithoutExtension(d.FilePath))
                .Any(name => name.Contains("(Disc", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> TryHydrateRomCacheAsync(string root)
    {
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            AnsiConsole.MarkupLine("[yellow]ROM cache is empty. Run 'scan --root <path>' to hydrate metadata before importing.[/]");
            return false;
        }

        var confirmed = AnsiConsole.Confirm($"ROM cache is empty. Scan {root.EscapeMarkup()} now?");
        if (!confirmed)
        {
            return false;
        }

        await global::ARK.Cli.Program.RunScanAsync(new[] { "scan", "--root", root, "--recursive" });
        return true;
    }

    private static string[] GetDatFiles(string system)
    {
        var datRoot = Path.Combine(InstancePathResolver.GetInstanceRoot(), "dat", system);
        if (!Directory.Exists(datRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(datRoot, "*.*", SearchOption.TopDirectoryOnly);
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private sealed record MultiTrackMovePlan(string Title, string DestinationDirectory, IReadOnlyList<string> Files);

    private sealed record CueCreationPlan(string BinPath, string CuePath);

    private sealed record IngestMovePlan(string SourcePath, string DestinationPath, string? Title, string? Region);

    private sealed record FlattenMovePlan(string Directory, IReadOnlyList<string> Files);

    private sealed class TitleRegionComparer : IEqualityComparer<(string Title, string Region)>
    {
        public bool Equals((string Title, string Region) x, (string Title, string Region) y) =>
            string.Equals(x.Title, y.Title, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Region, y.Region, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Title, string Region) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Title),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Region));
    }
}
