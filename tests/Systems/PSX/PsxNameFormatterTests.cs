using ARK.Core.Systems.PSX;

namespace ARK.Tests.Systems.PSX;

public class PsxNameFormatterTests
{
    [Fact]
    public void Format_SingleDisc_NoDiscSuffix()
    {
        // Arrange
        var discInfo = new PsxDiscInfo
        {
            FilePath = "test.bin",
            Title = "Final Fantasy VII",
            Region = "USA",
            Serial = "SCUS-94163",
            Extension = ".bin",
            DiscNumber = null,
            DiscCount = null
        };
        
        // Act
        var result = PsxNameFormatter.Format(discInfo);
        
        // Assert
        Assert.Equal("Final Fantasy VII (USA) [SCUS-94163].bin", result);
    }
    
    [Fact]
    public void Format_MultiDisc_IncludesDiscSuffix()
    {
        // Arrange
        var discInfo = new PsxDiscInfo
        {
            FilePath = "test.bin",
            Title = "Alone in the Dark - The New Nightmare",
            Region = "USA",
            Serial = "SLUS-01201",
            Extension = ".bin",
            DiscNumber = 1,
            DiscCount = 2
        };
        
        // Act
        var result = PsxNameFormatter.Format(discInfo);
        
        // Assert
        Assert.Equal("Alone in the Dark - The New Nightmare (USA) (Disc 1) [SLUS-01201].bin", result);
    }
    
    [Fact]
    public void Format_MultiDiscCHD_IncludesDiscSuffix()
    {
        // Arrange
        var discInfo = new PsxDiscInfo
        {
            FilePath = "test.chd",
            Title = "Alone in the Dark - The New Nightmare",
            Region = "USA",
            Serial = "SLUS-01377",
            Extension = ".chd",
            DiscNumber = 2,
            DiscCount = 2
        };
        
        // Act
        var result = PsxNameFormatter.Format(discInfo);
        
        // Assert
        Assert.Equal("Alone in the Dark - The New Nightmare (USA) (Disc 2) [SLUS-01377].chd", result);
    }
    
    [Fact]
    public void NormalizeDiscSuffix_ConvertsOfFormat()
    {
        // Arrange
        var filename = "Game Title (USA) [SLUS-12345] (Disc 1 of 2).bin";
        
        // Act
        var result = PsxNameFormatter.NormalizeDiscSuffix(filename);
        
        // Assert
        Assert.Equal("Game Title (USA) [SLUS-12345] (Disc 1).bin", result);
    }
    
    [Fact]
    public void NormalizeDiscSuffix_PreservesAlreadyNormalized()
    {
        // Arrange
        var filename = "Game Title (USA) [SLUS-12345] (Disc 2).cue";
        
        // Act
        var result = PsxNameFormatter.NormalizeDiscSuffix(filename);
        
        // Assert
        Assert.Equal("Game Title (USA) [SLUS-12345] (Disc 2).cue", result);
    }
    
    [Fact]
    public void Format_NoSerial_OmitsSerialBrackets()
    {
        // Arrange
        var discInfo = new PsxDiscInfo
        {
            FilePath = "test.bin",
            Title = "Educational Title",
            Region = "USA",
            Serial = null,
            Extension = ".bin"
        };
        
        // Act
        var result = PsxNameFormatter.Format(discInfo);
        
        // Assert
        Assert.Equal("Educational Title (USA).bin", result);
    }
    
    [Fact]
    public void Format_LightspanTitle_NoDoubleSpacing()
    {
        // Arrange - Test 16 Tales case from problem statement
        var discInfo = new PsxDiscInfo
        {
            FilePath = "test.bin",
            Title = "16 Tales 1",
            Region = "USA",
            Serial = "LSP-990121",
            Extension = ".bin"
        };
        
        // Act
        var result = PsxNameFormatter.Format(discInfo);
        
        // Assert
        Assert.Equal("16 Tales 1 (USA) [LSP-990121].bin", result);
        Assert.DoesNotContain("  ", result); // No double spaces
    }
    
    [Fact]
    public void Format_TitleWithTrailingSpace_TrimsCorrectly()
    {
        // Arrange - Title with trailing space
        var discInfo = new PsxDiscInfo
        {
            FilePath = "test.bin",
            Title = "Secret of Googol ",  // Note trailing space
            Region = "USA",
            Serial = "LSP-06015",
            Extension = ".bin"
        };
        
        // Act
        var result = PsxNameFormatter.Format(discInfo);
        
        // Assert
        Assert.Equal("Secret of Googol (USA) [LSP-06015].bin", result);
        Assert.DoesNotContain("  ", result); // No double spaces
    }

    [Fact]
    public void Format_AudioTrack_AddsTrackSuffix()
    {
        var discInfo = new PsxDiscInfo
        {
            FilePath = "track02.bin",
            Title = "Game Title",
            Region = "USA",
            Serial = "SLUS-00000",
            Extension = ".bin",
            TrackNumber = 2,
            TrackCount = 10,
            IsAudioTrack = true
        };

        var result = PsxNameFormatter.Format(discInfo);

        Assert.Equal("Game Title (USA) (Track 02) [SLUS-00000].bin", result);
    }

    [Fact]
    public void Format_DataTrackOne_PreservesTrackSuffix()
    {
        var discInfo = new PsxDiscInfo
        {
            FilePath = "track01.bin",
            Title = "Game Title",
            Region = "USA",
            Serial = "SLUS-00001",
            Extension = ".bin",
            TrackNumber = 1,
            TrackCount = 10,
            IsAudioTrack = false
        };

        var result = PsxNameFormatter.Format(discInfo);

        Assert.Equal("Game Title (USA) (Track 01) [SLUS-00001].bin", result);
    }

    [Fact]
    public void Format_RestoreArticles_MovesArticleToFront()
    {
        var discInfo = new PsxDiscInfo
        {
            FilePath = "test.bin",
            Title = "Legend of Dragoon, The",
            Region = "USA",
            Serial = "SCUS-94491",
            Extension = ".bin"
        };

        var result = PsxNameFormatter.Format(discInfo, restoreArticles: true);

        Assert.Equal("The Legend of Dragoon (USA) [SCUS-94491].bin", result);
    }

    [Fact]
    public void Format_DuplicateRegionSuffix_StripsExtras()
    {
        var discInfo = new PsxDiscInfo
        {
            FilePath = "test.bin",
            Title = "BrainDead 13 (USA) (USA)",
            Region = "USA",
            Extension = ".bin"
        };

        var result = PsxNameFormatter.Format(discInfo);

        Assert.Equal("BrainDead 13 (USA).bin", result);
    }

    [Fact]
    public void Format_TitleWithDiscsRange_NormalizesLegacySuffix()
    {
        var discInfo = new PsxDiscInfo
        {
            FilePath = "test.bin",
            Title = "BrainDead 13 (USA) (Discs 1-2)",
            Region = "USA",
            Extension = ".bin",
            DiscNumber = 2,
            DiscCount = 2
        };

        var result = PsxNameFormatter.Format(discInfo);

        Assert.Equal("BrainDead 13 (USA) (Disc 2).bin", result);
    }
}
