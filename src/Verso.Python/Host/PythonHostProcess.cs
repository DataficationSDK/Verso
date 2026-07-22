using System.Text;
using System.Text.Json.Nodes;

namespace Verso.Python.Host;

/// <summary>
/// Supervises a Python host subprocess: spawns the interpreter running the host script, completes
/// the loopback handshake, captures raw output, and provides shutdown, restart, and crash detection.
/// </summary>
internal sealed class PythonHostProcess : IAsyncDisposable
{
    private static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(2);
    private const int StdErrTailLimit = 8192;

    private readonly string _pythonExecutable;
    private readonly string _workingDirectory;
    private readonly TimeSpan _handshakeTimeout;
    private readonly string? _scriptPathOverride;

    private readonly object _stdErrTailLock = new();
    private readonly StringBuilder _stdErrTail = new();

    private PythonHostConnection? _connection;
    private IHostProcessHandle? _process;
    private HostHandshake? _handshake;
    private Task? _stdoutPump;
    private Task? _stderrPump;
    private bool _disposed;

    /// <param name="pythonExecutable">Interpreter to run (a bare name is resolved through PATH).</param>
    /// <param name="workingDirectory">Working directory for the subprocess (the notebook's directory).</param>
    /// <param name="handshakeTimeout">Handshake deadline; defaults to 30 seconds.</param>
    /// <param name="scriptPathOverride">Host script path to run instead of the extracted script; for tests.</param>
    public PythonHostProcess(
        string pythonExecutable,
        string workingDirectory,
        TimeSpan? handshakeTimeout = null,
        string? scriptPathOverride = null)
    {
        _pythonExecutable = pythonExecutable ?? throw new ArgumentNullException(nameof(pythonExecutable));
        _workingDirectory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;
        _handshakeTimeout = handshakeTimeout ?? DefaultHandshakeTimeout;
        _scriptPathOverride = scriptPathOverride;
    }

    public bool IsAlive => _connection is not null && _process is { HasExited: false };
    public int ProcessId => _process?.Id ?? -1;
    public HostHandshake? Handshake => _handshake;

    /// <summary>The most recent bounded tail of the subprocess's raw standard error.</summary>
    public string StandardErrorTail
    {
        get { lock (_stdErrTailLock) return _stdErrTail.ToString(); }
    }

    /// <summary>Spawn the subprocess, complete the handshake, and release it into its message loop.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_connection is not null)
            throw new InvalidOperationException("The host process has already been started.");

        var scriptPath = _scriptPathOverride ?? HostScriptExtractor.EnsureExtracted();
        var connection = new PythonHostConnection();

        var startInfo = new HostProcessStartInfo(
            _pythonExecutable,
            new[] { "-X", "utf8", "-u", scriptPath, "--connect", $"127.0.0.1:{connection.Port}" },
            _workingDirectory,
            new Dictionary<string, string> { ["VERSO_PYHOST_TOKEN"] = connection.Token });

        IHostProcessHandle process;
        try
        {
            process = HostProcessLauncher.Launch(startInfo);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _connection = connection;
        _process = process;
        StartOutputPumps(process);

        try
        {
            _handshake = await CompleteHandshakeAsync(connection, process, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TeardownAsync(kill: true).ConfigureAwait(false);
            throw;
        }

        // Bootstrap configuration releases the subprocess from its handshake into the message loop.
        await connection.SendAsync(BuildHelloOk(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ask the subprocess to exit, waiting a short grace period before killing it.</summary>
    public async Task ShutdownAsync()
    {
        if (_process is null) return;

        if (!_process.HasExited && _connection is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(ShutdownGracePeriod);
                await _connection.SendAsync(
                    new JsonObject { [HostProtocol.TypeField] = HostProtocol.Shutdown }, cts.Token).ConfigureAwait(false);
            }
            catch { /* the process may already be gone */ }

            try
            {
                await _process.WaitForExitAsync(CancellationToken.None).WaitAsync(ShutdownGracePeriod).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _process.Kill();
            }
        }

        await TeardownAsync(kill: false).ConfigureAwait(false);
    }

    /// <summary>Kill the current subprocess and start a fresh one.</summary>
    public async Task RespawnAsync(CancellationToken cancellationToken = default)
    {
        await TeardownAsync(kill: true).ConfigureAwait(false);
        ClearStandardErrorTail();
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Terminate the subprocess immediately.</summary>
    public void Kill() => _process?.Kill();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { await ShutdownAsync().ConfigureAwait(false); }
        catch { /* fall through to a forced teardown */ }

        if (_process is not null || _connection is not null)
            await TeardownAsync(kill: true).ConfigureAwait(false);
    }

    private async Task<HostHandshake> CompleteHandshakeAsync(
        PythonHostConnection connection, IHostProcessHandle process, CancellationToken cancellationToken)
    {
        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeCts.CancelAfter(_handshakeTimeout);

        var acceptTask = connection.AcceptAndAuthenticateAsync(handshakeCts.Token);
        var exitTask = process.WaitForExitAsync(CancellationToken.None);

        // Fail fast when the subprocess dies before completing the handshake, rather than waiting
        // out the full timeout for a connection that will never arrive.
        var winner = await Task.WhenAny(acceptTask, exitTask).ConfigureAwait(false);
        if (winner == exitTask && !acceptTask.IsCompleted)
        {
            handshakeCts.Cancel(); // unblock the pending accept
            try { await acceptTask.ConfigureAwait(false); } catch { /* observe the cancellation */ }
            var tail = await CollectStderrAsync().ConfigureAwait(false);
            throw new PythonHostException(
                $"The Python host process exited during startup (exit code {SafeExitCode()}).{FormatTail(tail)}");
        }

        try
        {
            return await acceptTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var tail = await CollectStderrAsync().ConfigureAwait(false);
            throw new PythonHostTimeoutException(
                $"The Python host did not complete its handshake within {_handshakeTimeout.TotalSeconds:0} seconds.{FormatTail(tail)}");
        }
    }

    private void StartOutputPumps(IHostProcessHandle process)
    {
        _stdoutPump = Task.Run(() => DrainAsync(process.StandardOutput, isError: false));
        _stderrPump = Task.Run(() => DrainAsync(process.StandardError, isError: true));
    }

    private async Task DrainAsync(TextReader reader, bool isError)
    {
        try
        {
            var buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false)) > 0)
            {
                if (isError)
                    AppendStandardErrorTail(buffer, read);
                // Raw standard output is drained to keep the pipe from filling; forwarding it as cell
                // output belongs to the execution pipeline layered on top of this supervisor.
            }
        }
        catch
        {
            // The pipe closed with the process; nothing further to drain.
        }
    }

    private void AppendStandardErrorTail(char[] buffer, int count)
    {
        lock (_stdErrTailLock)
        {
            _stdErrTail.Append(buffer, 0, count);
            if (_stdErrTail.Length > StdErrTailLimit)
                _stdErrTail.Remove(0, _stdErrTail.Length - StdErrTailLimit);
        }
    }

    private void ClearStandardErrorTail()
    {
        lock (_stdErrTailLock) _stdErrTail.Clear();
    }

    private async Task<string> CollectStderrAsync()
    {
        // Give the standard error pump a moment to flush the final bytes before reading the tail.
        await WaitForPumpsAsync(ShutdownGracePeriod).ConfigureAwait(false);
        return StandardErrorTail;
    }

    private async Task WaitForPumpsAsync(TimeSpan timeout)
    {
        var pumps = new[] { _stdoutPump, _stderrPump }.Where(p => p is not null).Cast<Task>().ToArray();
        if (pumps.Length == 0) return;
        try { await Task.WhenAll(pumps).WaitAsync(timeout).ConfigureAwait(false); }
        catch { /* best effort; the tail may be slightly short */ }
    }

    private async Task TeardownAsync(bool kill)
    {
        if (kill)
            _process?.Kill();

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        await WaitForPumpsAsync(ShutdownGracePeriod).ConfigureAwait(false);
        _stdoutPump = null;
        _stderrPump = null;

        _process?.Dispose();
        _process = null;
        _handshake = null;
    }

    private int SafeExitCode()
    {
        try { return _process is { HasExited: true } p ? p.ExitCode : -1; }
        catch { return -1; }
    }

    private static JsonObject BuildHelloOk() => new()
    {
        [HostProtocol.TypeField] = HostProtocol.HelloOk,
        [HostProtocol.ProtocolField] = HostProtocol.ProtocolVersion,
        ["config"] = new JsonObject(),
    };

    private static string FormatTail(string tail)
        => string.IsNullOrWhiteSpace(tail) ? "" : $"\n\nPython output:\n{tail.TrimEnd()}";

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PythonHostProcess));
    }
}
