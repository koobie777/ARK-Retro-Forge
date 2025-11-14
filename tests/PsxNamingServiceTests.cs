using ARK.Core.Psx;

namespace ARK.Tests;

public class PsxNamingServiceTests
{
    [Fact]
    public void NormalizeTitle_WithTrailingTheArticle_MovesToFront()
    {
        var input = "Legend of Dragoon, The";
        var result = PsxNamingService.NormalizeTitle(input);
        Assert.Equal("The Legend of Dragoon", result);
    }
    
    [Fact]
    public void NormalizeTitle_WithTrailingAArticle_MovesToFront()
    {
        var input = "Link to the Past, A";
        var result = PsxNamingService.NormalizeTitle(input);
        Assert.Equal("A Link to the Past", result);
    }
    
    [Fact]
    public void NormalizeTitle_WithTrailingAnArticle_MovesToFront()
    {
        var input = "American Tail, An";
        var result = PsxNamingService.NormalizeTitle(input);
        Assert.Equal("An American Tail", result);
    }
    
    [Fact]
    public void NormalizeTitle_WithoutTrailingArticle_RemainsUnchanged()
    {
        var input = "Final Fantasy VII";
        var result = PsxNamingService.NormalizeTitle(input);
        Assert.Equal("Final Fantasy VII", result);
    }
    
    [Fact]
    public void NormalizeTitle_WithLeadingArticle_RemainsUnchanged()
    {
        var input = "The Legend of Zelda";
        var result = PsxNamingService.NormalizeTitle(input);
        Assert.Equal("The Legend of Zelda", result);
    }
    
    [Fact]
    public void GenerateSingleDiscName_WithAllMetadata_IncludesAllParts()
    {
        var titleGroup = new PsxTitleGroup
        {
            Title = "Final Fantasy VII",
            Region = "USA",
            Version = "v1.1",
            Discs = new[]
            {
                new PsxDisc
                {
                    DiscNumber = 1,
                    Serial = "SCUS-94163",
                    SourcePath = "/test/ff7.cue",
                    Format = PsxDiscFormat.BinCue
                }
            }
        };
        
        var result = PsxNamingService.GenerateSingleDiscName(titleGroup, ".chd");
        Assert.Equal("Final Fantasy VII (USA) [v1.1] [SCUS-94163].chd", result);
    }
    
    [Fact]
    public void GenerateSingleDiscName_WithoutVersion_OmitsVersionBlock()
    {
        var titleGroup = new PsxTitleGroup
        {
            Title = "Crash Bandicoot",
            Region = "USA",
            Version = null,
            Discs = new[]
            {
                new PsxDisc
                {
                    DiscNumber = 1,
                    Serial = "SCUS-94900",
                    SourcePath = "/test/crash.cue",
                    Format = PsxDiscFormat.BinCue
                }
            }
        };
        
        var result = PsxNamingService.GenerateSingleDiscName(titleGroup, ".chd");
        Assert.Equal("Crash Bandicoot (USA) [SCUS-94900].chd", result);
    }
    
    [Fact]
    public void GenerateSingleDiscName_WithoutSerial_OmitsSerialBlock()
    {
        var titleGroup = new PsxTitleGroup
        {
            Title = "Spyro the Dragon",
            Region = "Europe",
            Version = null,
            Discs = new[]
            {
                new PsxDisc
                {
                    DiscNumber = 1,
                    Serial = null,
                    SourcePath = "/test/spyro.cue",
                    Format = PsxDiscFormat.BinCue
                }
            }
        };
        
        var result = PsxNamingService.GenerateSingleDiscName(titleGroup, ".chd");
        Assert.Equal("Spyro the Dragon (Europe).chd", result);
    }
    
    [Fact]
    public void GenerateMultiDiscName_IncludesDiscSerial()
    {
        var disc = new PsxDisc
        {
            DiscNumber = 1,
            Serial = "SCUS-94163",
            SourcePath = "/test/ff7_d1.cue",
            Format = PsxDiscFormat.BinCue
        };
        
        var titleGroup = new PsxTitleGroup
        {
            Title = "Final Fantasy VII",
            Region = "USA",
            Version = "v1.1",
            Discs = new[] { disc }
        };
        
        var result = PsxNamingService.GenerateMultiDiscName(titleGroup, disc, ".chd");
        Assert.Equal("Final Fantasy VII (USA) [v1.1] [SCUS-94163].chd", result);
    }
    
    [Fact]
    public void GeneratePlaylistName_OmitsSerials()
    {
        var titleGroup = new PsxTitleGroup
        {
            Title = "Final Fantasy VII",
            Region = "USA",
            Version = "v1.1",
            Discs = new[]
            {
                new PsxDisc
                {
                    DiscNumber = 1,
                    Serial = "SCUS-94163",
                    SourcePath = "/test/ff7_d1.cue",
                    Format = PsxDiscFormat.BinCue
                }
            }
        };
        
        var result = PsxNamingService.GeneratePlaylistName(titleGroup);
        Assert.Equal("Final Fantasy VII (USA) [v1.1].m3u", result);
    }
    
    [Fact]
    public void GeneratePlaylistName_WithMinimalMetadata_WorksCorrectly()
    {
        var titleGroup = new PsxTitleGroup
        {
            Title = "Metal Gear Solid",
            Region = null,
            Version = null,
            Discs = new[]
            {
                new PsxDisc
                {
                    DiscNumber = 1,
                    Serial = null,
                    SourcePath = "/test/mgs_d1.cue",
                    Format = PsxDiscFormat.BinCue
                }
            }
        };
        
        var result = PsxNamingService.GeneratePlaylistName(titleGroup);
        Assert.Equal("Metal Gear Solid.m3u", result);
    }
}
