using System.Text;
using ARK.Core.Configuration;
using ARK.Core.Naming;
using ARK.Core.Scanning;
using ARK.Core.Units;

namespace ARK.Tests;

/// <summary>
/// Phase 10 — disc units. Written before the implementation, per the Definition of Done.
/// </summary>
/// <remarks>
/// This is where v1 died: multi-track read as multi-disc, multi-disc read as variants, cue sheets
/// rewritten from assumptions. Every test here pins one of those three apart from the others.
/// </remarks>
public class DiscUnitTests
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);

    // ---- Gate 4: membership comes from parsing the cue, never from filename similarity ----

    [Fact]
    public void Cue_parser_reads_quoted_and_unquoted_file_names()
    {
        var sheet = CueSheet.Parse(string.Join("\n",
            "REM GENRE Racing",
            "FILE \"Ridge Racer (USA) (Track 1).bin\" BINARY",
            "  TRACK 01 MODE2/2352",
            "    INDEX 01 00:00:00",
            "FILE Unquoted Name With Spaces.bin BINARY",
            "  TRACK 02 AUDIO",
            "    INDEX 01 00:00:00"));

        Assert.Equal(2, sheet.Files.Count);
        Assert.Equal("Ridge Racer (USA) (Track 1).bin", sheet.Files[0].Name);
        Assert.Equal("BINARY", sheet.Files[0].Format);
        Assert.Equal("Unquoted Name With Spaces.bin", sheet.Files[1].Name);
        Assert.Equal(2, sheet.TrackCount);
    }

    // Gate 4 in its load-bearing form. The cue names a BIN that shares nothing with the archive's
    // name, and a similarly-named BIN sits beside it belonging to something else. Membership must
    // follow the sheet, not the resemblance.
    [Fact]
    public void Membership_follows_the_cue_not_the_resemblance()
    {
        var directory = @"D:\Sony - PlayStation";
        var cue = File(directory, "Ridge Racer (USA)", ".cue");
        var named = File(directory, "RR-DATA-01", ".bin");
        var lookalike = File(directory, "Ridge Racer (USA) (Track 2)", ".bin");

        var reader = Reader(directory, new[] { cue, named, lookalike },
            (cue.FullPath, Cue("RR-DATA-01.bin")));

        var units = Resolver(reader, NoArchives()).Resolve(Profile(directory), new[] { cue, named, lookalike });

        var disc = Assert.Single(units, unit => unit.PrimaryPath == cue.FullPath);
        Assert.Equal(new[] { cue.FullPath, named.FullPath }, disc.Files.Select(file => file.FullPath));
        Assert.DoesNotContain(lookalike.FullPath, disc.Files.Select(file => file.FullPath));
    }

    // ---- Gates 3 and 14: a cue plus its BINs is one unit, resolved without an archive ----

    [Fact]
    public void Loose_cue_and_its_bins_resolve_to_one_unit()
    {
        var directory = @"D:\Sony - PlayStation";
        var cue = File(directory, "Ridge Racer (USA)", ".cue");
        var track = File(directory, "Ridge Racer (USA)", ".bin");

        var reader = Reader(directory, new[] { cue, track }, (cue.FullPath, Cue("Ridge Racer (USA).bin")));
        var units = Resolver(reader, NoArchives()).Resolve(Profile(directory), new[] { cue, track });

        var unit = Assert.Single(units);
        Assert.Equal(GameUnitKind.Disc, unit.Kind);
        Assert.Equal(2, unit.Files.Count);
        Assert.False(unit.HasAnomalies);
    }

    // ---- Gate 5: track count never implies disc count ----

    [Fact]
    public void A_cue_with_twelve_tracks_is_one_unit_not_twelve()
    {
        var directory = @"D:\Sony - PlayStation";
        var cue = File(directory, "Wipeout (Europe)", ".cue");
        var tracks = Enumerable.Range(1, 12)
            .Select(number => File(directory, $"Wipeout (Europe) (Track {number})", ".bin"))
            .ToArray();

        var files = new[] { cue }.Concat(tracks).ToArray();
        var reader = Reader(directory, files,
            (cue.FullPath, Cue(tracks.Select(track => track.Name).ToArray())));

        var units = Resolver(reader, NoArchives()).Resolve(Profile(directory), files);

        var unit = Assert.Single(units);
        Assert.Equal(13, unit.Files.Count);
        Assert.False(unit.HasAnomalies);
    }

    // ---- Gates 6 and 7: two discs, two units, one set, never variants of each other ----

    [Fact]
    public void Disc_one_and_disc_two_are_two_units_grouped_as_one_set()
    {
        var directory = @"D:\Sony - PlayStation";
        var units = TwoDiscSet(directory, out _);

        Assert.Equal(2, units.Count);

        var set = Assert.Single(DiscSetGrouper.Group(units, Vocabulary).Where(candidate => candidate.IsMultiDisc));
        Assert.Equal(2, set.Units.Count);
        Assert.Equal(4, set.AllFiles.Count);
    }

    // Gate 7. Disc number is an identity axis, so the two discs never share a grouping key and can
    // never be ranked against each other — the multi-disc destruction scenario, structurally.
    [Fact]
    public void Disc_units_never_group_as_variants_of_one_another()
    {
        var directory = @"D:\Sony - PlayStation";
        var units = TwoDiscSet(directory, out _);

        var keys = units
            .Select(unit => ARK.Core.Policy.VariantGrouping.KeyFor(
                unit.Name, Vocabulary, ARK.Core.Policy.VariantPresets.ReportOnly))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, keys.Length);
        Assert.True(ARK.Core.Policy.VariantGrouping.IsIdentity(
            TokenCategory.Disc, ARK.Core.Policy.VariantPresets.ReportOnly));
    }

    // A set is one game's discs. Two releases that merely share a title are not a set.
    [Fact]
    public void Discs_of_different_releases_do_not_form_a_set()
    {
        var directory = @"D:\Sony - PlayStation";
        var usa = File(directory, "Final Fantasy VII (USA) (Disc 1)", ".iso");
        var japan = File(directory, "Final Fantasy VII (Japan) (Disc 2)", ".iso");

        var reader = Reader(directory, new[] { usa, japan });
        var units = Resolver(reader, NoArchives()).Resolve(Profile(directory), new[] { usa, japan });

        Assert.Empty(DiscSetGrouper.Group(units, Vocabulary).Where(set => set.IsMultiDisc));
    }

    // Gate 6 against the real drive. 199 disc-numbered PlayStation archives on the reference
    // listing form 91 groups: 75 genuine multi-disc sets and 16 lone discs whose siblings are not
    // present. Grouping on token sets — not on a stripped filename — has to reproduce that split
    // exactly, because a set assembled wrongly is a set that gets moved wrongly.
    [Fact]
    public void Real_playstation_names_group_into_the_expected_multi_disc_sets()
    {
        var units = TestFixtures.ReadCorpus("reference-drive.txt")
            .Where(line => line.Contains(@"Sony - PlayStation", StringComparison.Ordinal)
                && line.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Select(line => line[(line.LastIndexOf('\\') + 1)..])
            .Select(name => name[..^4])
            .Where(stem => Tokenizer.Parse(stem).TokensOf(TokenCategory.Disc).Count > 0)
            .Select(Unit)
            .ToArray();

        Assert.Equal(199, units.Length);

        var sets = DiscSetGrouper.Group(units, Vocabulary);

        Assert.Equal(91, sets.Count);
        Assert.Equal(75, sets.Count(set => set.IsMultiDisc));
        Assert.Equal(199, sets.Sum(set => set.Units.Count));

        // The largest set on the drive. Five discs, one game, never merged into one unit.
        var riven = Assert.Single(sets, set => set.Title.StartsWith("Riven", StringComparison.Ordinal));
        Assert.Equal(5, riven.Units.Count);
    }

    // ---- Gate 8: a cue naming a missing file is incomplete, never partially resolved ----

    [Fact]
    public void A_cue_naming_a_missing_file_is_reported_incomplete()
    {
        var directory = @"D:\Sony - PlayStation";
        var cue = File(directory, "Tekken 3 (USA)", ".cue");
        var present = File(directory, "Tekken 3 (USA) (Track 1)", ".bin");

        var reader = Reader(directory, new[] { cue, present },
            (cue.FullPath, Cue("Tekken 3 (USA) (Track 1).bin", "Tekken 3 (USA) (Track 2).bin")));

        var unit = Assert.Single(Resolver(reader, NoArchives()).Resolve(Profile(directory), new[] { cue, present }));

        Assert.True(unit.HasAnomalies);
        var issue = Assert.Single(unit.Anomalies);
        Assert.Equal("incomplete-disc-unit", issue.Code);
        Assert.Contains("Track 2", issue.Detail, StringComparison.Ordinal);
    }

    // ---- Gates 12 and 13: the archive and bare-ISO shapes ----

    [Fact]
    public void An_archive_holding_bin_and_cue_resolves_as_a_disc_unit()
    {
        var directory = @"D:\Sony - PlayStation";
        var archive = File(directory, "Ridge Racer (USA)", ".zip");

        var inspector = new FakeArchiveInspector();
        inspector.Entries[archive.FullPath] = new[]
        {
            new ArchiveEntry("Ridge Racer (USA).cue", 100),
            new ArchiveEntry("Ridge Racer (USA).bin", 700_000_000),
        };
        inspector.SetEntry(archive.FullPath, "Ridge Racer (USA).cue", Cue("Ridge Racer (USA).bin"));

        var reader = Reader(directory, new[] { archive });
        var unit = Assert.Single(Resolver(reader, inspector).Resolve(Profile(directory), new[] { archive }));

        Assert.Equal(GameUnitKind.Disc, unit.Kind);
        Assert.False(unit.IsUnsupportedFormat);
        Assert.False(unit.HasAnomalies);

        // The archive is the unit: one file on disk, two entries inside it.
        Assert.Single(unit.Files);
        Assert.Equal(2, unit.Contents.Count);
    }

    [Fact]
    public void A_bare_iso_resolves_as_a_single_file_disc_unit()
    {
        var directory = @"D:\PSP\Roms";
        var iso = File(directory, "Daxter (USA)", ".iso");

        var reader = Reader(directory, new[] { iso });
        var unit = Assert.Single(Resolver(reader, NoArchives()).Resolve(Profile(directory), new[] { iso }));

        Assert.Equal(GameUnitKind.Disc, unit.Kind);
        Assert.Single(unit.Files);
        Assert.False(unit.HasAnomalies);
    }

    // A bare .bin with no cue is not claimed. Mega Drive ROMs are .bin files, and claiming the
    // extension would file the whole Genesis library as discs.
    [Fact]
    public void A_bare_bin_with_no_cue_is_left_to_the_cartridge_resolver()
    {
        var directory = @"D:\Sega - Mega Drive - Genesis";
        var rom = File(directory, "Sonic the Hedgehog (USA, Europe)", ".bin");

        var reader = Reader(directory, new[] { rom });
        Assert.Empty(Resolver(reader, NoArchives()).Resolve(Profile(directory), new[] { rom }));
    }

    // ---- Gate 15: .gdi and .ccd recognized and reported, never silently mis-resolved ----

    [Theory]
    [InlineData(".gdi")]
    [InlineData(".ccd")]
    public void Recognized_but_unparsed_descriptors_are_reported_as_unsupported(string extension)
    {
        var directory = @"D:\Sega - Dreamcast";
        var descriptor = File(directory, "Shenmue (USA) (Disc 1)", extension);

        var reader = Reader(directory, new[] { descriptor });
        var unit = Assert.Single(Resolver(reader, NoArchives()).Resolve(Profile(directory), new[] { descriptor }));

        Assert.True(unit.IsUnsupportedFormat);
        Assert.False(unit.HasAnomalies);
        Assert.Equal("unsupported-disc-descriptor", Assert.Single(unit.UnsupportedFormats).Code);
    }

    // ---- Gate 10: no cue is written in this phase at all ----

    // The strongest form available: resolving every shape opens files for reading and writes
    // nothing. A cue that matches its DAT hash is provably correct, and one that does not is
    // reported — neither is ever regenerated.
    [Fact]
    public void Resolving_discs_writes_nothing()
    {
        var directory = @"D:\Sony - PlayStation";
        var cue = File(directory, "Ridge Racer (USA)", ".cue");
        var track = File(directory, "Ridge Racer (USA)", ".bin");
        var iso = File(directory, "Daxter (USA)", ".iso");

        var reader = new WriteRefusingReader(Reader(directory, new[] { cue, track, iso },
            (cue.FullPath, Cue("Ridge Racer (USA).bin"))));

        Resolver(reader, NoArchives()).Resolve(Profile(directory), new[] { cue, track, iso });

        Assert.Empty(reader.Written);
        Assert.NotEmpty(reader.Inner.Opened);
    }

    // ---- helpers ----

    private static IReadOnlyList<GameUnit> TwoDiscSet(string directory, out FileEntry[] files)
    {
        var one = File(directory, "Final Fantasy VII (USA) (Disc 1)", ".cue");
        var oneBin = File(directory, "Final Fantasy VII (USA) (Disc 1)", ".bin");
        var two = File(directory, "Final Fantasy VII (USA) (Disc 2)", ".cue");
        var twoBin = File(directory, "Final Fantasy VII (USA) (Disc 2)", ".bin");

        files = new[] { one, oneBin, two, twoBin };
        var reader = Reader(directory, files,
            (one.FullPath, Cue("Final Fantasy VII (USA) (Disc 1).bin")),
            (two.FullPath, Cue("Final Fantasy VII (USA) (Disc 2).bin")));

        return Resolver(reader, NoArchives()).Resolve(Profile(directory), files);
    }

    /// <summary>A resolved disc unit for one archive name, with no filesystem behind it.</summary>
    private static GameUnit Unit(string stem)
    {
        var file = File(@"D:\Sony - PlayStation", stem, ".zip");
        return new GameUnit(
            GameUnitKind.Disc,
            file.FullPath,
            new[] { file },
            Tokenizer.Parse(stem),
            "Sony - PlayStation",
            Array.Empty<string>(),
            Array.Empty<ArchiveEntry>(),
            Array.Empty<GameUnitIssue>());
    }

    private static DiscUnitResolver Resolver(IFileSystemReader reader, IArchiveInspector inspector) =>
        new(Tokenizer, inspector, reader);

    private static FakeArchiveInspector NoArchives() => new() { Enabled = false };

    internal static byte[] Cue(params string[] fileNames) =>
        Encoding.UTF8.GetBytes(string.Join("\n", fileNames.SelectMany((name, index) => new[]
        {
            $"FILE \"{name}\" BINARY",
            $"  TRACK {index + 1:00} MODE2/2352",
            "    INDEX 01 00:00:00",
        })));

    private static FileEntry File(string directory, string name, string extension) =>
        new(Path.Combine(directory, name + extension), name + extension, extension, 1024, DateTimeOffset.UnixEpoch);

    private static InMemoryFileSystemReader Reader(
        string directory,
        IReadOnlyList<FileEntry> files,
        params (string Path, byte[] Bytes)[] contents)
    {
        var map = contents.ToDictionary(entry => entry.Path, entry => entry.Bytes, StringComparer.OrdinalIgnoreCase);
        var leaf = directory[(directory.LastIndexOf('\\') + 1)..];
        return new InMemoryFileSystemReader(new[] { new DirectoryListing(directory, leaf, files) }, map);
    }

    private static DirectoryProfile Profile(string directory) => new(
        directory,
        directory[(directory.LastIndexOf('\\') + 1)..],
        1, ".cue", 1, 1, DirectoryOutcome.RomSet, ExclusionReason.None,
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<FileEntry>());

    /// <summary>A reader that records any attempt to obtain a writable handle. There is none.</summary>
    private sealed class WriteRefusingReader : IFileSystemReader
    {
        public WriteRefusingReader(InMemoryFileSystemReader inner) => Inner = inner;

        public InMemoryFileSystemReader Inner { get; }

        public List<string> Written { get; } = new();

        public IEnumerable<DirectoryListing> EnumerateDirectories(string root) => Inner.EnumerateDirectories(root);

        public Stream OpenRead(string path)
        {
            var stream = Inner.OpenRead(path);
            Assert.False(stream.CanWrite);
            return stream;
        }

        public FileEntry? Describe(string path) => Inner.Describe(path);
    }
}
