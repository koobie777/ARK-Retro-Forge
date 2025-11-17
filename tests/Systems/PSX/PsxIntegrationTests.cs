using ARK.Core.Systems.PSX;

namespace ARK.Tests.Systems.PSX;

public class PsxIntegrationTests
{
    [Fact]
    public void AloneInTheDark_MultiDiscRename_CorrectlyNormalizesDiscSuffixes()
    {
        // Arrange - simulate the user's scenario
        var testDir = Path.Combine(Path.GetTempPath(), "ark-test-aitd-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);

        try
        {
            // Create test files matching user's scenario
            var disc1Bin = Path.Combine(testDir, "Alone in the Dark - The New Nightmare (USA) [SLUS-01201] (Disc 1 of 2).bin");
            var disc1Cue = Path.Combine(testDir, "Alone in the Dark - The New Nightmare (USA) [SLUS-01201] (Disc 1 of 2).cue");
            var disc2Bin = Path.Combine(testDir, "Alone in the Dark - The New Nightmare (USA) [SLUS-01377].bin");
            var disc2Cue = Path.Combine(testDir, "Alone in the Dark - The New Nightmare (USA) [SLUS-01377].cue");
            
            File.WriteAllText(disc1Bin, "");
            File.WriteAllText(disc1Cue, "");
            File.WriteAllText(disc2Bin, "");
            File.WriteAllText(disc2Cue, "");

            var planner = new PsxRenamePlanner();

            // Act
            var operations = planner.PlanRenames(testDir, recursive: false);

            // Assert
            Assert.Equal(4, operations.Count);

            // Disc 1 BIN should normalize from "Disc 1 of 2" to "Disc 1"
            var disc1BinOp = operations.First(o => o.SourcePath == disc1Bin);
            Assert.False(disc1BinOp.IsAlreadyNamed);
            Assert.Contains("(Disc 1)", disc1BinOp.DestinationPath);
            Assert.EndsWith(".bin", disc1BinOp.DestinationPath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("of 2", disc1BinOp.DestinationPath);

            // Disc 1 CUE should normalize from "Disc 1 of 2" to "Disc 1"
            var disc1CueOp = operations.First(o => o.SourcePath == disc1Cue);
            Assert.False(disc1CueOp.IsAlreadyNamed);
            Assert.Contains("(Disc 1)", disc1CueOp.DestinationPath);
            Assert.EndsWith(".cue", disc1CueOp.DestinationPath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("of 2", disc1CueOp.DestinationPath);

            // Disc 2 BIN lacked suffix originally but multi-disc grouping should inject it now
            var disc2BinOp = operations.First(o => o.SourcePath == disc2Bin);
            Assert.False(disc2BinOp.IsAlreadyNamed);
            Assert.Contains("(Disc 2)", disc2BinOp.DestinationPath);

            // Disc 2 CUE also receives the generated suffix
            var disc2CueOp = operations.First(o => o.SourcePath == disc2Cue);
            Assert.False(disc2CueOp.IsAlreadyNamed);
            Assert.Contains("(Disc 2)", disc2CueOp.DestinationPath);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    [Fact]
    public void AloneInTheDark_MultiDiscConvert_ShowsCorrectDiscNumbers()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), "ark-test-aitd-convert-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);

        try
        {
            // Create test CUE files with proper multi-disc format
            var disc1Cue = Path.Combine(testDir, "Alone in the Dark - The New Nightmare (USA) [SLUS-01201] (Disc 1 of 2).cue");
            var disc2Cue = Path.Combine(testDir, "Alone in the Dark - The New Nightmare (USA) [SLUS-01377] (Disc 2 of 2).cue");
            
            File.WriteAllText(disc1Cue, "FILE \"disc1.bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00");
            File.WriteAllText(disc2Cue, "FILE \"disc2.bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00");

            var planner = new PsxConvertPlanner();

            // Act
            var operations = planner.PlanConversions(testDir, recursive: false);

            // Assert
            Assert.Equal(2, operations.Count);

            var disc1Op = operations.First(o => o.SourcePath == disc1Cue);
            Assert.Equal(1, disc1Op.DiscInfo.DiscNumber);
            Assert.Equal(2, disc1Op.DiscInfo.DiscCount);
            Assert.True(disc1Op.DiscInfo.IsMultiDisc);
            Assert.Contains("(Disc 1)", disc1Op.DestinationPath);
            Assert.EndsWith(".chd", disc1Op.DestinationPath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("of 2", disc1Op.DestinationPath);

            var disc2Op = operations.First(o => o.SourcePath == disc2Cue);
            Assert.Equal(2, disc2Op.DiscInfo.DiscNumber);
            Assert.Equal(2, disc2Op.DiscInfo.DiscCount);
            Assert.True(disc2Op.DiscInfo.IsMultiDisc);
            Assert.Contains("(Disc 2)", disc2Op.DestinationPath);
            Assert.EndsWith(".chd", disc2Op.DestinationPath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("of 2", disc2Op.DestinationPath);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    [Fact]
    public void CheatAndEducationalDiscs_ProperlyClassified()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), "ark-test-classify-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);

        try
        {
            // Create test files for different content types
            var cheatFile = Path.Combine(testDir, "Xploder vv2.0 (Europe).bin");
            var eduFile = Path.Combine(testDir, "16 Tales 1 [LSP-990121].bin");
            var normalFile = Path.Combine(testDir, "Final Fantasy VII (USA) [SCUS-94163].bin");
            
            File.WriteAllText(cheatFile, "");
            File.WriteAllText(eduFile, "");
            File.WriteAllText(normalFile, "");

            var planner = new PsxRenamePlanner();

            // Act
            var operations = planner.PlanRenames(testDir, recursive: false);

            // Assert
            var cheatOp = operations.First(o => o.SourcePath == cheatFile);
            Assert.Equal(PsxContentType.Cheat, cheatOp.DiscInfo.ContentType);
            Assert.NotNull(cheatOp.Warning);
            Assert.Contains("Cheat/utility disc", cheatOp.Warning);

            var eduOp = operations.First(o => o.SourcePath == eduFile);
            Assert.Equal(PsxContentType.Educational, eduOp.DiscInfo.ContentType);

            var normalOp = operations.First(o => o.SourcePath == normalFile);
            Assert.Equal(PsxContentType.Mainline, normalOp.DiscInfo.ContentType);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }
}
