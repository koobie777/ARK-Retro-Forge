using ARK.Core.Systems.PSX;

namespace ARK.Tests.Systems.PSX;

public class PsxRenamePlannerTests
{
    [Fact]
    public void PlanRename_DiscOfFormat_ShouldNormalize()
    {
        // Arrange
        var planner = new PsxRenamePlanner();
        var testFile = "/tmp/test/Test Game (USA) [SLUS-12345] (Disc 1 of 3).bin";
        
        // Create temp directory and file
        Directory.CreateDirectory(Path.GetDirectoryName(testFile)!);
        File.WriteAllText(testFile, "");
        
        try
        {
            // Act
            var operation = planner.PlanRename(testFile);
            
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
            if (File.Exists(testFile))
            {
                File.Delete(testFile);
            }
        }
    }
}
