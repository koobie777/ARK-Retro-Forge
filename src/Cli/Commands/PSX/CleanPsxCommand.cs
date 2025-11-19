using System.Text;
using System.Text.RegularExpressions;
using ARK.Cli.Infrastructure;
using ARK.Core.Database;
using ARK.Core.Dat;
using ARK.Core.Systems.PSX;
using Spectre.Console;
using HeaderMetadata = ARK.Cli.Infrastructure.ConsoleDecorations.HeaderMetadata;

namespace ARK.Cli.Commands.PSX;

/// <summary>
/// Cleans PSX directories by corralling multi-track sets, generating missing CUE sheets, and importing staged ROMs.
/// </summary>
public static class CleanPsxCommand
{
    private static readonly string[] PsxExtensions = { ".bin", ".cue", ".iso", ".pbp", ".chd" };
    private static readonly Regex TrackNumberPattern = new(@"\(Track\s*(?<track>\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<int> RunAsync(string[] args)
    {
        var root = GetArgValue(args, "--root");
        if (string.IsNullOrWhiteSpace(root))
        {
            AnsiConsole.MarkupLine("[red][[IMPACT]] | Component: clean psx | Context: Missing --root argument | Fix: Specify --root <path>[/]");
            return (int)ExitCode.InvalidArgs;
        }

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red][[IMPACT]] | Component: clean psx | Context: Directory not found: {root} | Fix: Verify the --root path exists[/]");
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

        var importDirName = GetArgValue(args, "--import-dir") ?? "PSX Imports";
        var moveMultiTrack = args.Contains("--move-multitrack");
        var multiTrackDirName = GetArgValue(args, "--multitrack-dir") ?? string.Empty;
        var moveMultiDisc = args.Contains("--move-multidisc");
        var generateCues = args.Contains("--generate-cues");
        var moveIngest = args.Contains("--ingest-move");
        var flattenSingles = args.Contains("--flatten");
        var removeDuplicates = args.Contains("--remove-duplicates");
        var autoYes = args.Contains("--yes");
        var interactive = AnsiConsole.Profile.Capabilities.Interactive;
        var datSummary = DatStatusReporter.Inspect("psx").FirstOrDefault();
        var datStatus = datSummary switch
        {
            null => "[yellow]Catalog offline[/]",
            _ when !datSummary.HasCatalog => "[red]Missing[/]",
            _ when datSummary.IsStale => "[yellow]Stale[/]",
            _ => "[green]Ready[/]"
        };

        ConsoleDecorations.RenderOperationHeader(
            "PSX Clean",
            new HeaderMetadata("Mode", apply ? "[green]APPLY[/]" : "[yellow]DRY-RUN[/]", IsMarkup: true),
            new HeaderMetadata("Root", root),
            new HeaderMetadata("Recursive", recursive ? "Yes" : "No"),
            new HeaderMetadata("DAT", datStatus, IsMarkup: true),
            new HeaderMetadata("Ingest", string.IsNullOrWhiteSpace(ingestRoot) ? "n/a" : ingestRoot!));

        DatUsageHelper.WarnIfCatalogMissing("psx", "PSX clean");

        List<MultiTrackMovePlan> multiTrackPlans = new();
        List<MultiDiscMovePlan> multiDiscPlans = new();
        List<CueCreationPlan> cuePlans = new();
        List<IngestMovePlan> ingestPlans = new();
        List<FlattenMovePlan> flattenPlans = new();
        List<DuplicateGroup> duplicateGroups = new();

        var planningSteps = new List<(string Label, Func<Task> Work)>
        {
            ("Planning multi-track sets", () =>
            {
                multiTrackPlans = PlanMultiTrackMoves(root, recursive, multiTrackDirName);
                return Task.CompletedTask;
            }),
            ("Planning multi-disc sets", () =>
            {
                multiDiscPlans = PlanMultiDiscMoves(root, recursive);
                return Task.CompletedTask;
            }),
            ("Scanning for missing CUE files", () =>
            {
                cuePlans = PlanMissingCueCreations(root, recursive);
                return Task.CompletedTask;
            }),
            ("Evaluating ingest directory", async () =>
            {
                ingestPlans = await PlanIngestMovesAsync(root, ingestRoot, importDirName);
            }),
            ("Checking folders safe to flatten", () =>
            {
                flattenPlans = PlanFlattenMoves(root, recursive);
                return Task.CompletedTask;
            }),
            ("Scanning for duplicates", () =>
            {
                if (removeDuplicates)
                {
                    var detector = new PsxDuplicateDetector();
                    duplicateGroups = detector.ScanForDuplicates(root, recursive, "SHA1");
                }
                return Task.CompletedTask;
            })
        };

        var progressColumns = new ProgressColumn[]
        {
            new TaskDescriptionColumn(),
            new ProgressBarColumn { Width = 50, CompletedStyle = new Style(Color.SpringGreen1), RemainingStyle = new Style(Color.Grey35) },
            new PercentageColumn(),
            new RemainingTimeColumn(),
            new SpinnerColumn()
        };

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(progressColumns)
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Analyzing PSX library", maxValue: planningSteps.Count);
                foreach (var (label, work) in planningSteps)
                {
                    ThrowIfCancelled();
                    task.Description = label;
                    await work();
                    task.Increment(1);
                }
            });

        RenderSummary(multiTrackPlans, multiDiscPlans, cuePlans, ingestPlans, flattenPlans, duplicateGroups);

        OperationContextScope.ThrowIfCancellationRequested();

        if (!moveMultiTrack && multiTrackPlans.Count > 0 && apply && interactive)
        {
            moveMultiTrack = autoYes || AnsiConsole.Confirm($"Organize {multiTrackPlans.Count} multi-track set(s) into Title (Region) folders?");
        }

        if (!moveMultiDisc && multiDiscPlans.Count > 0 && apply && interactive)
        {
            moveMultiDisc = autoYes || AnsiConsole.Confirm($"Move {multiDiscPlans.Count} multi-disc set(s) into Title (Region) folders?");
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

        if (removeDuplicates && duplicateGroups.Count > 0 && apply && interactive)
        {
            var totalDupes = duplicateGroups.Sum(g => g.Files.Count - 1);
            removeDuplicates = autoYes || AnsiConsole.Confirm($"[red]Delete {totalDupes} duplicate file(s)?[/] (Keeps first in each group)");
        }

        if (!apply)
        {
            AnsiConsole.MarkupLine("[yellow]DRY-RUN:[/] Preview only. Use --apply to make changes.");
            return (int)ExitCode.OK;
        }

        var movedMultiTracks = moveMultiTrack ? ExecuteMultiTrackMoves(multiTrackPlans) : 0;
        var movedMultiDiscs = moveMultiDisc ? ExecuteMultiDiscMoves(multiDiscPlans) : 0;
        var createdCues = generateCues ? ExecuteCueCreations(cuePlans) : 0;
        var movedImports = moveIngest ? ExecuteIngestMoves(ingestPlans) : 0;
        var flattened = flattenSingles ? ExecuteFlattenMoves(flattenPlans, root) : 0;
        var removedDuplicates = removeDuplicates ? ExecuteDuplicateRemoval(duplicateGroups) : 0;

        AnsiConsole.MarkupLine("[green]Clean-up complete.[/]");
        AnsiConsole.MarkupLine($"  Multi-track sets organized: {movedMultiTracks}");
        AnsiConsole.MarkupLine($"  Multi-disc sets organized: {movedMultiDiscs}");
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
            ThrowIfCancelled();
            var baseFolder = BuildTitleRegionFolder(op.DiscInfo.Title, op.DiscInfo.Region);
            var discFolder = BuildDiscFolderName(baseFolder, op.DiscInfo.DiscNumber, op.DiscInfo.DiscCount);
            
            // If multiTrackDirName is blank, we want root/Title (Region)
            // If provided, we want root/MultiTrack/Title (Region)
            var containerDir = string.IsNullOrWhiteSpace(multiTrackDirName)
                ? root
                : Path.Combine(root, SanitizePathSegment(multiTrackDirName));
            
            var destDir = Path.Combine(containerDir, discFolder);

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

    private static List<MultiDiscMovePlan> PlanMultiDiscMoves(string root, bool recursive)
    {
        var parser = new PsxNameParser();
        var plans = new List<MultiDiscMovePlan>();

        var directories = Directory.GetDirectories(
                root,
                "*",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(dir => !dir.Equals(root, StringComparison.OrdinalIgnoreCase));

        foreach (var directory in directories)
        {
            ThrowIfCancelled();
            var files = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(file => PsxExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0)
            {
                continue;
            }

            var entries = files.Select(file => new DiscEntry(file, parser.Parse(file))).ToList();

            if (entries.Any(e => e.Info.IsMultiTrack))
            {
                continue;
            }

            var groups = entries
                .Where(e => e.Info.DiscNumber.HasValue)
                .GroupBy(e => (
                        Title: GetTitleOrFallback(e.Info, Path.GetFileName(directory)),
                        Region: GetRegionOrFallback(e.Info)),
                    new TitleRegionComparer())
                .Where(g => g.Select(entry => entry.Info.DiscNumber ?? 0).Distinct().Count() > 1);

            foreach (var group in groups)
            {
                var baseFolder = BuildTitleRegionFolder(group.Key.Title, group.Key.Region);
                var discPlans = group
                    .GroupBy(entry => entry.Info.DiscNumber ?? 1)
                    .OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        var discNumber = g.Key;
                        var discCount = g.First().Info.DiscCount ?? group.Count();
                        var destination = Path.Combine(root, baseFolder, BuildDiscFolderName(baseFolder, discNumber, discCount));
                        var fileList = g.Select(entry => entry.FilePath).ToList();
                        return new MultiDiscDiscPlan(discNumber, destination, fileList);
                    })
                    .ToList();

                if (discPlans.Count > 1)
                {
                    plans.Add(new MultiDiscMovePlan(baseFolder, discPlans));
                }
            }
        }

        return plans;
    }

    private static List<CueCreationPlan> PlanMissingCueCreations(string root, bool recursive)
    {
        var bins = Directory.EnumerateFiles(
                root,
                "*.bin",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .ToList();

        var groups = bins
            .Select(bin => new { Bin = bin, Cue = GetCandidateCuePath(bin) })
            .GroupBy(entry => entry.Cue, StringComparer.OrdinalIgnoreCase);

        var plans = new List<CueCreationPlan>();
        foreach (var group in groups)
        {
            ThrowIfCancelled();
            if (File.Exists(group.Key))
            {
                continue;
            }

            var trackPlans = BuildCueTrackPlans(group.Select(entry => entry.Bin));
            if (trackPlans.Count == 0)
            {
                continue;
            }

            plans.Add(new CueCreationPlan(group.Key, trackPlans));
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
            ThrowIfCancelled();
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
            ThrowIfCancelled();
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
        IReadOnlyCollection<MultiDiscMovePlan> multiDiscPlans,
        IReadOnlyCollection<CueCreationPlan> cuePlans,
        IReadOnlyCollection<IngestMovePlan> ingestPlans,
        IReadOnlyCollection<FlattenMovePlan> flattenPlans,
        List<DuplicateGroup> duplicateGroups)
    {
        var infoTable = new Table().Border(TableBorder.Rounded);
        infoTable.AddColumn("[cyan]Focus[/]");
        infoTable.AddColumn("[green]Count[/]");

        infoTable.AddRow("Multi-track sets", multiTrackPlans.Count.ToString("N0"));
        infoTable.AddRow("Multi-disc sets", multiDiscPlans.Count.ToString("N0"));
        infoTable.AddRow("Missing CUE files", cuePlans.Count.ToString("N0"));
        infoTable.AddRow("Import candidates", ingestPlans.Count.ToString("N0"));
        infoTable.AddRow("Flatten candidates", flattenPlans.Count.ToString("N0"));
        if (duplicateGroups.Count > 0)
        {
            var totalDupes = duplicateGroups.Sum(g => g.Files.Count - 1);
            infoTable.AddRow("Duplicate files", totalDupes.ToString("N0"));
        }

        AnsiConsole.Write(infoTable);

        if (multiDiscPlans.Count > 0)
        {
            var table = new Table { Title = new TableTitle("[magenta]Multi-Disc Sets[/]") };
            table.AddColumn("Title");
            table.AddColumn("Discs");
            foreach (var plan in multiDiscPlans.Take(5))
            {
                table.AddRow(plan.BaseFolder, plan.Discs.Count.ToString("N0"));
            }
            if (multiDiscPlans.Count > 5)
            {
                table.AddRow("[grey]...[/]", "[grey]...[/]");
            }
            AnsiConsole.Write(table);
        }

        if (cuePlans.Count > 0)
        {
            var table = new Table { Title = new TableTitle("[yellow]Missing CUEs[/]") };
            table.AddColumn("CUE");
            table.AddColumn("Source(s)");
            foreach (var plan in cuePlans.Take(5))
            {
                var preview = Path.GetFileName(plan.Tracks[0].BinPath).EscapeMarkup();
                if (plan.Tracks.Count > 1)
                {
                    preview = $"{preview} +{plan.Tracks.Count - 1}";
                }

                table.AddRow(Truncate(plan.CuePath), preview);
            }
            if (cuePlans.Count > 5)
            {
                table.AddRow("[grey]…[/]", "[grey]…[/]");
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

        if (duplicateGroups.Count > 0)
        {
            var table = new Table { Title = new TableTitle("[red]Duplicates[/]") };
            table.AddColumn("Hash");
            table.AddColumn("Title");
            table.AddColumn("Files");
            table.AddColumn("Size");
            foreach (var group in duplicateGroups.Take(5))
            {
                var dupeCount = group.Files.Count - 1; // First kept
                var size = group.Files.Skip(1).Sum(f => f.FileSize);
                var sizeStr = FormatBytes(size);
                
                table.AddRow(
                    group.Hash[..8], // First 8 chars of hash
                    group.Files[0].DiscInfo.Title ?? "Unknown",
                    dupeCount.ToString("N0"),
                    sizeStr);
            }
            if (duplicateGroups.Count > 5)
            {
                table.AddRow("[grey]…[/]", "[grey]…[/]", "[grey]…[/]", "[grey]…[/]");
            }
            AnsiConsole.Write(table);
        }
    }

    private static int ExecuteMultiTrackMoves(List<MultiTrackMovePlan> plans)
    {
        var moved = 0;
        foreach (var plan in plans)
        {
            ThrowIfCancelled();
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

    private static int ExecuteMultiDiscMoves(List<MultiDiscMovePlan> plans)
    {
        var moved = 0;
        foreach (var plan in plans)
        {
            ThrowIfCancelled();
            foreach (var disc in plan.Discs)
            {
                var targetDirectory = EnsureUniqueDirectory(disc.DestinationDirectory);
                foreach (var file in disc.Files)
                {
                    var destination = EnsureUniquePath(Path.Combine(targetDirectory, Path.GetFileName(file)));
                    File.Move(file, destination, overwrite: false);
                    moved++;
                }
            }
        }

        return moved;
    }

    private static int ExecuteCueCreations(List<CueCreationPlan> plans)
    {
        var created = 0;
        foreach (var plan in plans)
        {
            ThrowIfCancelled();
            var directory = Path.GetDirectoryName(plan.CuePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(plan.CuePath, BuildCueContents(plan));
            created++;
        }

        return created;
    }

    private static int ExecuteIngestMoves(List<IngestMovePlan> plans)
    {
        var moved = 0;
        foreach (var plan in plans)
        {
            ThrowIfCancelled();
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
            ThrowIfCancelled();
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

    private static int ExecuteDuplicateRemoval(List<DuplicateGroup> groups)
    {
        var removed = 0;
        foreach (var group in groups)
        {
            ThrowIfCancelled();
            // Keep first file, delete the rest
            foreach (var file in group.Files.Skip(1))
            {
                try
                {
                    File.Delete(file.FilePath);
                    removed++;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Failed to delete {file.FilePath.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
                }
            }
        }

        return removed;
    }

    private static List<CueTrackPlan> BuildCueTrackPlans(IEnumerable<string> binPaths)
    {
        var ordered = binPaths
            .Select(path => new
            {
                Path = path,
                Track = ParseTrackNumber(path)
            })
            .OrderBy(entry => entry.Track ?? int.MaxValue)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var plans = new List<CueTrackPlan>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var trackNumber = ordered[i].Track ?? (i + 1);
            if (trackNumber < 1)
            {
                trackNumber = i + 1;
            }

            plans.Add(new CueTrackPlan(ordered[i].Path, trackNumber));
        }

        return plans;
    }

    private static int? ParseTrackNumber(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var match = TrackNumberPattern.Match(fileName);
        if (match.Success && int.TryParse(match.Groups["track"].Value, out var track))
        {
            return track;
        }

        return null;
    }

    private static string BuildCueContents(CueCreationPlan plan)
    {
        var builder = new StringBuilder();
        foreach (var track in plan.Tracks)
        {
            var fileName = Path.GetFileName(track.BinPath);
            builder.AppendLine($"FILE \"{fileName}\" BINARY");

            var trackType = track.TrackNumber == 1 ? "MODE2/2352" : "AUDIO";
            builder.AppendLine($"  TRACK {track.TrackNumber:D2} {trackType}");
            builder.AppendLine("    INDEX 01 00:00:00");
        }

        return builder.ToString();
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

    private static string BuildTitleRegionFolder(string? title, string? region)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "Unknown Title" : title.Trim();
        var safeRegion = string.IsNullOrWhiteSpace(region) ? "Unknown" : region.Trim();
        return SanitizePathSegment($"{safeTitle} ({safeRegion})");
    }

    private static string BuildDiscFolderName(string baseFolder, int? discNumber, int? discCount)
    {
        if (discCount.GetValueOrDefault() > 1 || discNumber.HasValue)
        {
            var number = discNumber ?? 1;
            return SanitizePathSegment($"{baseFolder} (Disc {number})");
        }

        return baseFolder;
    }

    private static string GetTitleOrFallback(PsxDiscInfo info, string fallback)
        => string.IsNullOrWhiteSpace(info.Title) ? fallback : info.Title.Trim();

    private static string GetRegionOrFallback(PsxDiscInfo info)
        => string.IsNullOrWhiteSpace(info.Region) ? "Unknown" : info.Region.Trim();

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
    private sealed record MultiDiscMovePlan(string BaseFolder, IReadOnlyList<MultiDiscDiscPlan> Discs);
    private sealed record MultiDiscDiscPlan(int DiscNumber, string DestinationDirectory, IReadOnlyList<string> Files);
    private sealed record CueCreationPlan(string CuePath, IReadOnlyList<CueTrackPlan> Tracks);
    private sealed record CueTrackPlan(string BinPath, int TrackNumber);
    private sealed record IngestMovePlan(string SourcePath, string DestinationPath, string? Title, string? Region);
    private sealed record FlattenMovePlan(string Directory, IReadOnlyList<string> Files);
    private sealed record DiscEntry(string FilePath, PsxDiscInfo Info);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static void ThrowIfCancelled()
        => OperationContextScope.ThrowIfCancellationRequested();

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

