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

    // --- P2 region-duplication cases ---

    [Fact]
    public void Parse_SimpleFormatNoSerial_ExtractsTitleAndRegion()
    {
        var result = _parser.Parse("Final Fantasy VII (USA).bin");
        Assert.Equal("Final Fantasy VII", result.Title);
        Assert.Equal("USA", result.Region);
        Assert.Null(result.DiscNumber);
    }

    [Fact]
    public void Parse_DuplicateRegionInFilename_RegionExtractedAndRoundTripClean()
    {
        // Parser leaves the extra (USA) in Title; formatter must strip it
        var result = _parser.Parse("Final Fantasy VII (USA) (USA).bin");
        Assert.Equal("USA", result.Region);
        var formatted = PsxNameFormatter.Format(result with { Extension = ".bin" });
        Assert.Equal("Final Fantasy VII (USA).bin", formatted);
        Assert.DoesNotContain("(USA) (USA)", formatted);
    }

    [Fact]
    public void Parse_MultiDiscWithRegionNoSerial_ExtractsTitleRegionAndDisc()
    {
        // (Disc 1) blocks SimplePattern; Step 5b must rescue region from the cleaned title
        var result = _parser.Parse("Final Fantasy VII (USA) (Disc 1).bin");
        Assert.Equal("Final Fantasy VII", result.Title);
        Assert.Equal("USA", result.Region);
        Assert.Equal(1, result.DiscNumber);
    }

    [Fact]
    public void Parse_MultiDiscWithRegionNoSerial_FormatterOutputIsIdempotent()
    {
        // DiscCount is unknown without DAT, so IsMultiDisc is false and the formatter
        // drops the disc suffix. Whatever the formatter produces on the first pass
        // must be reproduced identically on the second pass.
        var first = _parser.Parse("Final Fantasy VII (USA) (Disc 1).bin");
        var firstFormatted = PsxNameFormatter.Format(first with { Extension = ".bin" });

        var second = _parser.Parse(firstFormatted);
        var secondFormatted = PsxNameFormatter.Format(second with { Extension = ".bin" });
        Assert.Equal(firstFormatted, secondFormatted);
    }

    [Fact]
    public void Parse_LanguageTagsWithRegion_PreservesTagsInTitle()
    {
        // Language tags are embedded in Title; stripping them is the rename planner's job
        var result = _parser.Parse("Some Game (En,Fr,De) (USA).bin");
        Assert.Equal("Some Game (En,Fr,De)", result.Title);
        Assert.Equal("USA", result.Region);
    }
}
