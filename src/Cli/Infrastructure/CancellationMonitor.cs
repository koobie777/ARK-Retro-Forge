using Spectre.Console;

namespace ARK.Cli.Infrastructure;

internal static class CancellationMonitor
{
    public static Task? WatchForEscapeAsync(CancellationTokenSource cts, string label)
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
                if (IsCancelKey(key))
                {
                    if (!cts.IsCancellationRequested)
                    {
                        var reason = key.Key == ConsoleKey.B ? "B" : "ESC";
                        AnsiConsole.MarkupLine($"\n[yellow]{label} cancelled ({reason}).[/]");
                        cts.Cancel();
                    }
                    break;
                }
            }
        });
    }

    private static bool IsCancelKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            return true;
        }

        if (key.Key == ConsoleKey.B)
        {
            // Treat both lowercase and uppercase B as "Back" regardless of Shift.
            return (key.Modifiers & ~ConsoleModifiers.Shift) == 0;
        }

        return false;
    }
}
