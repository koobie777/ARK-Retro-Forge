using ARK.Core.Psx;

namespace ARK.Tests.Psx;

public class PsxConvertPlannerTests
{
    private readonly PsxConvertPlanner _planner = new();

    [Fact]
    public void PlanConversions_BinCueToChd_WithNoChdPresent_PlansConversion()
    {
        // Arrange
        var groups = new List<PsxTitleGroup>
        {
            new PsxTitleGroup
            {
                Title = "Test Game",
                Region = "USA",
                Discs = new List<PsxDisc>
                {
                    new PsxDisc
                    {
                        DiscNumber = 1,
                        CuePath = "/games/test.cue",
                        BinPaths = new List<string> { "/games/test.bin" }
                    }
                }
            }
        };

        // Act
        var operations = _planner.PlanConversions(groups, ConversionDirection.BinCueToChd, deleteSource: false);

        // Assert
        Assert.Single(operations);
        Assert.Equal(ConversionStatus.Planned, operations[0].Status);
        Assert.Equal("/games/test.cue", operations[0].SourcePath);
    }

    [Fact]
    public void PlanConversions_BinCueToChd_WithChdPresent_SkipsConversion()
    {
        // Arrange
        var groups = new List<PsxTitleGroup>
        {
            new PsxTitleGroup
            {
                Title = "Test Game",
                Region = "USA",
                Discs = new List<PsxDisc>
                {
                    new PsxDisc
                    {
                        DiscNumber = 1,
                        CuePath = "/games/test.cue",
                        BinPaths = new List<string> { "/games/test.bin" },
                        ChdPath = "/games/test.chd"
                    }
                }
            }
        };

        // Act
        var operations = _planner.PlanConversions(groups, ConversionDirection.BinCueToChd, deleteSource: false);

        // Assert
        Assert.Single(operations);
        Assert.Equal(ConversionStatus.Skipped, operations[0].Status);
        Assert.Equal("Already in CHD format", operations[0].SkipReason);
    }

    [Fact]
    public void PlanConversions_WithDeleteSource_MarksFilesForDeletion()
    {
        // Arrange
        var groups = new List<PsxTitleGroup>
        {
            new PsxTitleGroup
            {
                Title = "Test Game",
                Region = "USA",
                Discs = new List<PsxDisc>
                {
                    new PsxDisc
                    {
                        DiscNumber = 1,
                        CuePath = "/games/test.cue",
                        BinPaths = new List<string> { "/games/test.bin" }
                    }
                }
            }
        };

        // Act
        var operations = _planner.PlanConversions(groups, ConversionDirection.BinCueToChd, deleteSource: true);

        // Assert
        var plannedOp = operations.First(o => o.Status == ConversionStatus.Planned);
        Assert.Equal(2, plannedOp.FilesToDelete.Count);
        Assert.Contains("/games/test.cue", plannedOp.FilesToDelete);
        Assert.Contains("/games/test.bin", plannedOp.FilesToDelete);
    }

    [Fact]
    public void PlanConversions_ChdToBinCue_OnlyConsidersChdFiles()
    {
        // Arrange
        var groups = new List<PsxTitleGroup>
        {
            new PsxTitleGroup
            {
                Title = "Test Game",
                Region = "USA",
                Discs = new List<PsxDisc>
                {
                    new PsxDisc
                    {
                        DiscNumber = 1,
                        ChdPath = "/games/test.chd"
                    },
                    new PsxDisc
                    {
                        DiscNumber = 2,
                        CuePath = "/games/test2.cue",
                        BinPaths = new List<string> { "/games/test2.bin" }
                    }
                }
            }
        };

        // Act
        var operations = _planner.PlanConversions(groups, ConversionDirection.ChdToBinCue, deleteSource: false);

        // Assert
        Assert.Single(operations);
        Assert.Equal("/games/test.chd", operations[0].SourcePath);
    }
}
