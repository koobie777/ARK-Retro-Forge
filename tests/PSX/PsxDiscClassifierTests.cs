using ARK.Core.PSX;

namespace ARK.Tests.PSX;

public class PsxDiscClassifierTests
{
    [Theory]
    [InlineData("GameShark Ultimate Codes", true)]
    [InlineData("Game Shark", true)]
    [InlineData("Xploder Ultimate Cheat Codes", true)]
    [InlineData("X-Ploder Pro", true)]
    [InlineData("Action Replay", true)]
    [InlineData("CodeBreaker", true)]
    [InlineData("Pro Action Replay", true)]
    [InlineData("Cheat Engine", true)]
    [InlineData("Normal Game", false)]
    [InlineData("Final Fantasy VII", false)]
    public void IsCheatDisc_VariousTitles_ReturnsCorrectly(string title, bool expected)
    {
        var result = PsxDiscClassifier.IsCheatDisc(title);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Lightspan Adventures", true)]
    [InlineData("Adventures in Learning", true)]
    [InlineData("Click Start Math", true)]
    [InlineData("LeapFrog Learning", true)]
    [InlineData("Normal Game", false)]
    [InlineData("Crash Bandicoot", false)]
    public void IsEducationalDisc_VariousTitles_ReturnsCorrectly(string title, bool expected)
    {
        var result = PsxDiscClassifier.IsEducationalDisc(title);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("LSP-12345", true)]
    [InlineData("lsp-00001", true)]
    [InlineData("SLUS-00001", false)]
    [InlineData("SCUS-94163", false)]
    [InlineData(null, false)]
    public void IsLightspanSerial_VariousSerials_ReturnsCorrectly(string? serial, bool expected)
    {
        var result = PsxDiscClassifier.IsLightspanSerial(serial);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ClassifyDisc_CheatTitle_ReturnsCheat()
    {
        var (isCheat, isEducational) = PsxDiscClassifier.ClassifyDisc("GameShark", null);
        Assert.True(isCheat);
        Assert.False(isEducational);
    }

    [Fact]
    public void ClassifyDisc_EducationalTitle_ReturnsEducational()
    {
        var (isCheat, isEducational) = PsxDiscClassifier.ClassifyDisc("Lightspan Adventures", null);
        Assert.False(isCheat);
        Assert.True(isEducational);
    }

    [Fact]
    public void ClassifyDisc_LightspanSerial_OverridesTitleClassification()
    {
        // Even if title looks like cheat, Lightspan serial takes precedence
        var (isCheat, isEducational) = PsxDiscClassifier.ClassifyDisc("Some Cheat", "LSP-12345");
        Assert.False(isCheat);
        Assert.True(isEducational);
    }

    [Fact]
    public void ClassifyDisc_NormalGame_ReturnsBothFalse()
    {
        var (isCheat, isEducational) = PsxDiscClassifier.ClassifyDisc("Final Fantasy VII", "SLUS-00001");
        Assert.False(isCheat);
        Assert.False(isEducational);
    }
}
