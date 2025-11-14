using ARK.Core.PSX;

namespace ARK.Tests.PSX;

public class PsxSerialResolverTests
{
    [Theory]
    [InlineData("Final Fantasy VII (USA) [SLUS-00001].bin", "SLUS-00001")]
    [InlineData("Crash Bandicoot [SCUS-94163].cue", "SCUS-94163")]
    [InlineData("Gran Turismo [SCUS-94164].bin", "SCUS-94164")]
    [InlineData("Game [SLPS-01234].bin", "SLPS-01234")]
    [InlineData("Game [SLES-12345].bin", "SLES-12345")]
    [InlineData("Game.bin", null)]
    [InlineData("Game (USA).bin", null)]
    public void ExtractSerialFromFilename_VariousFilenames_ReturnsCorrectSerial(string filename, string? expected)
    {
        var result = PsxSerialResolver.ExtractSerialFromFilename(filename);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("SLUS-00001", true)]
    [InlineData("SCUS-94163", true)]
    [InlineData("SLPS-01234", true)]
    [InlineData("SCPS-12345", true)]
    [InlineData("SLES-12345", true)]
    [InlineData("SCES-12345", true)]
    [InlineData("LSP-12345", true)]
    [InlineData("INVALID", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidSerial_VariousSerials_ReturnsCorrectly(string? serial, bool expected)
    {
        var result = PsxSerialResolver.IsValidSerial(serial);
        Assert.Equal(expected, result);
    }
}
