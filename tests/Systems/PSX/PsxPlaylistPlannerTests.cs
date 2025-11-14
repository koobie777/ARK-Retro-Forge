using ARK.Core.Systems.PSX;

namespace ARK.Tests.Systems.PSX;

public class PsxPlaylistPlannerTests : IDisposable
{
    private readonly string _testRoot;
    private readonly PsxPlaylistPlanner _planner;
    
    public PsxPlaylistPlannerTests()
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
    public void PlanPlaylists_MultiDiscTitle_CreatesPlaylist()
    {
        // Arrange - Create multi-disc title
        File.WriteAllText(Path.Combine(_testRoot, "Final Fantasy VIII (USA) [SLUS-00892] (Disc 1).cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Final Fantasy VIII (USA) [SLUS-00908] (Disc 2).cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Final Fantasy VIII (USA) [SLUS-00909] (Disc 3).cue"), "");
        
        // Act
        var operations = _planner.PlanPlaylists(_testRoot, recursive: false, preferredExtension: ".cue");
        
        // Assert
        Assert.Single(operations);
        var op = operations[0];
        Assert.Equal(PlaylistOperationType.Create, op.OperationType);
        Assert.Equal("Final Fantasy VIII", op.Title);
        Assert.Equal("USA", op.Region);
        Assert.Equal(3, op.DiscFilenames.Count);
        Assert.Contains("Final Fantasy VIII (USA) [SLUS-00892] (Disc 1).cue", op.DiscFilenames);
        Assert.EndsWith("Final Fantasy VIII (USA).m3u", op.PlaylistPath);
    }
    
    [Fact]
    public void PlanPlaylists_SingleDiscTitle_NoPlaylist()
    {
        // Arrange - Single disc title
        File.WriteAllText(Path.Combine(_testRoot, "Final Fantasy VII (USA) [SCUS-94163].cue"), "");
        
        // Act
        var operations = _planner.PlanPlaylists(_testRoot, recursive: false);
        
        // Assert
        Assert.Empty(operations);
    }
    
    [Fact]
    public void PlanPlaylists_PrefersCHDOverCUE()
    {
        // Arrange - Multi-disc with both CUE and CHD
        File.WriteAllText(Path.Combine(_testRoot, "Xenogears (USA) [SLUS-00664] (Disc 1).cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Xenogears (USA) [SLUS-00664] (Disc 1).chd"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Xenogears (USA) [SLUS-00669] (Disc 2).cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Xenogears (USA) [SLUS-00669] (Disc 2).chd"), "");
        
        // Act
        var operations = _planner.PlanPlaylists(_testRoot, recursive: false, preferredExtension: null);
        
        // Assert
        Assert.Single(operations);
        var op = operations[0];
        Assert.Equal(2, op.DiscFilenames.Count);
        Assert.All(op.DiscFilenames, filename => Assert.EndsWith(".chd", filename));
    }
    
    [Fact]
    public void PlanPlaylists_ExistingPlaylistUpToDate_NoOperation()
    {
        // Arrange - Multi-disc with existing correct playlist
        File.WriteAllText(Path.Combine(_testRoot, "Riven (USA) [SLUS-00482] (Disc 1).cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Riven (USA) [SLUS-00483] (Disc 2).cue"), "");
        
        var playlistPath = Path.Combine(_testRoot, "Riven (USA).m3u");
        File.WriteAllText(playlistPath, "Riven (USA) [SLUS-00482] (Disc 1).cue\nRiven (USA) [SLUS-00483] (Disc 2).cue");
        
        // Act
        var operations = _planner.PlanPlaylists(_testRoot, recursive: false, preferredExtension: ".cue");
        
        // Assert
        Assert.Empty(operations);
    }
    
    [Fact]
    public void PlanPlaylists_ExistingPlaylistOutdated_UpdateOperation()
    {
        // Arrange - Multi-disc with outdated playlist
        File.WriteAllText(Path.Combine(_testRoot, "Legend of Dragoon, The (USA) [SCUS-94491] (Disc 1).cue"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Legend of Dragoon, The (USA) [SCUS-94491] (Disc 1).chd"), "");
        
        var playlistPath = Path.Combine(_testRoot, "Legend of Dragoon, The (USA).m3u");
        File.WriteAllText(playlistPath, "Legend of Dragoon, The (USA) [SCUS-94491] (Disc 1).cue");
        
        // Act - Plan with CHD preference
        var operations = _planner.PlanPlaylists(_testRoot, recursive: false, preferredExtension: ".chd", updateExisting: true);
        
        // Assert
        Assert.Single(operations);
        var op = operations[0];
        Assert.Equal(PlaylistOperationType.Update, op.OperationType);
        Assert.Contains(".chd", op.DiscFilenames[0]);
    }
    
    [Fact]
    public void ApplyOperation_CreatePlaylist_WritesFile()
    {
        // Arrange
        var playlistPath = Path.Combine(_testRoot, "Test Game (USA).m3u");
        var operation = new PsxPlaylistOperation
        {
            PlaylistPath = playlistPath,
            Title = "Test Game",
            Region = "USA",
            DiscFilenames = new List<string> { "Test Game (USA) (Disc 1).chd", "Test Game (USA) (Disc 2).chd" },
            OperationType = PlaylistOperationType.Create
        };
        
        // Act
        _planner.ApplyOperation(operation);
        
        // Assert
        Assert.True(File.Exists(playlistPath));
        var content = File.ReadAllText(playlistPath);
        Assert.Contains("Test Game (USA) (Disc 1).chd", content);
        Assert.Contains("Test Game (USA) (Disc 2).chd", content);
    }
    
    [Fact]
    public void ApplyOperation_UpdatePlaylist_CreatesBackup()
    {
        // Arrange
        var playlistPath = Path.Combine(_testRoot, "Test Game (USA).m3u");
        File.WriteAllText(playlistPath, "Old content");
        
        var operation = new PsxPlaylistOperation
        {
            PlaylistPath = playlistPath,
            Title = "Test Game",
            Region = "USA",
            DiscFilenames = new List<string> { "Test Game (USA) (Disc 1).chd" },
            OperationType = PlaylistOperationType.Update,
            ExistingContent = "Old content"
        };
        
        // Act
        _planner.ApplyOperation(operation, createBackup: true);
        
        // Assert
        var backupPath = playlistPath + ".bak";
        Assert.True(File.Exists(backupPath));
        Assert.Equal("Old content", File.ReadAllText(backupPath));
        Assert.NotEqual("Old content", File.ReadAllText(playlistPath));
    }
    
    [Fact]
    public void PlanPlaylists_SkipsAudioTrackBins()
    {
        // Arrange - Multi-track disc with audio tracks
        File.WriteAllText(Path.Combine(_testRoot, "Lomax (USA) (Track 01).bin"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Lomax (USA) (Track 02).bin"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Lomax (USA) (Track 03).bin"), "");
        File.WriteAllText(Path.Combine(_testRoot, "Lomax (USA).cue"), "");
        
        // Act
        var operations = _planner.PlanPlaylists(_testRoot, recursive: false);
        
        // Assert - Should not create playlist for single-disc multi-track layout
        Assert.Empty(operations);
    }
}
