using Spectre.Console;

namespace ARK.Cli.Infrastructure;

internal sealed class OperationContextScope : IDisposable
{
    private static readonly AsyncLocal<OperationContextScope?> Current = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly ConsoleCancelEventHandler _handler;
    private readonly Task? _escapeWatcher;
    private readonly OperationContextScope? _previous;

    private OperationContextScope(string label, bool watchForEscape)
    {
        Label = label;
        _previous = Current.Value;
        Current.Value = this;

        _handler = (_, args) =>
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            args.Cancel = true;
            AnsiConsole.MarkupLine($"\n[yellow]{Label} cancelled (Ctrl+C).[/]");
            _cts.Cancel();
        };

        Console.CancelKeyPress += _handler;
        _escapeWatcher = watchForEscape ? CancellationMonitor.WatchForEscapeAsync(_cts, label) : null;
    }

    public string Label { get; }

    public static OperationContextScope Begin(string label, bool watchForEscape = false)
        => new(label, watchForEscape);

    public static CancellationToken CurrentToken => Current.Value?._cts.Token ?? CancellationToken.None;

    public static void ThrowIfCancellationRequested()
        => CurrentToken.ThrowIfCancellationRequested();

    public void Dispose()
    {
        Console.CancelKeyPress -= _handler;
        _cts.Cancel();
        if (_escapeWatcher != null)
        {
            try
            {
                _escapeWatcher.Wait(100);
            }
            catch
            {
                // ignored
            }
        }

        _cts.Dispose();
        Current.Value = _previous;
    }
}
