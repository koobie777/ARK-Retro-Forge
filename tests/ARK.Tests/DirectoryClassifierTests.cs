using ARK.Core.Configuration;
using ARK.Core.Naming;
using ARK.Core.Scanning;

namespace ARK.Tests;

/// <summary>
/// Phase 4 Part A: scan classifies directories, not files. Two signals, both required, measured
/// against the reference mixed-use drive.
/// </summary>
public class DirectoryClassifierTests
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);

    private static DirectoryProfiler Profiler() =>
        new(Tokenizer, ScanRulesLoader.Load(TestFixtures.ShippedScanRulesPath()), new[] { "BigEndian", "Headered", "Decrypted", "NKit RVZ" });

    private static IReadOnlyList<DirectoryProfile> ProfileDrive() =>
        ReferenceDrive.Listings().Select(Profiler().Profile).ToArray();

    // Gate 4. Folder names are no help — No-Intro, SNES Roms, Minerva_Myrient, PS2 Downloads and
    // Roms are all the same kind of content — so the split must come from the signals alone.
    [Fact]
    public void Classifier_reproduces_the_reference_split()
    {
        var profiles = ProfileDrive();
        var expected = ReferenceDrive.Rows();

        var romSets = profiles.Where(p => p.IsRomSet).ToArray();
        var excluded = profiles.Where(p => !p.IsRomSet).ToArray();

        var mismatches = profiles
            .Zip(expected, (actual, row) => (actual, row))
            .Where(pair => pair.actual.IsRomSet != (pair.row.Expected == "romset"))
            .Select(pair => $"{pair.row.Directory}: expected {pair.row.Expected}, got {pair.actual.Outcome} ({pair.actual.Explain()})")
            .ToArray();

        Assert.True(mismatches.Length == 0, string.Join(Environment.NewLine, mismatches));

        Assert.Equal(17, romSets.Length);
        Assert.Equal(140, excluded.Length);
        Assert.Equal(10_045, romSets.Sum(p => p.FileCount));
        Assert.Equal(11_050, excluded.Sum(p => p.FileCount));
    }

    // Gate 5. Homogeneity alone swallows 2,378 cheat files.
    [Theory]
    [InlineData(@"cheats\PS3")]
    [InlineData(@"cheats\Retroarch")]
    public void Perfectly_homogeneous_but_unconformant_directory_is_excluded(string relative)
    {
        var profile = ProfileDrive().Single(p => p.Path.EndsWith(relative, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(1.0, profile.ExtensionHomogeneity, 3);
        Assert.Equal(0.0, profile.NamingConformance, 3);
        Assert.False(profile.IsRomSet);
        Assert.Equal(ExclusionReason.BelowNamingConformance, profile.Reason);
    }

    // Gate 6. An extension allowlist would reject 90 bare .iso files with no archive wrapper.
    [Fact]
    public void Bare_iso_directory_with_no_archive_wrapper_is_admitted()
    {
        var profile = ProfileDrive().Single(p => p.Path.EndsWith(@"PSP\Roms", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(".iso", profile.DominantExtension);
        Assert.Equal(90, profile.FileCount);
        Assert.True(profile.IsRomSet, profile.Explain());
    }

    // Gate 7. 516 flawlessly No-Intro-named archives containing disc keys, not games.
    [Fact]
    public void Known_false_positive_is_admitted_but_flagged_never_silently()
    {
        var profile = ProfileDrive().Single(p => p.Name == "Nintendo - Wii U - Disc Keys");

        Assert.True(profile.IsRomSet, "it passes both signals — that is precisely the problem");
        Assert.Contains(profile.Warnings, warning => warning.Contains("known false positive", StringComparison.OrdinalIgnoreCase));
    }

    // Gate 10. Recorded, never acted on — header stripping and byte-order work belong to hashing.
    [Theory]
    [InlineData("Nintendo - Nintendo 64 (BigEndian)", "BigEndian")]
    [InlineData("Nintendo - Nintendo Entertainment System (Headered)", "Headered")]
    [InlineData("Nintendo - Nintendo DS (Decrypted)", "Decrypted")]
    [InlineData("Nintendo - GameCube - NKit RVZ", "NKit RVZ")]
    public void Format_qualifier_is_read_off_the_folder_name(string leaf, string expected)
    {
        var profile = ProfileDrive().Single(p => p.Name == leaf);

        Assert.Contains(expected, profile.FormatQualifiers);
    }

    // Both signals are always measured, so a rejected directory reports the number it failed on
    // and the number it would have passed.
    [Fact]
    public void Excluded_directory_states_a_reason_with_its_measurements()
    {
        foreach (var profile in ProfileDrive().Where(p => !p.IsRomSet))
        {
            Assert.NotEqual(ExclusionReason.None, profile.Reason);
            Assert.False(string.IsNullOrWhiteSpace(profile.Explain()));
        }
    }

    [Fact]
    public void Empty_and_tiny_directories_are_excluded_with_their_own_reasons()
    {
        var profiles = ProfileDrive();

        Assert.Equal(ExclusionReason.Empty, profiles.Single(p => p.Name == "empty").Reason);
        Assert.Equal(ExclusionReason.TooFewFiles, profiles.Single(p => p.Name == "chdman").Reason);
    }

    // A directory holding only subdirectories is how a tree is organized, not a finding. Reporting
    // the user's own scan root as "empty directory" is noise that buries the real exclusions.
    [Fact]
    public void Container_directory_is_distinguished_from_an_empty_one()
    {
        var container = Profiler().Profile(new DirectoryListing(@"D:\Games", "Games", Array.Empty<FileEntry>(), SubdirectoryCount: 4));
        var empty = Profiler().Profile(new DirectoryListing(@"D:\Games\spare", "spare", Array.Empty<FileEntry>()));

        Assert.Equal(ExclusionReason.Container, container.Reason);
        Assert.Equal("contains only subdirectories", container.Explain());
        Assert.Equal(ExclusionReason.Empty, empty.Reason);
    }

    // Both thresholds are boundaries, so they are asserted at the boundary rather than well clear of it.
    [Theory]
    [InlineData(90, 100, true, "exactly at the homogeneity threshold")]
    [InlineData(89, 100, false, "just under the homogeneity threshold")]
    [InlineData(100, 80, true, "exactly at the conformance threshold")]
    [InlineData(100, 79, false, "just under the conformance threshold")]
    public void Thresholds_are_inclusive_lower_bounds(int homogeneityPercent, int conformancePercent, bool admitted, string because)
    {
        var names = TestFixtures.ReadCorpus("real-names.txt");
        var files = new List<FileEntry>();
        for (var i = 0; i < 100; i++)
        {
            var extension = i < homogeneityPercent ? ".zip" : ".txt";
            var name = i < conformancePercent ? names[i] : $"unnamed_asset_{i:D3}";
            files.Add(new FileEntry($@"D:\x\{name}{extension}", name + extension, extension, 1, DateTimeOffset.UnixEpoch));
        }

        var profile = Profiler().Profile(new DirectoryListing(@"D:\x", "x", files));

        Assert.True(profile.IsRomSet == admitted, $"{because}: {profile.Explain()}");
    }
}
