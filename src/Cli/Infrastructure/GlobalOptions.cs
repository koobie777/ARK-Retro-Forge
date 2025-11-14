namespace ARK.Cli.Infrastructure;

/// <summary>
/// Global options available for all commands
/// </summary>
public class GlobalOptions
{
    public bool DryRun { get; set; } = true;
    public bool Apply { get; set; }
    public bool Force { get; set; }
    public int Workers { get; set; } = Environment.ProcessorCount;
    public bool Verbose { get; set; }
    public string? ReportDirectory { get; set; }
    public string Theme { get; set; } = "dark";
}
