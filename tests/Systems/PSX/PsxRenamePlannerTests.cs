using ARK.Core.Systems.PSX;

namespace ARK.Tests.Systems.PSX;

public class PsxRenamePlannerTests
{
    [Fact]
    public void PlanRenames_DiscOfFormat_ShouldNormalize()
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
            var operations = planner.PlanRenames(testDir, recursive: false);
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
    public void PlanRenames_StripsLanguageTagsByDefault()
    {
        var planner = new PsxRenamePlanner();
        var testDir = Path.Combine(Path.GetTempPath(), "ark-lang-default-" + Guid.NewGuid());
        var file = Path.Combine(testDir, "Test Game (USA) (En,Fr).bin");

        Directory.CreateDirectory(testDir);
        File.WriteAllText(file, string.Empty);

        try
        {
            var operations = planner.PlanRenames(testDir, recursive: false);
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
    public void PlanRenames_CanKeepLanguageTagsWhenRequested()
    {
        var planner = new PsxRenamePlanner();
        var testDir = Path.Combine(Path.GetTempPath(), "ark-lang-keep-" + Guid.NewGuid());
        var file = Path.Combine(testDir, "Test Game (USA) (En,Fr).bin");

        Directory.CreateDirectory(testDir);
        File.WriteAllText(file, string.Empty);

        try
        {
            var operations = planner.PlanRenames(testDir, stripLanguageTags: false);
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
}
