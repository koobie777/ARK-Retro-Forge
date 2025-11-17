namespace ARK.Cli.Infrastructure;

internal static class CancellationMonitor
{
    public static Task? WatchForEscapeAsync(CancellationTokenSource cts)
    {
        if (Console.IsInputRedirected)
        {
            return null;
        }

        return Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(100);
                    continue;
                }

                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Escape)
                {
                    cts.Cancel();
                    break;
                }
            }
        });
    }
}
