using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Verso.Python.Host;

/// <summary>
/// Owns the loopback listener the Python subprocess connects back to, authenticates the single
/// expected connection with a per-spawn token, and frames protocol messages over it.
/// </summary>
internal sealed class PythonHostConnection : IAsyncDisposable
{
    /// <summary>
    /// How long one connection has to present its handshake before it is dropped and the next is
    /// taken. Generous for a subprocess that sends it immediately on connecting, and short enough
    /// that something else holding the socket open costs the startup only this much.
    /// </summary>
    private static readonly TimeSpan GreetingTimeout = TimeSpan.FromSeconds(5);

    private readonly TcpListener _listener;
    private readonly byte[] _tokenBytes;
    private readonly RequestCorrelator _correlator = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private NdjsonFramer? _framer;
    private CancellationTokenSource? _pumpCancellation;
    private Task? _pump;
    private bool _authenticated;
    private bool _disposed;

    public PythonHostConnection()
    {
        _tokenBytes = RandomNumberGenerator.GetBytes(32); // 256-bit
        Token = Convert.ToHexString(_tokenBytes);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(backlog: 1);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    /// <summary>The ephemeral loopback port the subprocess should connect to.</summary>
    public int Port { get; }

    /// <summary>The handshake token the subprocess must present, passed to it out of band.</summary>
    public string Token { get; }

    /// <summary>
    /// Accept connections until one completes the handshake, then stop listening. Only a connection
    /// presenting the correct token is kept.
    /// <para>
    /// The port is on the loopback interface, so any process on the machine can reach it. One that
    /// arrives ahead of the subprocess is dropped and the listener keeps waiting, because refusing
    /// the whole session over it would hand any local process the ability to stop Python from
    /// starting simply by connecting first.
    /// </para>
    /// <para>
    /// Each connection gets its own short window to identify itself, so one that says nothing at
    /// all is turned away rather than holding the queue behind it for the whole startup budget.
    /// The subprocess sends its handshake as soon as it connects, so the window only ever expires
    /// on something that is not it.
    /// </para>
    /// <para>
    /// Throws <see cref="PythonHostAuthenticationException"/> when the caller gives up and
    /// something had been turned away, since that is the more useful account of what happened than
    /// the bare cancellation.
    /// </para>
    /// </summary>
    public async Task<HostHandshake> AcceptAndAuthenticateAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_client is not null)
            throw new InvalidOperationException("A connection has already been accepted.");

        string? lastRejection = null;

        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lastRejection is not null)
            {
                throw new PythonHostAuthenticationException(lastRejection);
            }

            using var greeting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            greeting.CancelAfter(GreetingTimeout);

            try
            {
                var handshake = await AuthenticateAsync(client, greeting.Token).ConfigureAwait(false);

                _listener.Stop();
                _client = client;
                _authenticated = true;
                return handshake;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // This one connected and then said nothing. Move on to the next.
                lastRejection = "A connection to the host port did not send a handshake.";
                _stream = null;
                _framer = null;
                try { client.Dispose(); } catch { /* best effort */ }
            }
            catch (PythonHostAuthenticationException ex)
            {
                // Not ours. Drop it and keep listening for the one that is.
                lastRejection = ex.Message;
                _stream = null;
                _framer = null;
                try { client.Dispose(); } catch { /* best effort */ }
            }
            catch (OperationCanceledException) when (lastRejection is not null)
            {
                try { client.Dispose(); } catch { /* best effort */ }
                throw new PythonHostAuthenticationException(lastRejection);
            }
            catch
            {
                try { client.Dispose(); } catch { /* best effort */ }
                throw;
            }
        }
    }

    /// <summary>
    /// Read one connection's handshake and decide whether it is the subprocess we started. Leaves
    /// the framer in place on success, which is what the rest of the session reads through.
    /// </summary>
    private async Task<HostHandshake> AuthenticateAsync(TcpClient client, CancellationToken cancellationToken)
    {
        client.NoDelay = true;
        _stream = client.GetStream();
        _framer = new NdjsonFramer(_stream);

        byte[]? frame;
        try
        {
            frame = await _framer.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or PythonHostProtocolException)
        {
            // A peer that sends nothing legible, or drops mid-frame, says the same thing a wrong
            // token does: this is not the subprocess.
            throw new PythonHostAuthenticationException(
                "A connection to the host port sent no readable handshake.");
        }

        if (frame is null)
            throw new PythonHostAuthenticationException(
                "The subprocess closed the connection before sending a handshake.");

        JsonObject message;
        try
        {
            message = HostProtocol.Deserialize(frame);
        }
        catch (PythonHostProtocolException)
        {
            throw new PythonHostAuthenticationException(
                "A connection to the host port sent a handshake that could not be read.");
        }

        if (HostProtocol.GetMessageType(message) != HostProtocol.Hello)
            throw new PythonHostAuthenticationException(
                "The subprocess did not begin with a valid handshake message.");

        if (!TokenMatches(HostProtocol.TryGetString(message, HostProtocol.TokenField)))
            throw new PythonHostAuthenticationException(
                "The subprocess presented an incorrect or missing handshake token.");

        return HostHandshake.FromHello(message);
    }

    /// <summary>
    /// Raised for every message that is not a reply to a pending request: the <c>stream</c>,
    /// <c>display</c>, and <c>input_request</c> events. Handlers must not block the pump.
    /// </summary>
    public event Action<JsonObject>? EventReceived;

    /// <summary>
    /// Raised once when the pump stops, carrying the fault that ended it or <c>null</c> for a
    /// clean end of stream. Pending requests have already been failed when this runs.
    /// </summary>
    public event Action<Exception?>? Closed;

    /// <summary>
    /// Begin reading messages continuously, completing pending requests and raising
    /// <see cref="EventReceived"/> for everything else. Call once, after the handshake.
    /// </summary>
    public void StartPump()
    {
        EnsureReady();
        if (_pump is not null)
            throw new InvalidOperationException("The read pump has already been started.");

        _pumpCancellation = new CancellationTokenSource();
        _pump = Task.Run(() => PumpAsync(_pumpCancellation.Token));
    }

    /// <summary>Send a message to the subprocess.</summary>
    public async Task SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var framer = EnsureReady();
        var payload = HostProtocol.Serialize(message);

        // Frames must not interleave, and an execute can race an input reply from another thread.
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await framer.WriteFrameAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Send a request and await its reply. The request id is assigned here and the pending
    /// registration is made before the send, so a fast reply cannot be missed.
    /// </summary>
    public async Task<JsonObject> SendRequestAsync(JsonObject message, CancellationToken cancellationToken)
    {
        EnsureReady();

        var id = _correlator.NextId();
        message[HostProtocol.IdField] = id;

        var reply = _correlator.RegisterAsync(id, cancellationToken);
        try
        {
            await SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _correlator.TryAbandon(id); // nothing will ever reply to a request that never left
            throw;
        }

        return await reply.ConfigureAwait(false);
    }

    /// <summary>Read the next message from the subprocess, or null at end of stream.</summary>
    public async Task<JsonObject?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var framer = EnsureReady();
        var frame = await framer.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        return frame is null ? null : HostProtocol.Deserialize(frame);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_pumpCancellation is not null)
        {
            try { _pumpCancellation.Cancel(); } catch { /* best effort */ }
        }

        try { _stream?.Dispose(); } catch { /* best effort */ }
        try { _client?.Dispose(); } catch { /* best effort */ }
        try { _listener.Stop(); } catch { /* best effort */ }

        if (_pump is not null)
        {
            try { await _pump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch { /* the pump is torn down with the socket regardless */ }
        }

        _pumpCancellation?.Dispose();
        _sendLock.Dispose();
        CryptographicOperations.ZeroMemory(_tokenBytes);
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        Exception? fault = null;
        try
        {
            while (true)
            {
                var message = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                    break; // the subprocess closed the connection cleanly

                Dispatch(message);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down; not a fault.
        }
        catch (Exception ex)
        {
            fault = ex;
        }
        finally
        {
            _correlator.FailAll(fault ?? new PythonHostException(
                "The Python host closed the connection before replying."));

            // A disposal tears the socket out from under the pump, which surfaces as a fault
            // here. That is an expected shutdown, not a crash, so it is not announced.
            if (!_disposed)
            {
                try { Closed?.Invoke(fault); } catch { /* a subscriber must not break teardown */ }
            }
        }
    }

    private void Dispatch(JsonObject message)
    {
        var messageType = HostProtocol.GetMessageType(message);

        if (HostProtocol.IsReply(messageType)
            && HostProtocol.TryGetInt(message, HostProtocol.ReqIdField) is int requestId
            && _correlator.TryComplete(requestId, message))
        {
            return;
        }

        // A handler that throws must not take the pump down with it: the remaining events,
        // and the terminal reply the caller is waiting on, still have to arrive.
        try { EventReceived?.Invoke(message); } catch { /* reported through the execute result */ }
    }

    private NdjsonFramer EnsureReady()
    {
        ThrowIfDisposed();
        if (!_authenticated || _framer is null)
            throw new InvalidOperationException("The connection has not completed its handshake.");
        return _framer;
    }

    private bool TokenMatches(string? presented)
    {
        if (presented is null) return false;
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var expectedBytes = Encoding.UTF8.GetBytes(Token);
        return CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PythonHostConnection));
    }
}

/// <summary>The interpreter details reported by the subprocess in its handshake.</summary>
internal sealed record HostHandshake(
    int Protocol,
    string PythonVersion,
    string PythonExecutable,
    string Implementation,
    IReadOnlyList<string> Capabilities)
{
    public static HostHandshake FromHello(JsonObject hello)
    {
        var protocol = HostProtocol.TryGetInt(hello, HostProtocol.ProtocolField) ?? 0;
        var python = hello[HostProtocol.PythonField] as JsonObject;

        var capabilities = new List<string>();
        if (hello[HostProtocol.CapabilitiesField] is JsonArray caps)
        {
            foreach (var entry in caps)
            {
                if (entry is JsonValue value && value.TryGetValue<string>(out var text))
                    capabilities.Add(text);
            }
        }

        return new HostHandshake(
            protocol,
            python is null ? "" : HostProtocol.TryGetString(python, "version") ?? "",
            python is null ? "" : HostProtocol.TryGetString(python, "executable") ?? "",
            python is null ? "" : HostProtocol.TryGetString(python, "implementation") ?? "",
            capabilities);
    }
}
