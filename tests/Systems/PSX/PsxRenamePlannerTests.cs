using ARK.Core.Systems.PSX;

namespace ARK.Tests.Systems.PSX;

public class PsxRenamePlannerTests
{
    [Fact]
    public async Task PlanRenames_DiscOfFormat_ShouldNormalize()
    {
        // Arrange
        var planner = new PsxRenamePlanner();
        var testDir = "/tmp/test-" + Guid.NewGuid();
        var testFile = Path.Combine(testDir, "Test Game (USA) [SLUS-12345] (Disc 1 of 3).bin");
        
        // Create temp directory and file
        Directory.CreateDirectory(testDir);
        File.WriteAllText(testFile, "");
        
        try
        {
            // Act
            var operations = await planner.PlanRenamesAsync(testDir, recursive: false);
            var operation = operations.Single();
            
            // Debug output
            var currentFileName = Path.GetFileName(testFile);
            var normalizedCurrent = PsxNameFormatter.NormalizeDiscSuffix(currentFileName);
            var canonicalName = Path.GetFileName(operation.DestinationPath);
            
            // Assert
            Assert.Equal("Test Game (USA) [SLUS-12345] (Disc 1).bin", normalizedCurrent);
            Assert.Equal("Test Game (USA) (Disc 1) [SLUS-12345].bin", canonicalName);
            Assert.False(operation.IsAlreadyNamed, $"File with 'Disc 1 of 3' should not be considered already named. Normalized: '{normalizedCurrent}', Canonical: '{canonicalName}'");
            Assert.Contains("(Disc 1)", operation.DestinationPath);
            Assert.EndsWith(".bin", operation.DestinationPath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("of 3", operation.DestinationPath);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PlanRenames_StripsLanguageTagsByDefault()
    {
        var planner = new PsxRenamePlanner();
        var testDir = Path.Combine(Path.GetTempPath(), "ark-lang-default-" + Guid.NewGuid());
        var file = Path.Combine(testDir, "Test Game (USA) (En,Fr).bin");

        Directory.CreateDirectory(testDir);
        File.WriteAllText(file, string.Empty);

        try
        {
            var operations = await planner.PlanRenamesAsync(testDir, recursive: false);
            var op = operations.Single();

            Assert.DoesNotContain("(En,Fr)", op.DestinationPath);
            Assert.Contains("(USA)", op.DestinationPath);
            Assert.False(op.IsAlreadyNamed);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PlanRenames_CanKeepLanguageTagsWhenRequested()
    {
        var planner = new PsxRenamePlanner();
        var testDir = Path.Combine(Path.GetTempPath(), "ark-lang-keep-" + Guid.NewGuid());
        var file = Path.Combine(testDir, "Test Game (USA) (En,Fr).bin");

        Directory.CreateDirectory(testDir);
        File.WriteAllText(file, string.Empty);

        try
        {
            var operations = await planner.PlanRenamesAsync(testDir, stripLanguageTags: false);
            var op = operations.Single();

            Assert.Contains("(En,Fr)", op.DestinationPath);
            Assert.EndsWith(".bin", op.DestinationPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    // --- P4 multi-disc vs multi-track separation ---

    [Fact]
    public async Task PlanRenames_SameExtensionMultiDisc_GroupsCorrectly()
    {
        var planner = new PsxRenamePlanner();
        var testDir = Path.Combine(Path.GetTempPath(), "ark-disc-same-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "Game (Disc 1).chd"), string.Empty);
        File.WriteAllText(Path.Combine(testDir, "Game (Disc 2).chd"), string.Empty);

        try
        {
            var operations = await planner.PlanRenamesAsync(testDir);
            Assert.Equal(2, operations.Count);
            Assert.All(operations, op => Assert.True(op.DiscInfo.IsMultiDisc, "Both discs should be flagged as multi-disc"));
            Assert.All(operations, op => Assert.Equal(2, op.DiscInfo.DiscCount));
            var discNumbers = operations.Select(op => op.DiscInfo.DiscNumber).OrderBy(n => n).ToList();
            Assert.Equal([1, 2], discNumbers);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task PlanRenames_MixedExtensionMultiDisc_GroupsCorrectly()
    {
        // Regression: before fix, extension was in the group key, so .bin and .chd
        // discs formed separate single-item groups and both received DiscCount = 1.
        var planner = new PsxRenamePlanner();
        var testDir = Path.Combine(Path.GetTempPath(), "ark-disc-mixed-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "Game (Disc 1).bin"), string.Empty);
        File.WriteAllText(Path.Combine(testDir, "Game (Disc 2).chd"), string.Empty);

        try
        {
            var operations = await planner.PlanRenamesAsync(testDir);
            Assert.Equal(2, operations.Count);
            Assert.All(operations, op => Assert.True(op.DiscInfo.IsMultiDisc, "Both discs should be flagged as multi-disc"));
            Assert.All(operations, op => Assert.Equal(2, op.DiscInfo.DiscCount));
            var discNumbers = operations.Select(op => op.DiscInfo.DiscNumber).OrderBy(n => n).ToList();
            Assert.Equal([1, 2], discNumbers);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task PlanRenames_MultiTrackBins_NeverAssignedDiscNumbers()
    {
        // Track 02 is an audio track and must be skipped entirely.
        // Track 01 (data) survives but must not receive a disc assignment.
        var planner = new PsxRenamePlanner();
        var testDir = Path.Combine(Path.GetTempPath(), "ark-track-only-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "Game (Track 01).bin"), string.Empty);
        File.WriteAllText(Path.Combine(testDir, "Game (Track 02).bin"), string.Empty);

        try
        {
            var operations = await planner.PlanRenamesAsync(testDir);
            // Only Track 01 (data track) generates an operation; Track 02 is skipped
            Assert.Single(operations);
            var op = operations[0];
            Assert.False(op.DiscInfo.IsMultiDisc, "Track files must not be treated as multi-disc");
            Assert.Null(op.DiscInfo.DiscNumber);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task PlanRenames_MultiDiscAndMultiTrack_AudioTracksSkippedDataTracksNotGroupedAsMultiDisc()
    {
        // Disc 1 has two BIN tracks (data + audio). Audio track must be skipped.
        // The lone data track must not be promoted to a multi-disc set.
        var planner = new PsxRenamePlanner();
        var testDir = Path.Combine(Path.GetTempPath(), "ark-disc-track-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "Game (Disc 1) (Track 01).bin"), string.Empty);
        File.WriteAllText(Path.Combine(testDir, "Game (Disc 1) (Track 02).bin"), string.Empty);

        try
        {
            var operations = await planner.PlanRenamesAsync(testDir);
            // Only the data track (Track 01) generates an operation
            Assert.Single(operations);
            var op = operations[0];
            Assert.False(op.DiscInfo.IsMultiDisc, "A single data track from one disc must not be treated as multi-disc");
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, recursive: true);
        }
    }
}
