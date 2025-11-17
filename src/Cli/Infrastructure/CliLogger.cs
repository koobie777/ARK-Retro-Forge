using System.Collections.Concurrent;
using System.Text;

namespace ARK.Cli.Infrastructure;

internal static class CliLogger
{
    private static readonly object SyncRoot = new();
    private static string? _logFile;
    private static readonly ConcurrentQueue<string> Buffer = new();

    public static void Initialize()
    {
        var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logsDirectory);

        _logFile = Path.Combine(logsDirectory, $"ark-cli-{DateTime.UtcNow:yyyyMMdd}.log");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogError("Unhandled exception", ex);
            }
            Flush();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError("Unobserved task exception", args.Exception);
            args.SetObserved();
            Flush();
        };

        LogInfo("CLI logger initialized");
    }

    public static void LogInfo(string message)
        => Enqueue("INFO", message);

    public static void LogOperation(string component, string message)
        => Enqueue(component.ToUpperInvariant(), message);

    public static void LogError(string message, Exception? exception = null)
    {
        var builder = new StringBuilder(message);
        if (exception != null)
        {
            builder.AppendLine().Append(exception);
        }

        Enqueue("ERROR", builder.ToString());
    }

    private static void Enqueue(string level, string message)
    {
        var stamp = $"{DateTime.UtcNow:O} [{level}] {message}";
        Buffer.Enqueue(stamp);
        Flush();
    }

    private static void Flush()
    {
        if (_logFile == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (Buffer.IsEmpty)
            {
                return;
            }

            using var stream = new FileStream(_logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream);
            while (Buffer.TryDequeue(out var entry))
            {
                writer.WriteLine(entry);
            }
        }
    }
}
