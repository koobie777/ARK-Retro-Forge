using ARK.Core.Systems.PSX;

namespace ARK.Tests.Systems.PSX;

public class PsxMultiTrackTests
{
    private readonly PsxNameParser _parser;
    
    public PsxMultiTrackTests()
    {
        _parser = new PsxNameParser();
    }
    
    [Fact]
    public void Parse_TrackBin_DetectsTrackNumber()
    {
        // Arrange
        var filename = "The Adventures of Lomax (USA) (Track 02).bin";
        
        // Act
        var result = _parser.Parse(filename);
        
        // Assert
        Assert.Equal(2, result.TrackNumber);
        Assert.True(result.IsAudioTrack);
        Assert.Contains("Audio track from multi-track disc", result.Warning!);
    }
    
    [Fact]
    public void Parse_Track01_IsNotAudioTrack()
    {
        // Arrange
        var filename = "Game Title (USA) (Track 01).bin";
        
        // Act
        var result = _parser.Parse(filename);
        
        // Assert
        Assert.Equal(1, result.TrackNumber);
        Assert.False(result.IsAudioTrack);
    }
    
    [Fact]
    public void Parse_Track25_IsAudioTrack()
    {
        // Arrange
        var filename = "The Adventures of Lomax (USA) (Track 25).bin";
        
        // Act
        var result = _parser.Parse(filename);
        
        // Assert
        Assert.Equal(25, result.TrackNumber);
        Assert.True(result.IsAudioTrack);
    }
    
    [Fact]
    public void Parse_NoTrackSuffix_NoTrackDetected()
    {
        // Arrange
        var filename = "Final Fantasy VII (USA) [SCUS-94163].bin";
        
        // Act
        var result = _parser.Parse(filename);
        
        // Assert
        Assert.Null(result.TrackNumber);
        Assert.False(result.IsAudioTrack);
        Assert.False(result.IsMultiTrack);
    }
    
    [Fact]
    public void Parse_TrackBin_ExtractsTitleWithoutTrackSuffix()
    {
        // Arrange
        var filename = "The Adventures of Lomax (USA) (Track 15).bin";
        
        // Act
        var result = _parser.Parse(filename);
        
        // Assert
        Assert.Equal("The Adventures of Lomax (USA)", result.Title);
        Assert.DoesNotContain("Track", result.Title!);
    }
}
