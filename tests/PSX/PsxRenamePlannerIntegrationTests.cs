using ARK.Core.PSX;

namespace ARK.Tests.PSX;

public class PsxRenamePlannerIntegrationTests : IDisposable
{
    private readonly string _testRoot;

    public PsxRenamePlannerIntegrationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ark-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public async Task PlanRenames_SingleDiscWithSerial_NoRenameNeeded()
    {
        // Arrange
        var testDir = Path.Combine(_testRoot, "single-disc");
        Directory.CreateDirectory(testDir);
        
        var cueFile = Path.Combine(testDir, "Final Fantasy VII (USA) [SLUS-00001].cue");
        var binFile = Path.Combine(testDir, "Final Fantasy VII (USA) [SLUS-00001].bin");
        File.WriteAllText(cueFile, "FILE \"Final Fantasy VII (USA) [SLUS-00001].bin\" BINARY");
        File.WriteAllText(binFile, "");

        var planner = new PsxRenamePlanner(CheatHandlingMode.Standalone);

        // Act
        var operations = await planner.PlanRenamesAsync(testDir, false);

        // Assert
        Assert.Equal(2, operations.Count);
        Assert.All(operations, op => Assert.True(op.IsAlreadyNamed));
        Assert.All(operations, op => Assert.Equal("SLUS-00001", op.Serial));
    }

    [Fact]
    public async Task PlanRenames_MultiDiscWithSuffix_NormalizesDiscSuffix()
    {
        // Arrange
        var testDir = Path.Combine(_testRoot, "multi-disc");
        Directory.CreateDirectory(testDir);
        
        var cue1 = Path.Combine(testDir, "Game (USA) (Disc 1 of 2).cue");
        var bin1 = Path.Combine(testDir, "Game (USA) (Disc 1 of 2).bin");
        var cue2 = Path.Combine(testDir, "Game (USA) (Disc 2 of 2).cue");
        var bin2 = Path.Combine(testDir, "Game (USA) (Disc 2 of 2).bin");
        
        File.WriteAllText(cue1, "FILE \"Game (USA) (Disc 1 of 2).bin\" BINARY");
        File.WriteAllText(bin1, "");
        File.WriteAllText(cue2, "FILE \"Game (USA) (Disc 2 of 2).bin\" BINARY");
        File.WriteAllText(bin2, "");

        var planner = new PsxRenamePlanner(CheatHandlingMode.Standalone);

        // Act
        var operations = await planner.PlanRenamesAsync(testDir, false);

        // Assert
        Assert.Equal(4, operations.Count);
        
        var disc1Files = operations.Where(op => op.DestinationFileName.Contains("(Disc 1)")).ToList();
        var disc2Files = operations.Where(op => op.DestinationFileName.Contains("(Disc 2)")).ToList();
        
        Assert.Equal(2, disc1Files.Count);
        Assert.Equal(2, disc2Files.Count);
        
        Assert.All(operations, op => Assert.DoesNotContain("of 2", op.DestinationFileName));
    }

    [Fact]
    public async Task PlanRenames_CheatDiscStandaloneMode_TreatsAsStandalone()
    {
        // Arrange
        var testDir = Path.Combine(_testRoot, "cheat");
        Directory.CreateDirectory(testDir);
        
        var cueFile = Path.Combine(testDir, "GameShark Ultimate Codes.cue");
        var binFile = Path.Combine(testDir, "GameShark Ultimate Codes.bin");
        File.WriteAllText(cueFile, "FILE \"GameShark Ultimate Codes.bin\" BINARY");
        File.WriteAllText(binFile, "");

        var planner = new PsxRenamePlanner(CheatHandlingMode.Standalone);

        // Act
        var operations = await planner.PlanRenamesAsync(testDir, false);

        // Assert
        Assert.Equal(2, operations.Count);
        Assert.All(operations, op => Assert.True(op.IsCheatDisc));
        Assert.All(operations, op => Assert.DoesNotContain("(Disc", op.DestinationFileName));
    }

    [Fact]
    public async Task PlanRenames_CheatDiscOmitMode_ExcludesCheatDiscs()
    {
        // Arrange
        var testDir = Path.Combine(_testRoot, "cheat-omit");
        Directory.CreateDirectory(testDir);
        
        var cueFile = Path.Combine(testDir, "GameShark Ultimate Codes.cue");
        var binFile = Path.Combine(testDir, "GameShark Ultimate Codes.bin");
        File.WriteAllText(cueFile, "FILE \"GameShark Ultimate Codes.bin\" BINARY");
        File.WriteAllText(binFile, "");

        var planner = new PsxRenamePlanner(CheatHandlingMode.Omit);

        // Act
        var operations = await planner.PlanRenamesAsync(testDir, false);

        // Assert
        Assert.Empty(operations);
    }

    [Fact]
    public async Task PlanRenames_EducationalDisc_ClassifiedCorrectly()
    {
        // Arrange
        var testDir = Path.Combine(_testRoot, "educational");
        Directory.CreateDirectory(testDir);
        
        var cueFile = Path.Combine(testDir, "Lightspan Adventures - Math.cue");
        var binFile = Path.Combine(testDir, "Lightspan Adventures - Math.bin");
        File.WriteAllText(cueFile, "FILE \"Lightspan Adventures - Math.bin\" BINARY");
        File.WriteAllText(binFile, "");

        var planner = new PsxRenamePlanner(CheatHandlingMode.Standalone);

        // Act
        var operations = await planner.PlanRenamesAsync(testDir, false);

        // Assert
        Assert.Equal(2, operations.Count);
        Assert.All(operations, op => Assert.True(op.IsEducationalDisc));
        Assert.All(operations, op => Assert.Contains("Serial number not found", op.Warning ?? ""));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }
    }
}
