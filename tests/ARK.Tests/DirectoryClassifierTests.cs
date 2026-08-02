using ARK.Core.Configuration;
using ARK.Core.Naming;
using ARK.Core.Scanning;

namespace ARK.Tests;

/// <summary>
/// Phase 4 Part A, measured against the real 22,050-file mixed-use drive: scan classifies
/// directories, not files, on two signals that are both required.
/// </summary>
public class DirectoryClassifierTests
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);
    private static readonly Lazy<IReadOnlyList<DirectoryProfile>> Drive = new(() => ReferenceDrive.Listings().Select(Profiler().Profile).ToArray());

    private static DirectoryProfiler Profiler() =>
        new(Tokenizer, ScanRulesLoader.Load(TestFixtures.ShippedScanRulesPath()), new[] { "BigEndian", "Headered", "Headerless", "Decrypted", "NKit RVZ" });

    private static IReadOnlyList<DirectoryProfile> ProfileDrive() => Drive.Value;

    private static DirectoryProfile Directory(string relativePath) =>
        ProfileDrive().Single(profile => profile.Path.EndsWith(relativePath, StringComparison.OrdinalIgnoreCase));

    // Gate 4. Folder names are no help — this drive spells ROM sets "No-Intro", "SNES Roms",
    // "Minerva_Myrient", "Nintendo - Game Boy Advance", "PS2 Downloads" and "Roms" — so the split
    // has to come from the measured signals alone.
    [Fact]
    public void Classifier_reproduces_the_reference_split()
    {
        var expected = ReferenceDrive.Expectations();
        var profiles = ProfileDrive();

        var romSets = profiles.Where(p => p.IsRomSet).ToArray();
        var tooFew = profiles.Where(p => p.Reason == ExclusionReason.TooFewFiles).ToArray();
        var other = profiles.Where(p => !p.IsRomSet && p.Reason != ExclusionReason.TooFewFiles).ToArray();

        Assert.Equal(expected.TotalFiles, profiles.Sum(p => p.FileCount));

        Assert.Equal(expected.RomSetDirectories, romSets.Length);
        Assert.Equal(expected.RomSetFiles, romSets.Sum(p => p.FileCount));

        Assert.Equal(expected.OtherDirectories, other.Length);
        Assert.Equal(expected.OtherFiles, other.Sum(p => p.FileCount));

        Assert.Equal(expected.TooFewDirectories, tooFew.Length);
        Assert.Equal(expected.TooFewFiles, tooFew.Sum(p => p.FileCount));
    }

    // Gate 5. Both cheat databases are perfectly homogeneous and carry no region tokens at all.
    // Homogeneity alone admits 2,378 files that are not games.
    [Theory]
    [InlineData(@"N64\Emulator\RMG-Portable-Windows64\Data\Cheats", 706, ".cht")]
    [InlineData(@"PS3\Tools\ps3tools\tools\BruteforceSaveData\Cheats", 1672, ".ps3savepatch")]
    public void Perfectly_homogeneous_but_unconformant_directory_is_excluded(string relative, int files, string extension)
    {
        var profile = Directory(relative);

        Assert.Equal(files, profile.FileCount);
        Assert.Equal(extension, profile.DominantExtension);
        Assert.Equal(1.0, profile.ExtensionHomogeneity, 3);
        Assert.Equal(0.0, profile.NamingConformance, 3);
        Assert.False(profile.IsRomSet);
        Assert.Equal(ExclusionReason.BelowNamingConformance, profile.Reason);
    }

    // Gate 6. 90 bare .iso files with no archive wrapper — an extension allowlist rejects this,
    // naming conformance admits it.
    [Fact]
    public void Bare_iso_directory_with_no_archive_wrapper_is_admitted()
    {
        var profile = Directory(@"PSP\Roms");

        Assert.Equal(90, profile.FileCount);
        Assert.Equal(".iso", profile.DominantExtension);
        Assert.True(profile.IsRomSet, profile.Explain());
    }

    // Gate 7. 516 flawlessly No-Intro-named archives containing disc keys, not games. Conformant
    // naming does not prove game content.
    [Fact]
    public void Known_false_positive_is_admitted_but_flagged_never_silently()
    {
        var profile = Directory(@"Wii U\Wii U Disc Keys\Nintendo - Wii U - Disc Keys");

        Assert.Equal(516, profile.FileCount);
        Assert.True(profile.IsRomSet, "it passes both signals — that is precisely the problem");
        Assert.Contains(profile.Warnings, warning => warning.Contains("known false positive", StringComparison.OrdinalIgnoreCase));
    }

    // Gate 10. Recorded, never acted on — header stripping and byte-order work belong to hashing.
    [Theory]
    [InlineData(@"N64\No-Intro\Nintendo - Nintendo 64 (BigEndian)", "BigEndian")]
    [InlineData(@"NES\NES Roms\Nintendo - Nintendo Entertainment System (Headered)", "Headered")]
    [InlineData(@"NDS\NDS Roms\Nintendo - Nintendo DS (Decrypted)", "Decrypted")]
    [InlineData(@"Gamecube\Gamecube Roms\Nintendo - GameCube - NKit RVZ [zstd-19-128k]", "NKit RVZ")]
    public void Format_qualifier_is_read_off_the_folder_name(string relative, string expected)
    {
        Assert.Contains(expected, Directory(relative).FormatQualifiers);
    }

    // The ROM sets the drive actually holds, spelled every way a real drive spells them.
    [Theory]
    [InlineData(@"NDS\NDS Roms\Nintendo - Nintendo DS (Decrypted)")]
    [InlineData(@"PSX\Minerva_Myrient\Redump\Sony - PlayStation")]
    [InlineData(@"GBA\Nintendo - Game Boy Advance")]
    [InlineData(@"SNES\SNES Roms\Nintendo - Super Nintendo Entertainment System")]
    [InlineData(@"PS2\PS2 Downloads\Minerva_Myrient\Redump\Sony - PlayStation 2")]
    public void Real_rom_sets_are_admitted_whatever_the_folder_is_called(string relative)
    {
        Assert.True(Directory(relative).IsRomSet, Directory(relative).Explain());
    }

    // Tooling and firmware that sits right beside the ROM sets.
    [Theory]
    [InlineData(@"PS3\Tools\ps3tools\tools\scetool\.ps3")]
    [InlineData(@"Switch\Emulator\Firmware.22.5.0")]
    [InlineData(@"PS3\Emulator\rpcs3\dev_flash\vsh\module")]
    public void Tooling_and_firmware_are_excluded(string relative)
    {
        Assert.False(Directory(relative).IsRomSet);
    }

    // Both signals are always measured, so a rejected directory reports the number it failed on
    // and the number it would have passed.
    [Fact]
    public void Every_excluded_directory_states_a_reason()
    {
        foreach (var profile in ProfileDrive().Where(p => !p.IsRomSet))
        {
            Assert.NotEqual(ExclusionReason.None, profile.Reason);
            Assert.False(string.IsNullOrWhiteSpace(profile.Explain()));
        }
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
