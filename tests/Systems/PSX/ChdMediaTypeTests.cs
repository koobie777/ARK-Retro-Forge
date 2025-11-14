using ARK.Core.Systems.PSX;

namespace ARK.Tests.Systems.PSX;

public class ChdMediaTypeTests
{
    [Fact]
    public void DetermineFromExtensionOrContext_PsxContext_ReturnsCD()
    {
        // Act
        var result = ChdMediaTypeHelper.DetermineFromExtensionOrContext(".bin", "PSX");
        
        // Assert
        Assert.Equal(ChdMediaType.CD, result);
    }
    
    [Fact]
    public void DetermineFromExtensionOrContext_BinExtension_ReturnsCD()
    {
        // Act
        var result = ChdMediaTypeHelper.DetermineFromExtensionOrContext(".bin");
        
        // Assert
        Assert.Equal(ChdMediaType.CD, result);
    }
    
    [Fact]
    public void DetermineFromExtensionOrContext_CueExtension_ReturnsCD()
    {
        // Act
        var result = ChdMediaTypeHelper.DetermineFromExtensionOrContext(".cue");
        
        // Assert
        Assert.Equal(ChdMediaType.CD, result);
    }
    
    [Fact]
    public void DetermineFromExtensionOrContext_IsoExtension_ReturnsCD()
    {
        // Act
        var result = ChdMediaTypeHelper.DetermineFromExtensionOrContext(".iso");
        
        // Assert
        Assert.Equal(ChdMediaType.CD, result);
    }
    
    [Fact]
    public void DetermineFromExtensionOrContext_UnknownExtension_ReturnsUnknown()
    {
        // Act
        var result = ChdMediaTypeHelper.DetermineFromExtensionOrContext(".xyz");
        
        // Assert
        Assert.Equal(ChdMediaType.Unknown, result);
    }
    
    [Fact]
    public void GetChdmanCommand_CD_ReturnsCreatecd()
    {
        // Act
        var result = ChdMediaTypeHelper.GetChdmanCommand(ChdMediaType.CD);
        
        // Assert
        Assert.Equal("createcd", result);
    }
    
    [Fact]
    public void GetChdmanCommand_DVD_ReturnsCreatedvd()
    {
        // Act
        var result = ChdMediaTypeHelper.GetChdmanCommand(ChdMediaType.DVD);
        
        // Assert
        Assert.Equal("createdvd", result);
    }
    
    [Fact]
    public void GetChdmanCommand_Unknown_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            ChdMediaTypeHelper.GetChdmanCommand(ChdMediaType.Unknown));
    }
}
