using System.Diagnostics;

namespace ARK.Cli.Infrastructure.Progress;

internal struct ArchiveProgressState
{
    public const string StateKey = "cli.archive.progress";

    private readonly Stopwatch _stopwatch;

    public ArchiveProgressState(int total)
    {
        Total = total;
        CurrentArchive = string.Empty;
        CurrentIndex = 0;
        Completed = 0;
        Failed = 0;
        CancelRequested = false;
        _stopwatch = Stopwatch.StartNew();
    }

    public string CurrentArchive { get; set; }

    public int CurrentIndex { get; set; }

    public int Total { get; }

    public int Completed { get; set; }

    public int Failed { get; set; }

    public bool CancelRequested { get; set; }

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public int Remaining => Math.Max(Total - Completed - Failed, 0);
}
