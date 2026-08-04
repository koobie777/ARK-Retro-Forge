using ARK.Core.Configuration;
using ARK.Core.Naming;
using ARK.Core.Scanning;
using ARK.Core.Units;

namespace ARK.Tests;

/// <summary>
/// Phase 4.1 defect 2: a correctly-structured disc image is not an anomaly.
/// </summary>
/// <remarks>
/// 1,762 of 1,765 PlayStation archives on the reference drive hold a <c>.bin</c> + <c>.cue</c>
/// pair. Reporting them under <c>multiple-roms-in-archive</c> was not wrong about the structure —
/// the cartridge resolver is right to refuse it — it was wrong about the meaning. "Anomaly" says
/// <i>this is malformed</i>; the truth is <i>no resolver handles this yet</i>.
/// </remarks>
public class UnsupportedFormatTests
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);

    private static CartridgeUnitResolver Resolver(FakeArchiveInspector inspector) =>
        new(Tokenizer, inspector, ScanRulesLoader.Load(TestFixtures.ShippedScanRulesPath()).DiscDescriptorExtensions);

    // Gate 8.
    [Theory]
    [InlineData(".cue", ".bin")]
    [InlineData(".gdi", ".bin")]
    [InlineData(".ccd", ".img")]
    public void Disc_image_archive_is_an_unsupported_format_not_an_anomaly(string descriptor, string data)
    {
        var unit = ResolveOne(new[]
        {
            new ArchiveEntry("Ridge Racer (USA)" + descriptor, 100),
            new ArchiveEntry("Ridge Racer (USA)" + data, 700_000_000),
        });

        Assert.True(unit.IsUnsupportedFormat);
        Assert.False(unit.HasAnomalies);

        var issue = Assert.Single(unit.UnsupportedFormats);
        Assert.Equal("disc-image", issue.Code);
        Assert.Contains("disc resolver", issue.Detail, StringComparison.OrdinalIgnoreCase);
    }

    // A multi-track disc is still one disc: one track sheet, many data files.
    [Fact]
    public void Multi_track_disc_image_is_still_one_unsupported_unit()
    {
        var entries = new List<ArchiveEntry> { new("Game (USA).cue", 100) };
        entries.AddRange(Enumerable.Range(1, 12).Select(i => new ArchiveEntry($"Game (USA) (Track {i}).bin", 1000)));

        var unit = ResolveOne(entries);

        Assert.True(unit.IsUnsupportedFormat);
        Assert.False(unit.HasAnomalies);
    }

    // Found on the real drive: 172 PSP PSN packages were being reported as anomalies. Several
    // files under a title-ID folder is one distributable item, not several games — and at 84% of
    // the anomaly list they were burying the genuine findings just as the disc images had.
    [Fact]
    public void Packaged_content_under_a_directory_layout_is_an_unsupported_format()
    {
        var unit = ResolveOne(new[]
        {
            new ArchiveEntry("ULUS10538/DATA.TPA", 1000),
            new ArchiveEntry("ULUS10538/PBOOT.PBP", 2000),
        });

        Assert.True(unit.IsUnsupportedFormat);
        Assert.False(unit.HasAnomalies);
        Assert.Equal("packaged-content", Assert.Single(unit.UnsupportedFormats).Code);
    }

    // A DLC package can reference more than one title ID and is still one item.
    [Fact]
    public void Packaged_content_spanning_two_title_ids_is_still_one_item()
    {
        var unit = ResolveOne(new[]
        {
            new ArchiveEntry("NPUG80303/PARAM.PBP", 100),
            new ArchiveEntry("UCES01313/DOWN01.EDAT", 200),
            new ArchiveEntry("UCES01313/ECHO01.BIN", 300),
        });

        Assert.True(unit.IsUnsupportedFormat);
        Assert.False(unit.HasAnomalies);
    }

    // Gate 11's other half: genuinely unrelated contents, flat and unnested, stay an anomaly.
    [Fact]
    public void Two_unrelated_roms_in_one_archive_remain_an_anomaly()
    {
        var unit = ResolveOne(new[]
        {
            new ArchiveEntry("Tetris (World).gb", 32768),
            new ArchiveEntry("Dr. Mario (World).gb", 65536),
        });

        Assert.True(unit.HasAnomalies);
        Assert.False(unit.IsUnsupportedFormat);
        Assert.Equal("multiple-roms-in-archive", Assert.Single(unit.Anomalies).Code);
    }

    // Two track sheets is two discs in one archive — ambiguous, so it is reported, not guessed.
    [Fact]
    public void Two_disc_images_in_one_archive_remain_an_anomaly()
    {
        var unit = ResolveOne(new[]
        {
            new ArchiveEntry("Game (USA) (Disc 1).cue", 100),
            new ArchiveEntry("Game (USA) (Disc 1).bin", 1000),
            new ArchiveEntry("Game (USA) (Disc 2).cue", 100),
            new ArchiveEntry("Game (USA) (Disc 2).bin", 1000),
        });

        Assert.True(unit.HasAnomalies);
        Assert.False(unit.IsUnsupportedFormat);
    }

    // Gate 10. This is the finding worth keeping: it caught five real corrupt downloads.
    [Fact]
    public void Unreadable_archive_remains_an_anomaly()
    {
        var inspector = new FakeArchiveInspector { Error = "Cannot determine compressed stream type." };
        var unit = Resolver(inspector).Resolve(Profile(), new[] { File("Broken (USA)") })[0];

        Assert.True(unit.HasAnomalies);
        Assert.False(unit.IsUnsupportedFormat);
        Assert.Equal("unreadable-archive", Assert.Single(unit.Anomalies).Code);
    }

    // Phase 4.1 asserted these 39 archives were unsupported-format, because the disc resolver was
    // stubbed. Phase 10 gate 12 inverts exactly that: a .bin + .cue archive is now a resolved disc
    // unit. The corrupt archive beside them must still stand out, which is what this always tested.
    [Fact]
    public void Report_resolves_a_field_of_disc_images_and_still_isolates_the_corrupt_archive()
    {
        var names = TestFixtures.ReadCorpus("real-names.txt").Take(40).ToArray();
        var directory = @"D:\Sony - PlayStation";
        var files = names
            .Select(name => new FileEntry(Path.Combine(directory, name + ".zip"), name + ".zip", ".zip", 1024, DateTimeOffset.UnixEpoch))
            .ToArray();

        var inspector = new FakeArchiveInspector();
        foreach (var file in files)
        {
            inspector.Entries[file.FullPath] = new[]
            {
                new ArchiveEntry("disc.cue", 100),
                new ArchiveEntry("disc.bin", 700_000_000),
            };
            inspector.SetEntry(file.FullPath, "disc.cue", CueBytes("disc.bin"));
        }

        inspector.Unreadable.Add(files[7].FullPath);

        var rules = ScanRulesLoader.Load(TestFixtures.ShippedScanRulesPath());
        var reader = new InMemoryFileSystemReader(new[] { new DirectoryListing(directory, "Sony - PlayStation", files) });
        var report = new ScanService(
            reader,
            new DirectoryProfiler(Tokenizer, rules),
            new IGameUnitResolver[] { new DiscUnitResolver(Tokenizer, inspector, reader), Resolver(inspector) },
            rules).Scan(directory);

        // Nothing unsupported any more: the disc resolver handles this shape now.
        Assert.Empty(report.UnsupportedFormats);
        Assert.Equal(39, report.Units.Count(unit => unit.Unit.Kind == GameUnitKind.Disc));

        // One anomaly, not forty. The corrupt archive is visible instead of being 1 line in 40.
        var anomaly = Assert.Single(report.Anomalies);
        Assert.Equal("unreadable-archive", Assert.Single(anomaly.Unit.Anomalies).Code);

        Assert.True(report.IsComplete);
        Assert.Equal(40, report.CandidateFileCount);
    }

    private static byte[] CueBytes(params string[] binNames) =>
        System.Text.Encoding.UTF8.GetBytes(string.Join(
            "\n",
            binNames.SelectMany((name, i) => new[]
            {
                $"FILE \"{name}\" BINARY",
                $"  TRACK {i + 1:00} MODE2/2352",
                "    INDEX 01 00:00:00",
            })));

    private static GameUnit ResolveOne(IReadOnlyList<ArchiveEntry> entries)
    {
        var file = File("Ridge Racer (USA)");
        var inspector = new FakeArchiveInspector();
        inspector.Entries[file.FullPath] = entries;
        return Resolver(inspector).Resolve(Profile(), new[] { file })[0];
    }

    private static FileEntry File(string name) =>
        new($@"D:\Set\{name}.zip", name + ".zip", ".zip", 1024, DateTimeOffset.UnixEpoch);

    private static DirectoryProfile Profile() => new(
        @"D:\Set", "Set", 1, ".zip", 1, 1, DirectoryOutcome.RomSet, ExclusionReason.None,
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<FileEntry>());

}
