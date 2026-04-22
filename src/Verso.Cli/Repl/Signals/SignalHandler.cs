using System.Runtime.InteropServices;

namespace Verso.Cli.Repl.Signals;

/// <summary>
/// Translates terminal signals into <see cref="CancellationToken"/> state for the REPL loop.
/// A single Ctrl+C cancels the in-flight cell; a second Ctrl+C within the debounce window
/// forces a hard exit. SIGTERM always causes a graceful shutdown with code 143.
/// </summary>
public sealed class SignalHandler : IDisposable
{
    private readonly CancellationTokenSource _linkedCts;
    private readonly ConsoleCancelEventHandler _cancelHandler;
    private readonly PosixSignalRegistration? _sigtermReg;
    private DateTime _lastCtrlC = DateTime.MinValue;

    /// <summary>Debounce window for the "second Ctrl+C kills the process" behaviour.</summary>
    public TimeSpan HardExitWindow { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Exit code to set when SIGTERM fires. Observed by the caller when
    /// <see cref="ExitRequested"/> becomes non-null.
    /// </summary>
    public int? ExitRequested { get; private set; }

    public CancellationToken Token => _linkedCts.Token;

    public SignalHandler(CancellationToken outer)
    {
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(outer);

        _cancelHandler = OnCancelKeyPress;
        Console.CancelKeyPress += _cancelHandler;

        try
        {
            _sigtermReg = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSigTerm);
        }
        catch
        {
            // Platforms without POSIX signal support fall back to Ctrl+C only.
            _sigtermReg = null;
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        var now = DateTime.UtcNow;
        if (now - _lastCtrlC <= HardExitWindow)
        {
            // User insisted — leave with 130 so shells report the signal correctly.
            ExitRequested = Utilities.ExitCodes.SigInt;
            _linkedCts.Cancel();
            return;
        }
        _lastCtrlC = now;
        _linkedCts.Cancel();
    }

    private void OnSigTerm(PosixSignalContext context)
    {
        context.Cancel = true;
        ExitRequested = Utilities.ExitCodes.SigTerm;
        _linkedCts.Cancel();
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= _cancelHandler;
        _sigtermReg?.Dispose();
        _linkedCts.Dispose();
    }
}
