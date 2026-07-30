using ARK.Core.Systems.PSX;

namespace ARK.Tests.Systems.PSX;

/// <summary>
/// Tests for separate SKU scenarios (Command and Conquer variants, Shockwave, etc.)
/// where discs are independent titles, not multi-disc sets
/// </summary>
public class PsxSeparateSkuTests : IDisposable
{
    private readonly string _testRoot;
    private readonly PsxPlaylistPlanner _planner;
    
    public PsxSeparateSkuTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ark-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);
        _planner = new PsxPlaylistPlanner();
    }
    
    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }
    
    [Fact]
    public void PlanPlaylists_CommandAndConquerVariants_NoPlaylist()
    {
        // Arrange - C&C separate faction SKUs
        File.WriteAllText(Path.Combine(_testRoot, "Command & Conquer (GDI) (USA) [SLUS-00379].cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Command & Conquer (NOD) (USA) [SLUS-00377].cue"), "");
        
        // Act
        var operations = _planner.PlanPlaylists(_testRoot, recursive: false);
        
        // Assert - Should not create playlists since these are separate titles
        Assert.Empty(operations);
    }
    
    [Fact]
    public void PlanPlaylists_ShockwaveAssaultVariants_NoPlaylist()
    {
        // Arrange - Shockwave Assault separate episode SKUs with (Disc N) markers
        File.WriteAllText(Path.Combine(_testRoot, "Shockwave Assault (Shockwave - Invasion Earth) (USA) [SLUS-00028] (Disc 1).cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Shockwave Assault (Shockwave - Operation Jumpgate) (USA) [SLUS-00137] (Disc 2).cue"), "");
        
        // Act
        var operations = _planner.PlanPlaylists(_testRoot, recursive: false);
        
        // Assert - Should not create playlists since these have different titles
        // "Shockwave Assault (Shockwave - Invasion Earth)" != "Shockwave Assault (Shockwave - Operation Jumpgate)"
        Assert.Empty(operations);
    }
    
    [Fact]
    public void PlanPlaylists_RedAlertVariants_NoPlaylist()
    {
        // Arrange - Red Alert separate faction SKUs
        File.WriteAllText(Path.Combine(_testRoot, "Command & Conquer - Red Alert (Allies) (USA) [SLUS-00431].cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Command & Conquer - Red Alert (Soviet) (USA) [SLUS-00486].cue"), "");
        
        // Act
        var operations = _planner.PlanPlaylists(_testRoot, recursive: false);
        
        // Assert
        Assert.Empty(operations);
    }
    
    [Fact]
    public void PlanPlaylists_RetaliationVariants_NoPlaylist()
    {
        // Arrange - Retaliation separate faction SKUs
        File.WriteAllText(Path.Combine(_testRoot, "Command & Conquer - Red Alert - Retaliation (Allies) (USA) [SLUS-00485].cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Command & Conquer - Red Alert - Retaliation (Soviet) (USA) [SLUS-00421].cue"), "");
        
        // Act
        var operations = _planner.PlanPlaylists(_testRoot, recursive: false);
        
        // Assert
        Assert.Empty(operations);
    }
    
    [Fact]
    public void PlanPlaylists_TrueMultiDiscFF8_CreatesPlaylist()
    {
        // Arrange - FF8 true multi-disc (same title, different serials)
        File.WriteAllText(Path.Combine(_testRoot, "Final Fantasy VIII (USA) [SLUS-00892] (Disc 1).cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Final Fantasy VIII (USA) [SLUS-00908] (Disc 2).cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Final Fantasy VIII (USA) [SLUS-00909] (Disc 3).cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Final Fantasy VIII (USA) [SLUS-01080] (Disc 4).cue"), "");
        
        // Act
        var operations = _planner.PlanPlaylists(_testRoot, recursive: false);
        
        // Assert - Should create playlist for true multi-disc
        Assert.Single(operations);
        Assert.Equal("Final Fantasy VIII", operations[0].Title);
        Assert.Equal(4, operations[0].DiscFilenames.Count);
    }
}
