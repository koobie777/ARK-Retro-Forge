using ARK.Core.Psx;

namespace ARK.Tests.Psx;

public class PsxRenamePlannerTests
{
    private readonly PsxRenamePlanner _planner = new();

    [Fact]
    public void PlanRenames_SingleDisc_PlansRename()
    {
        // Arrange
        var groups = new List<PsxTitleGroup>
        {
            new PsxTitleGroup
            {
                Title = "Test Game",
                Region = "USA",
                GameDirectory = "/games/testgame",
                Discs = new List<PsxDisc>
                {
                    new PsxDisc
                    {
                        DiscNumber = 1,
                        Serial = "SLUS-12345",
                        CuePath = "/games/testgame/oldname.cue",
                        BinPaths = new List<string> { "/games/testgame/oldname.bin" }
                    }
                }
            }
        };

        // Act
        var operations = _planner.PlanRenames(groups, flattenMultidisc: false);

        // Assert
        Assert.NotEmpty(operations);
        Assert.Contains(operations, op => op.Type == FileOperationType.Rename && op.SourcePath.EndsWith("oldname.cue"));
        Assert.Contains(operations, op => op.Type == FileOperationType.Rename && op.SourcePath.EndsWith("oldname.bin"));
    }

    [Fact]
    public void PlanRenames_MultiDisc_WithFlatten_PlansMoveOperations()
    {
        // Arrange
        var groups = new List<PsxTitleGroup>
        {
            new PsxTitleGroup
            {
                Title = "Multi Disc Game",
                Region = "USA",
                GameDirectory = "/games/parent/multidisc",
                Discs = new List<PsxDisc>
                {
                    new PsxDisc
                    {
                        DiscNumber = 1,
                        ChdPath = "/games/parent/multidisc/disc1.chd"
                    },
                    new PsxDisc
                    {
                        DiscNumber = 2,
                        ChdPath = "/games/parent/multidisc/disc2.chd"
                    }
                }
            }
        };

        // Act
        var operations = _planner.PlanRenames(groups, flattenMultidisc: true);

        // Assert
        Assert.Contains(operations, op => op.Type == FileOperationType.Move);
        // Note: DeleteFolder operations are only added if the directory exists on disk
        // So in unit tests without actual filesystem, we verify the moves are planned
        Assert.Equal(2, operations.Count(op => op.Type == FileOperationType.Move));
    }

    [Fact]
    public void PlanRenames_MultiDisc_WithoutFlatten_PlansRenameOnly()
    {
        // Arrange
        var groups = new List<PsxTitleGroup>
        {
            new PsxTitleGroup
            {
                Title = "Multi Disc Game",
                Region = "USA",
                GameDirectory = "/games/multidisc",
                Discs = new List<PsxDisc>
                {
                    new PsxDisc
                    {
                        DiscNumber = 1,
                        ChdPath = "/games/multidisc/disc1.chd"
                    },
                    new PsxDisc
                    {
                        DiscNumber = 2,
                        ChdPath = "/games/multidisc/disc2.chd"
                    }
                }
            }
        };

        // Act
        var operations = _planner.PlanRenames(groups, flattenMultidisc: false);

        // Assert
        Assert.All(operations, op => Assert.NotEqual(FileOperationType.Move, op.Type));
        Assert.DoesNotContain(operations, op => op.Type == FileOperationType.DeleteFolder);
    }
}
