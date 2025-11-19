namespace ARK.Core.Systems.PSX;

/// <summary>
/// Represents metadata for a PSX disc, including multi-disc information and classification
/// </summary>
public record PsxDiscInfo
{
    /// <summary>
    /// Original file path
    /// </summary>
    public required string FilePath { get; init; }
    
    /// <summary>
    /// Game title (e.g., "Alone in the Dark - The New Nightmare")
    /// </summary>
    public string? Title { get; init; }
    
    /// <summary>
    /// Region code (e.g., "USA", "Europe", "Japan")
    /// </summary>
    public string? Region { get; init; }
    
    /// <summary>
    /// PSX serial number (e.g., "SLUS-01201")
    /// </summary>
    public string? Serial { get; init; }
    
    /// <summary>
    /// Disc number (1-based) for multi-disc titles, or null for single-disc
    /// </summary>
    public int? DiscNumber { get; init; }
    
    /// <summary>
    /// Total number of discs for this title, or null if unknown/single-disc
    /// </summary>
    public int? DiscCount { get; init; }
    
    /// <summary>
    /// Content classification
    /// </summary>
    public PsxContentType ContentType { get; init; } = PsxContentType.Mainline;
    
    /// <summary>
    /// File extension (e.g., ".bin", ".cue", ".chd")
    /// </summary>
    public string? Extension { get; init; }
    
    /// <summary>
    /// Whether this is part of a multi-disc set
    /// </summary>
    public bool IsMultiDisc => DiscCount.HasValue && DiscCount.Value > 1;
    
    /// <summary>
    /// Whether a standard PSX serial was found
    /// </summary>
    public bool HasSerial => !string.IsNullOrWhiteSpace(Serial);
    
    /// <summary>
    /// Diagnostic message for missing or non-standard data
    /// </summary>
    public string? Warning { get; init; }
    
    /// <summary>
    /// Track number for multi-track discs (1-based), or null for single-track
    /// </summary>
    public int? TrackNumber { get; init; }
    
    /// <summary>
    /// Total number of tracks for this disc, or null if unknown/single-track
    /// </summary>
    public int? TrackCount { get; init; }
    
    /// <summary>
    /// Whether this is an audio track (not the main data track)
    /// </summary>
    public bool IsAudioTrack { get; init; }
    
    /// <summary>
    /// Whether this disc has multiple tracks (multi-BIN layout)
    /// </summary>
    public bool IsMultiTrack => TrackNumber.HasValue;
    
    /// <summary>
    /// Version or revision string (e.g., "Rev 1", "v1.1"), or null if unknown
    /// </summary>
    public string? Version { get; init; }
    
    /// <summary>
    /// Referenced CUE file path for this BIN (if applicable)
    /// </summary>
    public string? CueFilePath { get; init; }

    /// <summary>
    /// Potential DAT matches when serial detection fails.
    /// </summary>
    public IReadOnlyList<PsxSerialCandidate> SerialCandidates { get; init; } = Array.Empty<PsxSerialCandidate>();
}

/// <summary>
/// Represents a candidate DAT match for serial recovery.
/// </summary>
public record PsxSerialCandidate(string Title, string? Region, string? Serial, int? DiscCount);
