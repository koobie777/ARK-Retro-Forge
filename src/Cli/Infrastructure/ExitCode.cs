namespace ARK.Cli.Infrastructure;

/// <summary>
/// Standardized exit codes for the application
/// </summary>
public enum ExitCode
{
    OK = 0,
    GeneralError = 1,
    InvalidArgs = 2,
    PreflightFail = 3,
    ToolMissing = 4,
    HashMismatch = 5,
    UserCancelled = 6,
    IOError = 7,
    PluginError = 8
}
