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
    private readonly TcpListener _listener;
    private readonly byte[] _tokenBytes;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private NdjsonFramer? _framer;
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
    /// Accept exactly one connection, then stop listening, and validate its handshake. Only a
    /// connection presenting the correct token is accepted; anything else throws
    /// <see cref="PythonHostAuthenticationException"/>.
    /// </summary>
    public async Task<HostHandshake> AcceptAndAuthenticateAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_client is not null)
            throw new InvalidOperationException("A connection has already been accepted.");

        _client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        _listener.Stop();
        _client.NoDelay = true;
        _stream = _client.GetStream();
        _framer = new NdjsonFramer(_stream);

        var frame = await _framer.ReadFrameAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new PythonHostAuthenticationException(
                "The subprocess closed the connection before sending a handshake.");

        var message = HostProtocol.Deserialize(frame);
        if (HostProtocol.GetMessageType(message) != HostProtocol.Hello)
            throw new PythonHostAuthenticationException(
                "The subprocess did not begin with a valid handshake message.");

        if (!TokenMatches(HostProtocol.TryGetString(message, HostProtocol.TokenField)))
            throw new PythonHostAuthenticationException(
                "The subprocess presented an incorrect or missing handshake token.");

        _authenticated = true;
        return HostHandshake.FromHello(message);
    }

    /// <summary>Send a message to the subprocess.</summary>
    public Task SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var framer = EnsureReady();
        return framer.WriteFrameAsync(HostProtocol.Serialize(message), cancellationToken);
    }

    /// <summary>Read the next message from the subprocess, or null at end of stream.</summary>
    public async Task<JsonObject?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var framer = EnsureReady();
        var frame = await framer.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        return frame is null ? null : HostProtocol.Deserialize(frame);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        try { _stream?.Dispose(); } catch { /* best effort */ }
        try { _client?.Dispose(); } catch { /* best effort */ }
        try { _listener.Stop(); } catch { /* best effort */ }
        CryptographicOperations.ZeroMemory(_tokenBytes);

        return ValueTask.CompletedTask;
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
