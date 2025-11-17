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

    [Fact]
    public async Task MergeMultiTrackCue_DeleteSources_RemovesBinsAndFolders()
    {
        var temp = Directory.CreateTempSubdirectory();
        var root = temp.FullName;
        var segmentsDir = Path.Combine(root, "Segments");
        var nestedDir = Path.Combine(segmentsDir, "Audio");
        Directory.CreateDirectory(nestedDir);

        var track1 = Path.Combine(nestedDir, "Demo Game (USA) (Track 01).bin");
        var track2 = Path.Combine(nestedDir, "Demo Game (USA) (Track 02).bin");
        File.WriteAllBytes(track1, new byte[2352 * 2]);
        File.WriteAllBytes(track2, new byte[2352 * 1]);

        var cuePath = Path.Combine(root, "Demo Game (USA) [SLUS-00000].cue");
        var cueContents = """
FILE "Segments\Audio\Demo Game (USA) (Track 01).bin" BINARY
  TRACK 01 MODE2/2352
    INDEX 01 00:00:00
FILE "Segments\Audio\Demo Game (USA) (Track 02).bin" BINARY
  TRACK 02 AUDIO
    INDEX 01 00:00:00
""";
        File.WriteAllText(cuePath, cueContents);

        var planner = new PsxBinMergePlanner();
        var operations = planner.PlanMerges(root);
        Assert.Single(operations);

        var operation = operations[0];
        var service = new PsxBinMergeService();
        await service.MergeAsync(operation, deleteSources: true);

        Assert.True(File.Exists(operation.DestinationBinPath));
        Assert.True(File.Exists(operation.DestinationCuePath));
        Assert.False(File.Exists(track1));
        Assert.False(File.Exists(track2));
        Assert.False(Directory.Exists(nestedDir));
        Assert.False(Directory.Exists(segmentsDir));
    }

    [Fact]
    public async Task MergeMultiTrackCue_ExistingMergedFiles_AreOverwritten()
    {
        var temp = Directory.CreateTempSubdirectory();
        var root = temp.FullName;

        var track1 = Path.Combine(root, "Demo Game (USA) (Track 01).bin");
        var track2 = Path.Combine(root, "Demo Game (USA) (Track 02).bin");
        var track1Data = new byte[2352];
        var track2Data = new byte[2352];
        Array.Fill(track1Data, (byte)0x11);
        Array.Fill(track2Data, (byte)0x22);
        File.WriteAllBytes(track1, track1Data);
        File.WriteAllBytes(track2, track2Data);

        var cuePath = Path.Combine(root, "Demo Game (USA) [SLUS-00000].cue");
        File.WriteAllText(cuePath,
            """
FILE "Demo Game (USA) (Track 01).bin" BINARY
  TRACK 01 MODE2/2352
    INDEX 01 00:00:00
FILE "Demo Game (USA) (Track 02).bin" BINARY
  TRACK 02 AUDIO
    INDEX 01 00:00:00
""");

        var planner = new PsxBinMergePlanner();
        var operations = planner.PlanMerges(root);
        Assert.Single(operations);
        var operation = operations[0];

        // Seed existing merged output that should be replaced.
        File.WriteAllBytes(operation.DestinationBinPath, new byte[] { 0x99, 0x99 });
        File.WriteAllText(operation.DestinationCuePath, "FILE \"old.bin\" BINARY\n");

        var service = new PsxBinMergeService();
        await service.MergeAsync(operation, deleteSources: false);

        var mergedLength = new FileInfo(operation.DestinationBinPath).Length;
        Assert.Equal(new FileInfo(track1).Length + new FileInfo(track2).Length, mergedLength);

        var updatedCue = File.ReadAllText(operation.DestinationCuePath);
        Assert.Contains(Path.GetFileName(operation.DestinationBinPath), updatedCue);
        Assert.Contains("TRACK 02 AUDIO", updatedCue);
    }
}
