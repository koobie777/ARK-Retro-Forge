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
        var flattenMultiTrack = args.Contains("--flatten-multitrack");
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
            new HeaderMetadata("Import Folder", string.IsNullOrWhiteSpace(ingestRoot) ? "n/a" : ingestRoot!));

        DatUsageHelper.WarnIfCatalogMissing("psx", "PSX clean");

        // Initialize DB for cache lookups
        var dbPath = Path.Combine(InstancePathResolver.GetInstanceRoot(), "db");
        await using var dbManager = new DatabaseManager(dbPath);
        await dbManager.InitializeAsync();
        var romRepository = new RomRepository(dbManager.GetConnection());

        List<MultiTrackMovePlan> multiTrackPlans = new();
        List<MultiDiscMovePlan> multiDiscPlans = new();
        List<CueCreationPlan> cuePlans = new();
        List<IngestMovePlan> ingestPlans = new();
        List<FlattenMovePlan> flattenPlans = new();
        List<DuplicateGroup> duplicateGroups = new();
        List<CollisionCleanupPlan> collisionPlans = new();

        var planningSteps = new List<(string Label, Func<ProgressTask, Task> Work)>
        {
            ("Planning multi-track sets", async task =>
            {
                multiTrackPlans = await PlanMultiTrackMovesAsync(root, recursive, multiTrackDirName, file => 
                {
                    var label = Truncate(Path.GetFileName(file), 35);
                    task.Description = $"Scanning: [grey]{label.PadRight(35)}[/]";
                });
                task.Description = "Planning multi-track sets";
            }),
            ("Planning multi-disc sets", async task =>
            {
                multiDiscPlans = await PlanMultiDiscMovesAsync(root, recursive, romRepository, multiTrackPlans, file => 
                {
                    var label = Truncate(Path.GetFileName(file), 35);
                    task.Description = $"Scanning: [grey]{label.PadRight(35)}[/]";
                });
                task.Description = "Planning multi-disc sets";
            }),
            ("Scanning for missing CUE files", _ =>
            {
                var handledFiles = multiTrackPlans.SelectMany(p => p.Files)
                    .Concat(multiDiscPlans.SelectMany(p => p.Discs.SelectMany(d => d.Files)))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                cuePlans = PlanMissingCueCreations(root, recursive, handledFiles);
                return Task.CompletedTask;
            }),
            ("Evaluating ingest directory", async _ =>
            {
                ingestPlans = await PlanIngestMovesAsync(root, ingestRoot, importDirName, romRepository);
            }),
            ("Checking folders safe to flatten", async _ =>
            {
                flattenPlans = await PlanFlattenMovesAsync(root, recursive, romRepository, flattenMultiTrack);
            }),
            ("Planning collision cleanup", _ =>
            {
                collisionPlans = PlanCollisionCleanup(root, recursive);
                return Task.CompletedTask;
            }),
            ("Scanning for duplicates", _ =>
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
            new TaskDescriptionColumn { Alignment = Justify.Left },
            new ProgressBarColumn { Width = 40, CompletedStyle = new Style(Color.SpringGreen1), RemainingStyle = new Style(Color.Grey35) },
            new SpinnerColumn()
        };

        await AnsiConsole.Progress()
            .AutoClear(true)
            .Columns(progressColumns)
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Analyzing PSX library", maxValue: planningSteps.Count);
                foreach (var (label, work) in planningSteps)
                {
                    ThrowIfCancelled();
                    task.Description = label;
                    await work(task);
                    task.Increment(1);
                }
            });

        RenderSummary(multiTrackPlans, multiDiscPlans, cuePlans, ingestPlans, flattenPlans, duplicateGroups, collisionPlans);

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

        if (interactive)
        {
            AnsiConsole.MarkupLine("[yellow]Press ENTER to execute changes...[/]");
            Console.ReadLine();
            AnsiConsole.Clear();
        }

        var movedMultiTracks = 0;
        var movedMultiDiscs = 0;
        var createdCues = 0;
        var movedImports = 0;
        var flattened = 0;
        var removedDuplicates = 0;

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var movedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (moveMultiTrack)
                {
                    var result = ExecuteMultiTrackMoves(ctx, multiTrackPlans);
                    movedMultiTracks = result.Count;
                    foreach (var kvp in result.MovedFiles)
                    {
                        movedFiles[kvp.Key] = kvp.Value;
                    }
                }
                if (moveMultiDisc)
                {
                    var result = ExecuteMultiDiscMoves(ctx, multiDiscPlans);
                    movedMultiDiscs = result.Count;
                    foreach (var kvp in result.MovedFiles)
                    {
                        movedFiles[kvp.Key] = kvp.Value;
                    }
                }
                if (generateCues)
                {
                    createdCues = ExecuteCueCreations(ctx, cuePlans, movedFiles);
                }
                if (moveIngest)
                {
                    var result = ExecuteIngestMoves(ctx, ingestPlans);
                    movedImports = result.Count;
                    foreach (var kvp in result.MovedFiles)
                    {
                        movedFiles[kvp.Key] = kvp.Value;
                    }
                }
                if (removeDuplicates)
                {
                    removedDuplicates = ExecuteDuplicateRemoval(ctx, duplicateGroups);
                }
                
                // Use a staging directory for flattening to ensure clean collision resolution
                var stagingDir = Path.Combine(root, ".ark-staging");
                if (flattenSingles)
                {
                    Directory.CreateDirectory(stagingDir);
                    var result = ExecuteFlattenMoves(ctx, flattenPlans, stagingDir, root);
                    flattened = result.Count;
                    foreach (var kvp in result.MovedFiles)
                    {
                        movedFiles[kvp.Key] = kvp.Value;
                    }
                    
                    // Commit staging back to root
                    CommitStaging(ctx, stagingDir, root);
                }
                
                // Re-plan collision cleanup as flattening/moving might have created new collisions
                var finalCollisionPlans = PlanCollisionCleanup(root, recursive);
                if (finalCollisionPlans.Count > 0)
                {
                    ExecuteCollisionCleanup(ctx, finalCollisionPlans);
                }
                
                // Cleanup staging if it still exists
                try 
                { 
                    if (Directory.Exists(stagingDir))
                    {
                        Directory.Delete(stagingDir, true);
                    }
                } 
                catch { }
                
                await Task.CompletedTask;
            });

        if (apply)
        {
            AnsiConsole.MarkupLine("[grey]Cleaning up empty directories...[/]");
            DeleteEmptyDirectories(root);
        }

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

    private static async Task<List<MultiTrackMovePlan>> PlanMultiTrackMovesAsync(string root, bool recursive, string multiTrackDirName, Action<string>? onProgress = null)
    {
        var planner = new PsxBinMergePlanner();
        var operations = await planner.PlanMergesAsync(root, recursive, outputDirectory: null, onProgress: onProgress);
        var plans = new List<MultiTrackMovePlan>();

        // 1. Convert CUE-based ops to a common format
        var sets = operations.Where(op => op.TrackSources.Count > 1)
            .Select(op => 
            {
                var files = op.TrackSources.Select(t => t.AbsolutePath).Where(File.Exists).ToList();
                if (File.Exists(op.CuePath))
                {
                    files.Add(op.CuePath);
                }
                
                return new TrackSet 
                { 
                    Title = op.Title, 
                    DiscInfo = op.DiscInfo, 
                    Files = files
                };
            })
            .ToList();

        // 2. Find loose multi-track files (orphans)
        var handledFiles = sets.SelectMany(s => s.Files).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var looseFiles = Directory.EnumerateFiles(root, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(f => PsxExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !handledFiles.Contains(f))
            .ToList();

        var parser = new PsxNameParser();
        var looseGroups = looseFiles
            .Select(f => new { File = f, BaseName = RemoveTrackSuffix(Path.GetFileNameWithoutExtension(f)) })
            .GroupBy(x => Path.Combine(Path.GetDirectoryName(x.File)!, x.BaseName), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && g.Any(x => TrackNumberPattern.IsMatch(Path.GetFileName(x.File))));

        foreach (var group in looseGroups)
        {
            var firstFile = group.First().File;
            var info = parser.Parse(firstFile);
            var title = string.IsNullOrWhiteSpace(info.Title) ? Path.GetFileNameWithoutExtension(firstFile) : info.Title;
            
            sets.Add(new TrackSet
            {
                Title = title,
                DiscInfo = info,
                Files = group.Select(x => x.File).ToList()
            });
        }

        var groups = sets.GroupBy(s => (Title: CleanTitle(GetTitleOrFallback(s.DiscInfo, "Unknown")), Region: GetRegionOrFallback(s.DiscInfo)), new TitleRegionComparer());

        foreach (var group in groups)
        {
            ThrowIfCancelled();
            var baseFolder = BuildTitleRegionFolder(group.Key.Title, group.Key.Region);

            // Calculate disc range for folder naming
            var discNumbers = group.Select(s => s.DiscInfo.DiscNumber ?? 0).Where(n => n > 0).OrderBy(n => n).ToList();
            var folderSuffix = "";
            if (discNumbers.Count > 0)
            {
                var min = discNumbers.First();
                var max = discNumbers.Last();
                folderSuffix = min == max ? $" (Disc {min})" : $" (Discs {min}-{max})";
            }

            // If multiTrackDirName is blank, we want root/Title (Region) (Discs X-Y)
            // If provided, we want root/MultiTrack/Title (Region) (Discs X-Y)
            var containerDir = string.IsNullOrWhiteSpace(multiTrackDirName)
                ? root
                : Path.Combine(root, SanitizePathSegment(multiTrackDirName));
            
            var targetFolderName = $"{baseFolder}{folderSuffix}";
            var destDir = Path.Combine(containerDir, targetFolderName);

            foreach (var set in group)
            {
                if (set.Files.Count == 0)
                {
                    continue;
                }

                plans.Add(new MultiTrackMovePlan(set.Title, group.Key.Region, destDir, set.Files));
            }
        }

        return plans;
    }

    private static async Task<List<MultiDiscMovePlan>> PlanMultiDiscMovesAsync(
        string root,
        bool recursive,
        RomRepository romRepository,
        IReadOnlyCollection<MultiTrackMovePlan> multiTrackPlans,
        Action<string>? onProgress = null)
    {
        var parser = new PsxNameParser();
        var plans = new List<MultiDiscMovePlan>();

        // Build set of files handled by multi-track planner to avoid conflicts
        var handledFiles = multiTrackPlans
            .SelectMany(p => p.Files)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allFiles = Directory.EnumerateFiles(
                root,
                "*.*",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(file => PsxExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var entries = new List<DiscEntry>();
        foreach (var file in allFiles)
        {
            ThrowIfCancelled();
            onProgress?.Invoke(file);

            var cached = await romRepository.GetByPathAsync(file);
            if (cached != null && !string.IsNullOrWhiteSpace(cached.Title))
            {
                // Use cached metadata but still check filename for track info (fast)
                var trackMatch = TrackNumberPattern.Match(Path.GetFileNameWithoutExtension(file));
                var isMultiTrack = trackMatch.Success;
                
                // Fallback: Check for disc number in filename if cache is missing it
                var discNumber = cached.Disc_Number;
                var discCount = cached.Disc_Count;
                if (!discNumber.HasValue)
                {
                    var discMatch = Regex.Match(Path.GetFileNameWithoutExtension(file), @"\(Disc (\d+)(?: of (\d+))?\)", RegexOptions.IgnoreCase);
                    if (discMatch.Success)
                    {
                        discNumber = int.Parse(discMatch.Groups[1].Value);
                        if (discMatch.Groups[2].Success)
                        {
                            discCount = int.Parse(discMatch.Groups[2].Value);
                        }
                    }
                }
                
                entries.Add(new DiscEntry(file, new PsxDiscInfo
                {
                    FilePath = file,
                    Title = cached.Title,
                    Region = cached.Region,
                    Serial = cached.Serial,
                    DiscNumber = discNumber,
                    DiscCount = discCount,
                    IsAudioTrack = isMultiTrack && int.Parse(trackMatch.Groups["track"].Value) > 1,
                    TrackNumber = isMultiTrack ? int.Parse(trackMatch.Groups["track"].Value) : null
                }));
            }
            else
            {
                var info = parser.Parse(file);
                // Fallback: If parser missed the disc number (e.g. it's buried in the title), try to extract it
                if (!info.DiscNumber.HasValue)
                {
                    var discMatch = Regex.Match(Path.GetFileNameWithoutExtension(file), @"\(Disc (\d+)(?: of (\d+))?\)", RegexOptions.IgnoreCase);
                    if (discMatch.Success)
                    {
                        info = info with 
                        { 
                            DiscNumber = int.Parse(discMatch.Groups[1].Value),
                            DiscCount = discMatch.Groups[2].Success ? int.Parse(discMatch.Groups[2].Value) : info.DiscCount
                        };
                    }
                }
                entries.Add(new DiscEntry(file, info));
            }
        }

        var groups = entries
            .Where(e => e.Info.DiscNumber.HasValue)
            .GroupBy(e => (
                    Title: CleanTitle(GetTitleOrFallback(e.Info, "Unknown")),
                    Region: GetRegionOrFallback(e.Info)),
                new TitleRegionComparer())
            .Where(g => g.Select(entry => entry.Info.DiscNumber ?? 0).Distinct().Count() > 1);

        foreach (var group in groups)
        {
            var baseFolder = BuildTitleRegionFolder(group.Key.Title, group.Key.Region);
            
            // Calculate disc range for folder naming
            var discNumbers = group.Select(e => e.Info.DiscNumber ?? 0).Where(n => n > 0).OrderBy(n => n).ToList();
            var folderSuffix = "";
            if (discNumbers.Count > 0)
            {
                var min = discNumbers.First();
                var max = discNumbers.Last();
                folderSuffix = min == max ? $" (Disc {min})" : $" (Discs {min}-{max})";
            }
            
            var targetFolderName = $"{baseFolder}{folderSuffix}";

            var discPlans = group
                .GroupBy(entry => entry.Info.DiscNumber ?? 1)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var discNumber = g.Key;
                    var discCount = g.First().Info.DiscCount ?? group.Count();
                    var destination = Path.Combine(root, targetFolderName);
                    
                    // Only include files that are NOT handled by the multi-track planner
                    var fileList = g
                        .Select(entry => entry.FilePath)
                        .Where(f => !handledFiles.Contains(f))
                        .ToList();
                        
                    return new MultiDiscDiscPlan(discNumber, destination, fileList);
                })
                .Where(p => p.Files.Count > 0) // Only add plan if there are files to move
                .ToList();

            if (discPlans.Count > 0)
            {
                plans.Add(new MultiDiscMovePlan(group.Key.Title, group.Key.Region, targetFolderName, discPlans));
            }
        }

        return plans;
    }

    private static List<CueCreationPlan> PlanMissingCueCreations(string root, bool recursive, HashSet<string> handledFiles)
    {
        var bins = Directory.EnumerateFiles(
                root,
                "*.bin",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(f => !handledFiles.Contains(f))
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

            if (IsReferencedByAnyCue(group.Key, group.Select(g => g.Bin)))
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

    private static async Task<List<FlattenMovePlan>> PlanFlattenMovesAsync(string root, bool recursive, RomRepository romRepository, bool flattenMultiTrack)
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

            var discs = new List<PsxDiscInfo>();
            foreach (var file in files)
            {
                var cached = await romRepository.GetByPathAsync(file);
                if (cached != null && !string.IsNullOrWhiteSpace(cached.Title))
                {
                    var trackMatch = TrackNumberPattern.Match(Path.GetFileNameWithoutExtension(file));
                    var isMultiTrack = trackMatch.Success;
                    
                    discs.Add(new PsxDiscInfo
                    {
                        FilePath = file,
                        Title = cached.Title,
                        Region = cached.Region,
                        Serial = cached.Serial,
                        DiscNumber = cached.Disc_Number,
                        DiscCount = cached.Disc_Count,
                        IsAudioTrack = isMultiTrack && int.Parse(trackMatch.Groups["track"].Value) > 1,
                        TrackNumber = isMultiTrack ? int.Parse(trackMatch.Groups["track"].Value) : null
                    });
                }
                else
                {
                    discs.Add(parser.Parse(file));
                }
            }

            // Skip directories containing true multi-disc sets
            if (ContainsTrueMultiDiscSet(discs))
            {
                continue;
            }

            // Skip multi-track sets unless explicitly requested
            if (!flattenMultiTrack && ContainsMultiTrackSet(discs))
            {
                continue;
            }

            plans.Add(new FlattenMovePlan(directory, files));
        }

        // Also check root for files that need normalization (renaming)
        var rootFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly)
            .Where(file => PsxExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (rootFiles.Count > 0)
        {
            var filesToRename = rootFiles.Where(f => 
            {
                var name = Path.GetFileNameWithoutExtension(f);
                return !string.Equals(name, RemoveDuplicateMarkers(name), StringComparison.Ordinal);
            }).ToList();

            if (filesToRename.Count > 0)
            {
                plans.Add(new FlattenMovePlan(root, filesToRename));
            }
        }

        return plans;
    }

    private static async Task<List<IngestMovePlan>> PlanIngestMovesAsync(
        string root,
        string? ingestRoot,
        string importDirName,
        RomRepository romRepository)
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
        List<DuplicateGroup> duplicateGroups,
        List<CollisionCleanupPlan> collisionPlans)
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
        if (collisionPlans.Count > 0)
        {
            var totalCollisionFiles = collisionPlans.Sum(p => p.FilesToDelete.Count);
            infoTable.AddRow("Collision duplicates", totalCollisionFiles.ToString("N0"));
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

        if (collisionPlans.Count > 0)
        {
            var table = new Table { Title = new TableTitle("[red]Collision Cleanup[/]") };
            table.AddColumn("Folder");
            table.AddColumn("Files to Delete");
            foreach (var plan in collisionPlans.Take(5))
            {
                table.AddRow(Truncate(plan.Directory), plan.FilesToDelete.Count.ToString("N0"));
            }
            if (collisionPlans.Count > 5)
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

    private static (int Count, Dictionary<string, string> MovedFiles) ExecuteMultiTrackMoves(ProgressContext ctx, List<MultiTrackMovePlan> plans)
    {
        var task = ctx.AddTask($"Moving {plans.Count} multi-track sets", maxValue: plans.Count);
        var moved = 0;
        var movedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans)
        {
            ThrowIfCancelled();
            var targetDirectory = EnsureDirectory(plan.DestinationDirectory);
            foreach (var file in plan.Files)
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                // Enforce standard naming: Title (Region) (Track XX).ext
                var trackMatch = TrackNumberPattern.Match(Path.GetFileName(file));
                var extension = Path.GetExtension(file);
                string targetFilename;
                
                if (trackMatch.Success)
                {
                    var trackNum = int.Parse(trackMatch.Groups["track"].Value);
                    targetFilename = SanitizeFilename(plan.Title, plan.Region, $"(Track {trackNum:D2})", extension);
                }
                else if (extension.Equals(".cue", StringComparison.OrdinalIgnoreCase))
                {
                    targetFilename = SanitizeFilename(plan.Title, plan.Region, null, ".cue");
                    
                    var content = FixCueContent(file, originalRef => {
                         var tm = TrackNumberPattern.Match(originalRef);
                         if (tm.Success) {
                             var tn = int.Parse(tm.Groups["track"].Value);
                             return SanitizeFilename(plan.Title, plan.Region, $"(Track {tn:D2})", Path.GetExtension(originalRef));
                         }
                         return SanitizeFilename(plan.Title, plan.Region, null, Path.GetExtension(originalRef));
                    });
                    
                    var dest = SmartWrite(content, targetDirectory, targetFilename);
                    File.Delete(file);
                    movedFiles[Path.GetFullPath(file)] = dest;
                    moved++;
                    task.Increment(1);
                    continue;
                }
                else
                {
                    // Fallback for non-track files (shouldn't happen in multi-track plan usually)
                    targetFilename = Path.GetFileName(file);
                }

                var targetPath = Path.Combine(targetDirectory, targetFilename);
                
                // Handle collision if target exists (SmartMove logic inline-ish)
                if (File.Exists(targetPath))
                {
                    if (string.Equals(file, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    
                    // If sizes match, assume duplicate and delete source
                    try 
                    {
                        if (new FileInfo(file).Length == new FileInfo(targetPath).Length)
                        {
                            File.Delete(file);
                            continue;
                        }
                    } catch {}
                    
                    // If different, ensure unique path
                    targetPath = EnsureUniquePath(targetPath);
                }

                File.Move(file, targetPath, overwrite: false);
                movedFiles[Path.GetFullPath(file)] = targetPath;
            }

            // Clean up source directories if empty
            foreach (var file in plan.Files)
            {
                TryDeleteEmptyDirectory(Path.GetDirectoryName(file));
            }

            moved++;
            task.Increment(1);
        }

        return (moved, movedFiles);
    }

    private static (int Count, Dictionary<string, string> MovedFiles) ExecuteMultiDiscMoves(ProgressContext ctx, List<MultiDiscMovePlan> plans)
    {
        var totalDiscs = plans.Sum(p => p.Discs.Count);
        var task = ctx.AddTask($"Moving {totalDiscs} multi-disc sets", maxValue: totalDiscs);
        var moved = 0;
        var movedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans)
        {
            ThrowIfCancelled();
            foreach (var disc in plan.Discs)
            {
                var targetDirectory = EnsureDirectory(disc.DestinationDirectory);
                foreach (var file in disc.Files)
                {
                    if (!File.Exists(file))
                    {
                        continue;
                    }

                    // Enforce standard naming: Title (Region) (Disc X).ext
                    // Or Title (Region) (Disc X) (Track XX).ext if multi-track
                    var trackMatch = TrackNumberPattern.Match(Path.GetFileName(file));
                    var extension = Path.GetExtension(file);
                    string targetFilename;

                    if (trackMatch.Success)
                    {
                        var trackNum = int.Parse(trackMatch.Groups["track"].Value);
                        targetFilename = SanitizeFilename(plan.Title, plan.Region, $"(Disc {disc.DiscNumber}) (Track {trackNum:D2})", extension);
                    }
                    else if (extension.Equals(".cue", StringComparison.OrdinalIgnoreCase))
                    {
                        targetFilename = SanitizeFilename(plan.Title, plan.Region, $"(Disc {disc.DiscNumber})", ".cue");
                        
                        var content = FixCueContent(file, originalRef => {
                             var tm = TrackNumberPattern.Match(originalRef);
                             if (tm.Success) {
                                 var tn = int.Parse(tm.Groups["track"].Value);
                                 return SanitizeFilename(plan.Title, plan.Region, $"(Disc {disc.DiscNumber}) (Track {tn:D2})", Path.GetExtension(originalRef));
                             }
                             return SanitizeFilename(plan.Title, plan.Region, $"(Disc {disc.DiscNumber})", Path.GetExtension(originalRef));
                        });
                        
                        var dest = SmartWrite(content, targetDirectory, targetFilename);
                        File.Delete(file);
                        movedFiles[Path.GetFullPath(file)] = dest;
                        moved++;
                        continue;
                    }
                    else
                    {
                        // Standard single-bin multi-disc file
                        targetFilename = SanitizeFilename(plan.Title, plan.Region, $"(Disc {disc.DiscNumber})", extension);
                    }

                    var targetPath = Path.Combine(targetDirectory, targetFilename);

                    // Handle collision
                    if (File.Exists(targetPath))
                    {
                        if (string.Equals(file, targetPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        try 
                        {
                            if (new FileInfo(file).Length == new FileInfo(targetPath).Length)
                            {
                                File.Delete(file);
                                continue;
                            }
                        } catch {}

                        targetPath = EnsureUniquePath(targetPath);
                    }

                    File.Move(file, targetPath, overwrite: false);
                    movedFiles[Path.GetFullPath(file)] = targetPath;
                    moved++;
                }

                // Clean up source directories if empty
                foreach (var file in disc.Files)
                {
                    TryDeleteEmptyDirectory(Path.GetDirectoryName(file));
                }
                task.Increment(1);
            }
        }

        return (moved, movedFiles);
    }

    private static int ExecuteCueCreations(ProgressContext ctx, List<CueCreationPlan> plans, Dictionary<string, string> movedFiles)
    {
        var task = ctx.AddTask($"Generating {plans.Count} CUE files", maxValue: plans.Count);
        var created = 0;
        foreach (var plan in plans)
        {
            ThrowIfCancelled();
            
            try
            {
                // Determine if any tracks moved
                var firstMovedTrack = plan.Tracks.FirstOrDefault(t => movedFiles.ContainsKey(Path.GetFullPath(t.BinPath)));
                var targetDirectory = Path.GetDirectoryName(plan.CuePath);
                var cuePath = plan.CuePath;

                if (firstMovedTrack != null)
                {
                    // If tracks moved, place CUE alongside the first track
                    var newBinPath = movedFiles[Path.GetFullPath(firstMovedTrack.BinPath)];
                    targetDirectory = Path.GetDirectoryName(newBinPath);
                    cuePath = Path.Combine(targetDirectory!, Path.GetFileName(plan.CuePath));
                }

                if (string.IsNullOrWhiteSpace(targetDirectory))
                {
                    continue;
                }

                if (File.Exists(cuePath))
                {
                    continue;
                }

                Directory.CreateDirectory(targetDirectory);
                File.WriteAllText(cuePath, BuildCueContents(plan, movedFiles));
                created++;
            }
            catch (Exception)
            {
                // Log warning but continue
                // AnsiConsole.MarkupLine($"[yellow]Warning: Failed to generate CUE for {Path.GetFileName(plan.CuePath).EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
            }
            task.Increment(1);
        }

        return created;
    }

    private static (int Count, Dictionary<string, string> MovedFiles) ExecuteIngestMoves(ProgressContext ctx, List<IngestMovePlan> plans)
    {
        var task = ctx.AddTask($"Importing {plans.Count} ROMs", maxValue: plans.Count);
        var moved = 0;
        var movedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans)
        {
            ThrowIfCancelled();
            var directory = Path.GetDirectoryName(plan.DestinationPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            if (!File.Exists(plan.SourcePath))
            {
                task.Increment(1);
                continue;
            }

            Directory.CreateDirectory(directory);
            var destination = SmartMove(plan.SourcePath, directory);
            movedFiles[Path.GetFullPath(plan.SourcePath)] = destination;
            moved++;
            task.Increment(1);
        }

        return (moved, movedFiles);
    }

    private static void CommitStaging(ProgressContext ctx, string stagingDir, string root)
    {
        var files = Directory.GetFiles(stagingDir, "*.*", SearchOption.TopDirectoryOnly);
        var task = ctx.AddTask($"Committing {files.Length} files from staging", maxValue: files.Length);
        
        foreach (var file in files)
        {
            SmartMove(file, root);
            task.Increment(1);
        }
    }

    private static (int Count, Dictionary<string, string> MovedFiles) ExecuteFlattenMoves(ProgressContext ctx, List<FlattenMovePlan> plans, string stagingDir, string finalDir)
    {
        var task = ctx.AddTask($"Flattening {plans.Count} folders", maxValue: plans.Count);
        var flattened = 0;
        var movedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans)
        {
            ThrowIfCancelled();
            foreach (var source in plan.Files)
            {
                if (!File.Exists(source))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(source);
                var ext = Path.GetExtension(source);
                var cleanName = RemoveDuplicateMarkers(name);
                var targetName = cleanName + ext;
                var finalPath = Path.Combine(finalDir, targetName);

                if (ext.Equals(".cue", StringComparison.OrdinalIgnoreCase))
                {
                     var content = FixCueContent(source, originalName => {
                         var n = Path.GetFileNameWithoutExtension(originalName);
                         var e = Path.GetExtension(originalName);
                         return RemoveDuplicateMarkers(n) + e;
                     });
                     
                     // Optimize: Write directly to final if safe (no collision)
                     string dest;
                     if (!File.Exists(finalPath))
                     {
                         dest = SmartWrite(content, finalDir, targetName);
                     }
                     else
                     {
                         dest = SmartWrite(content, stagingDir, targetName);
                     }

                     File.Delete(source);
                     movedFiles[Path.GetFullPath(source)] = dest;
                     continue;
                }

                // Optimize: Move directly to final if safe (no collision)
                if (!File.Exists(finalPath))
                {
                    var destination = SmartMove(source, finalDir, targetName);
                    movedFiles[Path.GetFullPath(source)] = destination;
                }
                else
                {
                    var destination = SmartMove(source, stagingDir, targetName);
                    movedFiles[Path.GetFullPath(source)] = destination;
                }
            }

            // Remove directory if empty after moves (but never delete root or staging)
            if (string.Equals(Path.GetFullPath(plan.Directory), Path.GetFullPath(finalDir), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFullPath(plan.Directory), Path.GetFullPath(stagingDir), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(plan.Directory).Any())
                {
                    Directory.Delete(plan.Directory);
                }
            }
            catch (Exception) { /* Ignore */ }

            flattened++;
            task.Increment(1);
        }

        return (flattened, movedFiles);
    }

    private static int ExecuteDuplicateRemoval(ProgressContext ctx, List<DuplicateGroup> groups)
    {
        var totalDupes = groups.Sum(g => g.Files.Count - 1);
        var task = ctx.AddTask($"Deleting {totalDupes} duplicates", maxValue: totalDupes);
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
                task.Increment(1);
            }
        }

        return removed;
    }

    private static List<CollisionCleanupPlan> PlanCollisionCleanup(string root, bool recursive)
    {
        var plans = new List<CollisionCleanupPlan>();
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var directories = Directory.GetDirectories(root, "*", searchOption).ToList();
        directories.Add(root);

        foreach (var dir in directories)
        {
            ThrowIfCancelled();
            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var cues = files.Where(f => Path.GetExtension(f).Equals(".cue", StringComparison.OrdinalIgnoreCase)).ToList();
            
            if (cues.Count < 2)
            {
                continue;
            }

            var filesToDelete = new List<string>();
            
            // Group by canonical name (stripping " (N)" suffix AND " (1)" style duplicates)
            var groups = cues.GroupBy(c => 
            {
                var name = Path.GetFileNameWithoutExtension(c);
                // Remove (1), (2) etc.
                name = Regex.Replace(name, @" \(\d+\)$", "");
                // Remove (Track XX) if present (though CUEs usually don't have it, some might)
                name = Regex.Replace(name, @" \(Track \d+\)", "", RegexOptions.IgnoreCase);
                return name.Trim();
            }, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                if (group.Count() < 2)
                {
                    continue;
                }

                // We found a collision set.
                // Sort by length (shortest is likely original) then alphabetically
                var sorted = group.OrderBy(path => Path.GetFileNameWithoutExtension(path).Length)
                                  .ThenBy(path => path)
                                  .ToList();

                // Keep the first one
                var keeper = sorted[0];
                var duplicates = sorted.Skip(1);

                foreach (var dupeCue in duplicates)
                {
                    filesToDelete.Add(dupeCue);
                    
                    // Also find associated files with the same stem
                    var stem = Path.GetFileNameWithoutExtension(dupeCue);
                    var associated = files.Where(f => 
                        !f.Equals(dupeCue, StringComparison.OrdinalIgnoreCase) && 
                        Path.GetFileNameWithoutExtension(f).Equals(stem, StringComparison.OrdinalIgnoreCase));
                    
                    filesToDelete.AddRange(associated);
                }
            }

            if (filesToDelete.Count > 0)
            {
                plans.Add(new CollisionCleanupPlan(dir, filesToDelete));
            }
        }

        return plans;
    }

    private static int ExecuteCollisionCleanup(ProgressContext ctx, List<CollisionCleanupPlan> plans)
    {
        var totalFiles = plans.Sum(p => p.FilesToDelete.Count);
        var task = ctx.AddTask($"Cleaning {totalFiles} collision duplicates", maxValue: totalFiles);
        var removed = 0;

        foreach (var plan in plans)
        {
            ThrowIfCancelled();
            foreach (var file in plan.FilesToDelete)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Failed to delete {Path.GetFileName(file).EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
                }
                task.Increment(1);
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

    private static string BuildCueContents(CueCreationPlan plan, Dictionary<string, string>? movedFiles = null)
    {
        var builder = new StringBuilder();
        foreach (var track in plan.Tracks)
        {
            var binPath = track.BinPath;
            if (movedFiles != null && movedFiles.TryGetValue(binPath, out var newPath))
            {
                binPath = newPath;
            }

            var fileName = Path.GetFileName(binPath);
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
        
        // 1. Strip Serials: [SLUS-XXXX], (SLUS-XXXX), [SCES XXXX], etc.
        safeTitle = Regex.Replace(safeTitle, @"\s*\[.*?\]", "");
        safeTitle = Regex.Replace(safeTitle, @"\s*\([A-Z]{4}[-_]\d+\)", "", RegexOptions.IgnoreCase);

        // 2. Extract Suffix Tags (Rev, Ver, Languages, etc.)
        // We look for parenthesized groups at the end of the string
        var suffixes = new Stack<string>();
        while (true)
        {
            var match = Regex.Match(safeTitle, @"\s+(\([^)]+\))$");
            if (!match.Success)
            {
                break;
            }

            var tag = match.Groups[1].Value;
            
            // If the tag is exactly the region, we'll handle it via safeRegion
            if (!tag.Equals($"({safeRegion})", StringComparison.OrdinalIgnoreCase))
            {
                suffixes.Push(tag);
            }
            
            safeTitle = safeTitle.Substring(0, match.Index).Trim();
        }

        // 3. Construct: Title (Region) Suffixes
        var sb = new StringBuilder();
        sb.Append(safeTitle);
        
        if (!safeRegion.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append($" ({safeRegion})");
        }
        else if (suffixes.Count == 0 && !safeTitle.Contains('(')) 
        {
            // Only append Unknown if we have no other tags and no region
            sb.Append(" (Unknown)");
        }

        while (suffixes.Count > 0)
        {
            var tag = suffixes.Pop();
            // Filter out (Unknown)
            if (tag.Equals("(Unknown)", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            
            // Filter out (Rev X), (v1.0), etc if requested
            if (Regex.IsMatch(tag, @"^\((?:Rev|Ver|v)\s*[\d.]+\)$", RegexOptions.IgnoreCase))
            {
                continue;
            }

            sb.Append(' ').Append(tag);
        }

        return SanitizePathSegment(sb.ToString());
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

    private static string EnsureDirectory(string destinationDirectory)
    {
        if (!Directory.Exists(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }
        return destinationDirectory;
    }

    private static void TryDeleteEmptyDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }
        
        try 
        {
            // Check if empty of files and directories
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        } 
        catch 
        { 
            // Best effort 
        }
    }

    private static void DeleteEmptyDirectories(string startLocation)
    {
        foreach (var directory in Directory.GetDirectories(startLocation))
        {
            DeleteEmptyDirectories(directory);
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, false);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { } 
        }
    }

    private static string SmartMove(string sourceFile, string targetDirectory, string? targetFilename = null)
    {
        var filename = targetFilename ?? Path.GetFileName(sourceFile);
        var targetPath = Path.Combine(targetDirectory, filename);

        if (File.Exists(targetPath))
        {
            if (string.Equals(sourceFile, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return targetPath;
            }

            try
            {
                var sourceInfo = new FileInfo(sourceFile);
                var destInfo = new FileInfo(targetPath);

                if (sourceInfo.Length == destInfo.Length)
                {
                    File.Delete(sourceFile);
                    return targetPath;
                }
            }
            catch
            {
                // Ignore errors, fall through to unique path
            }
        }

        var destination = EnsureUniquePath(targetPath);
        File.Move(sourceFile, destination, overwrite: false);
        return destination;
    }

    private static string CleanTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title;
        }
        var clean = RemoveDuplicateMarkers(title.Trim());
        // Also remove (Disc X) if it leaked into the title (common with some parsers)
        clean = Regex.Replace(clean, @"\s*\(Disc\s*\d+(?:\s*of\s*\d+)?\)", "", RegexOptions.IgnoreCase);
        return clean.Trim();
    }

    private static string RemoveDuplicateMarkers(string name)
    {
        var clean = name;
        // Recursively remove (1), (2) etc at the end
        while (Regex.IsMatch(clean, @"\s*\(\d+\)$"))
        {
            clean = Regex.Replace(clean, @"\s*\(\d+\)$", "");
        }
        return clean;
    }

    private static string SanitizeFilename(string title, string region, string? suffix = null, string extension = ".bin")
    {
        var cleanTitle = CleanTitle(title);
        var sb = new StringBuilder();
        sb.Append(cleanTitle);
        if (!string.IsNullOrWhiteSpace(region) && !region.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append($" ({region.Trim()})");
        }
        
        if (!string.IsNullOrWhiteSpace(suffix))
        {
            sb.Append($" {suffix.Trim()}");
        }

        sb.Append(extension);
        
        var filename = sb.ToString();
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            filename = filename.Replace(ch, '_');
        }
        return filename;
    }

    private sealed class TrackSet
    {
        public required string Title { get; init; }
        public required PsxDiscInfo DiscInfo { get; init; }
        public required List<string> Files { get; init; }
    }

    private sealed record MultiTrackMovePlan(string Title, string Region, string DestinationDirectory, IReadOnlyList<string> Files);
    private sealed record MultiDiscMovePlan(string Title, string Region, string BaseFolder, IReadOnlyList<MultiDiscDiscPlan> Discs);
    private sealed record MultiDiscDiscPlan(int DiscNumber, string DestinationDirectory, IReadOnlyList<string> Files);
    private sealed record CueCreationPlan(string CuePath, IReadOnlyList<CueTrackPlan> Tracks);
    private sealed record CueTrackPlan(string BinPath, int TrackNumber);
    private sealed record IngestMovePlan(string SourcePath, string DestinationPath, string? Title, string? Region);
    private sealed record FlattenMovePlan(string Directory, IReadOnlyList<string> Files);
    private sealed record CollisionCleanupPlan(string Directory, IReadOnlyList<string> FilesToDelete);
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

    private static bool ContainsMultiTrackSet(IReadOnlyList<PsxDiscInfo> discs)
    {
        // If we have explicit track numbers > 1 or audio tracks, it's multi-track
        if (discs.Any(d => d.TrackNumber > 1 || d.IsAudioTrack))
        {
            return true;
        }

        // If we have multiple binary files (BIN/ISO/IMG) that aren't distinct discs,
        // they are likely tracks of the same disc.
        var binCount = discs.Count(d => 
            Path.GetExtension(d.FilePath).Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(d.FilePath).Equals(".iso", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(d.FilePath).Equals(".img", StringComparison.OrdinalIgnoreCase));
            
        if (binCount > 1)
        {
             return true;
        }

        return false;
    }

    private static bool ContainsTrueMultiDiscSet(IReadOnlyList<PsxDiscInfo> discs)
    {
        var groups = discs
            .Where(d => !string.IsNullOrWhiteSpace(d.Title) && !string.IsNullOrWhiteSpace(d.Region))
            .GroupBy(d => (Title: d.Title!.Trim(), Region: d.Region!.Trim()), new TitleRegionComparer());

        foreach (var group in groups)
        {
            // Check for explicit DiscCount > 1
            if (group.Any(d => d.DiscCount > 1))
            {
                return true;
            }

            // Check for multiple distinct disc numbers (e.g. Disc 1 and Disc 2)
            var distinctDiscs = group
                .Where(d => d.DiscNumber.HasValue)
                .Select(d => d.DiscNumber!.Value)
                .Distinct()
                .ToList();

            if (distinctDiscs.Count > 1)
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

    private static bool IsReferencedByAnyCue(string candidateCuePath, IEnumerable<string> binPaths)
    {
        var directory = Path.GetDirectoryName(candidateCuePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        var existingCues = Directory.GetFiles(directory, "*.cue");
        foreach (var cue in existingCues)
        {
            if (string.Equals(cue, candidateCuePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(cue);
                foreach (var bin in binPaths)
                {
                    var binName = Path.GetFileName(bin);
                    if (content.Contains(binName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore read errors
            }
        }

        return false;
    }

    private static string SmartWrite(string content, string targetDirectory, string targetFilename)
    {
        var targetPath = Path.Combine(targetDirectory, targetFilename);

        if (File.Exists(targetPath))
        {
            try
            {
                if (File.ReadAllText(targetPath) == content)
                {
                    return targetPath;
                }
            }
            catch { }
            
            targetPath = EnsureUniquePath(targetPath);
        }

        File.WriteAllText(targetPath, content);
        return targetPath;
    }

    private static string FixCueContent(string cuePath, Func<string, string> renameFunc)
    {
        try
        {
            var lines = File.ReadAllLines(cuePath);
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, "FILE \"(.+?)\"");
                    if (match.Success)
                    {
                        var original = match.Groups[1].Value;
                        var newName = renameFunc(original);
                        sb.AppendLine(line.Replace($"\"{original}\"", $"\"{newName}\""));
                        continue;
                    }
                }
                sb.AppendLine(line);
            }
            return sb.ToString();
        }
        catch
        {
            return File.ReadAllText(cuePath);
        }
    }
}

