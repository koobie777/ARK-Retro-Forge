using ARK.Core.Systems.PSX;

namespace ARK.Tests.Systems.PSX;

public class PsxContentClassifierTests
{
    private readonly PsxContentClassifier _classifier;
    
    public PsxContentClassifierTests()
    {
        _classifier = new PsxContentClassifier();
    }
    
    [Theory]
    [InlineData("Xploder vv2.0 (Europe).bin", null, PsxContentType.Cheat)]
    [InlineData("Action Replay for PSX & PSone (Europe).bin", null, PsxContentType.Cheat)]
    [InlineData("GamePro Action (USA).bin", null, PsxContentType.Cheat)]
    [InlineData("GameShark (USA).bin", null, PsxContentType.Cheat)]
    public void Classify_CheatDiscs_ReturnsCheat(string filename, string? serial, PsxContentType expected)
    {
        // Act
        var result = _classifier.Classify(filename, serial);
        
        // Assert
        Assert.Equal(expected, result);
    }
    
    [Theory]
    [InlineData("16 Tales 1.bin", "LSP-990121", PsxContentType.Educational)]
    [InlineData("P.K.'s Math Studio.bin", "LSP-06019", PsxContentType.Educational)]
    [InlineData("Science Is Elementary.bin", null, PsxContentType.Educational)]
    [InlineData("The Secret of Googol.bin", null, PsxContentType.Educational)]
    public void Classify_EducationalDiscs_ReturnsEducational(string filename, string? serial, PsxContentType expected)
    {
        // Act
        var result = _classifier.Classify(filename, serial);
        
        // Assert
        Assert.Equal(expected, result);
    }
    
    [Theory]
    [InlineData("Final Fantasy VII (USA) [SCUS-94163].bin", "SCUS-94163", PsxContentType.Mainline)]
    [InlineData("Alone in the Dark (USA) [SLUS-01201].bin", "SLUS-01201", PsxContentType.Mainline)]
    public void Classify_MainlineGames_ReturnsMainline(string filename, string? serial, PsxContentType expected)
    {
        // Act
        var result = _classifier.Classify(filename, serial);
        
        // Assert
        Assert.Equal(expected, result);
    }
    
    [Theory]
    [InlineData("Demo Disc (USA).bin", null, PsxContentType.Demo)]
    [InlineData("Preview Disc (USA).bin", null, PsxContentType.Demo)]
    public void Classify_DemoDiscs_ReturnsDemo(string filename, string? serial, PsxContentType expected)
    {
        // Act
        var result = _classifier.Classify(filename, serial);
        
        // Assert
        Assert.Equal(expected, result);
    }
}
