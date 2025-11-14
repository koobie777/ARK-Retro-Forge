using ARK.Core.Psx;

namespace ARK.Tests.Psx;

public class PsxNamingServiceTests
{
    private readonly PsxNamingService _namingService = new();

    [Fact]
    public void GenerateSingleDiscName_WithAllMetadata_FormatsCorrectly()
    {
        // Arrange
        var group = new PsxTitleGroup
        {
            Title = "Final Fantasy VII",
            Region = "USA",
            Version = "1.0",
            Discs = new List<PsxDisc>
            {
                new PsxDisc { DiscNumber = 1, Serial = "SLUS-01201" }
            }
        };

        // Act
        var result = _namingService.GenerateSingleDiscName(group);

        // Assert
        Assert.Equal("Final Fantasy VII (USA) [v1.0] [SLUS-01201]", result);
    }

    [Fact]
    public void GenerateSingleDiscName_WithArticle_NormalizesArticle()
    {
        // Arrange
        var group = new PsxTitleGroup
        {
            Title = "Legend of Dragoon, The",
            Region = "USA",
            Discs = new List<PsxDisc>
            {
                new PsxDisc { DiscNumber = 1, Serial = "SCUS-94491" }
            }
        };

        // Act
        var result = _namingService.GenerateSingleDiscName(group);

        // Assert
        Assert.Equal("The Legend of Dragoon (USA) [SCUS-94491]", result);
    }

    [Fact]
    public void GenerateSingleDiscName_WithUnknownRegion_OmitsRegion()
    {
        // Arrange
        var group = new PsxTitleGroup
        {
            Title = "Some Game",
            Region = "Unknown",
            Discs = new List<PsxDisc>
            {
                new PsxDisc { DiscNumber = 1 }
            }
        };

        // Act
        var result = _namingService.GenerateSingleDiscName(group);

        // Assert
        Assert.Equal("Some Game", result);
    }

    [Fact]
    public void GenerateMultiDiscName_IncludesDiscNumber()
    {
        // Arrange
        var group = new PsxTitleGroup
        {
            Title = "Metal Gear Solid",
            Region = "USA",
            Discs = new List<PsxDisc>
            {
                new PsxDisc { DiscNumber = 1, Serial = "SLUS-00594" },
                new PsxDisc { DiscNumber = 2, Serial = "SLUS-00776" }
            }
        };

        var disc2 = group.Discs[1];

        // Act
        var result = _namingService.GenerateMultiDiscName(group, disc2);

        // Assert
        Assert.Equal("Metal Gear Solid (USA) [SLUS-00776] (Disc 2)", result);
    }

    [Fact]
    public void GeneratePlaylistName_OmitsSerials()
    {
        // Arrange
        var group = new PsxTitleGroup
        {
            Title = "Final Fantasy VII",
            Region = "USA",
            Version = "1.0",
            Discs = new List<PsxDisc>
            {
                new PsxDisc { DiscNumber = 1, Serial = "SLUS-01201" },
                new PsxDisc { DiscNumber = 2, Serial = "SLUS-01202" },
                new PsxDisc { DiscNumber = 3, Serial = "SLUS-01203" }
            }
        };

        // Act
        var result = _namingService.GeneratePlaylistName(group);

        // Assert
        Assert.Equal("Final Fantasy VII (USA) [v1.0]", result);
        Assert.DoesNotContain("SLUS", result);
    }
}
