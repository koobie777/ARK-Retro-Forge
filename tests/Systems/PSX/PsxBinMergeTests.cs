using ARK.Core.Systems.PSX;

namespace ARK.Tests.Systems.PSX;

public class PsxBinMergeTests
{
    [Fact]
    public async Task MergeMultiTrackCue_WritesSingleBinAndCue()
    {
        var temp = Directory.CreateTempSubdirectory();
        var root = temp.FullName;

        var track1 = Path.Combine(root, "Demo Game (USA) (Track 01).bin");
        var track2 = Path.Combine(root, "Demo Game (USA) (Track 02).bin");
        File.WriteAllBytes(track1, new byte[2352 * 2]);
        File.WriteAllBytes(track2, new byte[2352 * 1]);

        var cuePath = Path.Combine(root, "Demo Game (USA) [SLUS-00000].cue");
        var cueContents = """
FILE "Demo Game (USA) (Track 01).bin" BINARY
  TRACK 01 MODE2/2352
    INDEX 01 00:00:00
FILE "Demo Game (USA) (Track 02).bin" BINARY
  TRACK 02 AUDIO
    INDEX 01 00:00:00
""";
        File.WriteAllText(cuePath, cueContents);

        var planner = new PsxBinMergePlanner();
        var operations = planner.PlanMerges(root);
        Assert.Single(operations);

        var operation = operations[0];
        var service = new PsxBinMergeService();
        await service.MergeAsync(operation, deleteSources: false);

        Assert.True(File.Exists(operation.DestinationBinPath));
        Assert.True(File.Exists(operation.DestinationCuePath));

        var mergedLength = new FileInfo(operation.DestinationBinPath).Length;
        Assert.Equal(new FileInfo(track1).Length + new FileInfo(track2).Length, mergedLength);

        var updatedCue = File.ReadAllText(operation.DestinationCuePath);
        Assert.Contains($"FILE \"{Path.GetFileName(operation.DestinationBinPath)}\" BINARY", updatedCue);
        Assert.Contains("TRACK 02 AUDIO", updatedCue);
    }
}
