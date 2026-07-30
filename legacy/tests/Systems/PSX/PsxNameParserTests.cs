using ARK.Core.Systems.PSX;

namespace ARK.Tests.Systems.PSX;

public class PsxNameParserTests
{
    private readonly PsxNameParser _parser;
    
    public PsxNameParserTests()
    {
        _parser = new PsxNameParser();
    }
    
    [Fact]
    public void Parse_StandardFormat_ExtractsAllMetadata()
    {
        // Arrange
        var filename = "Final Fantasy VII (USA) [SCUS-94163].bin";
        
        // Act
        var result = _parser.Parse(filename);
        
        // Assert
        Assert.Equal("Final Fantasy VII", result.Title);
        Assert.Equal("USA", result.Region);
        Assert.Equal("SCUS-94163", result.Serial);
        Assert.Equal(".bin", result.Extension);
        Assert.Null(result.DiscNumber);
        Assert.Null(result.DiscCount);
        Assert.False(result.IsMultiDisc);
    }
    
    [Fact]
    public void Parse_MultiDiscWithOf_ExtractsDiscInfo()
    {
        // Arrange
        var filename = "Alone in the Dark - The New Nightmare (USA) [SLUS-01201] (Disc 1 of 2).bin";
        
        // Act
        var result = _parser.Parse(filename);
        
        // Assert
        Assert.Equal("Alone in the Dark - The New Nightmare", result.Title);
        Assert.Equal("USA", result.Region);
        Assert.Equal("SLUS-01201", result.Serial);
        Assert.Equal(1, result.DiscNumber);
        Assert.Equal(2, result.DiscCount);
        Assert.True(result.IsMultiDisc);
    }
    
    [Fact]
    public void Parse_MultiDiscWithoutOf_ExtractsDiscNumber()
    {
        // Arrange
        var filename = "Alone in the Dark - The New Nightmare (USA) [SLUS-01377] (Disc 2).cue";
        
        // Act
        var result = _parser.Parse(filename);
        
        // Assert
        Assert.Equal("Alone in the Dark - The New Nightmare", result.Title);
        Assert.Equal("USA", result.Region);
        Assert.Equal("SLUS-01377", result.Serial);
        Assert.Equal(2, result.DiscNumber);
        Assert.Equal(".cue", result.Extension);
    }
    
    [Fact]
    public void Parse_NoSerial_SetsWarning()
    {
        // Arrange
        var filename = "The Adventures of Lomax (USA) (Track 11).bin";
        
        // Act
        var result = _parser.Parse(filename);
        
        // Assert
        Assert.NotNull(result.Warning);
        Assert.Contains("Serial number not found", result.Warning);
    }
    
    [Fact]
    public void Parse_LightspanSerial_ClassifiesAsEducational()
    {
        // Arrange
        var filename = "16 Tales 1 [LSP-990121].bin";
        
        // Act
        var result = _parser.Parse(filename);
        
        // Assert
        Assert.Equal("LSP-990121", result.Serial);
        Assert.Equal(PsxContentType.Educational, result.ContentType);
    }
    
    [Fact]
    public void Parse_CheatDisc_ClassifiesAsCheat()
    {
        // Arrange
        var filename = "Xploder vv2.0 (Europe).bin";
        
        // Act
        var result = _parser.Parse(filename);
        
        // Assert
        Assert.Equal(PsxContentType.Cheat, result.ContentType);
        Assert.NotNull(result.Warning);
    }
}
