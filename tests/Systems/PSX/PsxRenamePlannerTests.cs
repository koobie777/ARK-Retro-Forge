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
            Assert.Equal("Test Game (USA) [SLUS-12345] (Disc 1).bin", canonicalName);
            Assert.False(operation.IsAlreadyNamed, $"File with 'Disc 1 of 3' should not be considered already named. Normalized: '{normalizedCurrent}', Canonical: '{canonicalName}'");
            Assert.Contains("(Disc 1).bin", operation.DestinationPath);
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
}
