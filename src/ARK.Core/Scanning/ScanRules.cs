namespace ARK.Core.Scanning;

/// <summary>
/// Tunable scan policy, loaded from <c>config/scan/scan-rules.json</c>.
/// </summary>
/// <remarks>
/// Exclusion rules are data, not code: one user's junk is not the next user's junk. The two
/// thresholds are here for the same reason, though the shipped values are the ones validated
/// against the reference drive and moving them is not a casual edit.
/// </remarks>
public sealed class ScanRules
{
    /// <summary>Share of files carrying the most common extension required to admit a directory.</summary>
    public double ExtensionHomogeneityThreshold { get; set; } = 0.90;

    /// <summary>Share of files carrying a recognized region token required to admit a directory.</summary>
    public double NamingConformanceThreshold { get; set; } = 0.80;

    /// <summary>Smallest directory worth profiling; below this the signals are noise.</summary>
    public int MinimumFileCount { get; set; } = 5;

    /// <summary>
    /// Extensions written by torrent and browser clients for a transfer still in flight.
    /// A signal, never proof — see <see cref="ScanReport"/>.
    /// </summary>
    public string[] IncompleteDownloadExtensions { get; set; } =
    {
        ".!qb", ".part", ".!ut", ".bc!", ".crdownload", ".aria2",
    };

    /// <summary>Directory names excluded outright, case-insensitive, before any signal is computed.</summary>
    public string[] ExcludedDirectoryNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Extensions of a disc image's track sheet. An archive holding one of these plus its data
    /// files is a disc image — correct structure that the stubbed disc resolver does not handle —
    /// rather than an archive that wrongly contains two games.
    /// </summary>
    public string[] DiscDescriptorExtensions { get; set; } = { ".cue", ".gdi", ".ccd", ".toc", ".m3u" };

    /// <summary>
    /// Directories that pass both signals but are known not to contain games.
    /// <c>Nintendo - Wii U - Disc Keys</c> is 516 flawlessly No-Intro-named archives holding disc
    /// keys. Conformant naming does not prove game content; these are admitted but flagged so the
    /// gap is visible rather than silent. A third signal is future work.
    /// </summary>
    public string[] KnownFalsePositiveDirectories { get; set; } = Array.Empty<string>();
}
