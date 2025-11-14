using ARK.Core.PSX;

namespace ARK.Tests.PSX;

public class DiscSuffixNormalizerTests
{
    [Theory]
    [InlineData("Game (Disc 1 of 2).bin", 1)]
    [InlineData("Game (Disc 2 of 2).cue", 2)]
    [InlineData("Game (Disc 1).bin", 1)]
    [InlineData("Game (Disc 3).bin", 3)]
    [InlineData("Game (CD 1).bin", 1)]
    [InlineData("Game (CD1).bin", 1)]
    [InlineData("Game (DVD 2 of 3).bin", 2)]
    [InlineData("Game (Disk 1).bin", 1)]
    public void ParseDiscNumber_ValidPatterns_ReturnsCorrectNumber(string filename, int expected)
    {
        var result = DiscSuffixNormalizer.ParseDiscNumber(filename);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Game.bin")]
    [InlineData("Game (USA).bin")]
    [InlineData("Game [SLUS-00001].bin")]
    public void ParseDiscNumber_NoDiscSuffix_ReturnsNull(string filename)
    {
        var result = DiscSuffixNormalizer.ParseDiscNumber(filename);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("Game (Disc 1 of 2).bin", 1, "Game (Disc 1).bin")]
    [InlineData("Game (Disc 2 of 3).cue", 2, "Game (Disc 2).cue")]
    [InlineData("Game (CD 1).bin", 1, "Game (Disc 1).bin")]
    [InlineData("Game (CD1).bin", 1, "Game (Disc 1).bin")]
    [InlineData("Game (Disk 2 of 2).bin", 2, "Game (Disc 2).bin")]
    public void NormalizeDiscSuffix_VariousPatterns_ReturnsCanonicalFormat(string input, int discNumber, string expected)
    {
        var result = DiscSuffixNormalizer.NormalizeDiscSuffix(input, discNumber);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Game (Disc 1 of 2).bin", "Game.bin")]
    [InlineData("Game (Disc 1).cue", "Game.cue")]
    [InlineData("Game (CD 1).bin", "Game.bin")]
    [InlineData("Game (DVD 2 of 3).bin", "Game.bin")]
    public void RemoveDiscSuffix_VariousPatterns_RemovesSuffix(string input, string expected)
    {
        var result = DiscSuffixNormalizer.RemoveDiscSuffix(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Game (Disc 1).bin", true)]
    [InlineData("Game (Disc 1 of 2).bin", true)]
    [InlineData("Game (CD 1).bin", true)]
    [InlineData("Game.bin", false)]
    [InlineData("Game (USA).bin", false)]
    public void HasDiscSuffix_VariousPatterns_ReturnsCorrectly(string filename, bool expected)
    {
        var result = DiscSuffixNormalizer.HasDiscSuffix(filename);
        Assert.Equal(expected, result);
    }
}
