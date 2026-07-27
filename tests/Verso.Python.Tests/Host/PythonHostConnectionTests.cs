using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Verso.Python.Host;

namespace Verso.Python.Tests.Host;

[TestClass]
public sealed class PythonHostConnectionTests
{
    /// <summary>
    /// How long a test that expects a rejection waits before giving up on the accept. Short,
    /// because the connection under test has already been turned away by the time it starts.
    /// </summary>
    private static readonly TimeSpan RejectionDeadline = TimeSpan.FromSeconds(2);

    [TestMethod]
    public async Task PortAndToken_AreInitialized()
    {
        await using var connection = new PythonHostConnection();

        Assert.IsTrue(connection.Port > 0);
        Assert.AreEqual(64, connection.Token.Length); // 32 random bytes rendered as hex
        StringAssert.Matches(connection.Token, new Regex("^[0-9A-F]+$"));
    }

    [TestMethod]
    public async Task Authenticates_WithCorrectToken()
    {
        await using var connection = new PythonHostConnection();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var acceptTask = connection.AcceptAndAuthenticateAsync(cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, connection.Port);
        await SendLineAsync(client.GetStream(), Hello(connection.Token));

        var handshake = await acceptTask;
        Assert.AreEqual(1, handshake.Protocol);
        Assert.AreEqual("3.13.3", handshake.PythonVersion);
        Assert.AreEqual("CPython", handshake.Implementation);
        Assert.AreEqual("/usr/bin/python3", handshake.PythonExecutable);
    }

    // A rejected connection no longer ends the wait: the listener keeps going, because the port is
    // reachable by anything on the machine and giving up would let any local process stop Python
    // from starting. These therefore give the accept a deadline and check that what it reports when
    // that deadline passes is the rejection, not a bare timeout.

    [TestMethod]
    public async Task Rejects_WrongToken()
    {
        await using var connection = new PythonHostConnection();
        using var cts = new CancellationTokenSource(RejectionDeadline);

        var acceptTask = connection.AcceptAndAuthenticateAsync(cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, connection.Port);
        await SendLineAsync(client.GetStream(), Hello("not-the-right-token"));

        var ex = await Assert.ThrowsExceptionAsync<PythonHostAuthenticationException>(async () => await acceptTask);
        StringAssert.Contains(ex.Message, "token");
    }

    [TestMethod]
    public async Task Rejects_MissingToken()
    {
        await using var connection = new PythonHostConnection();
        using var cts = new CancellationTokenSource(RejectionDeadline);

        var acceptTask = connection.AcceptAndAuthenticateAsync(cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, connection.Port);
        await SendLineAsync(client.GetStream(), Hello(token: null));

        await Assert.ThrowsExceptionAsync<PythonHostAuthenticationException>(async () => await acceptTask);
    }

    [TestMethod]
    public async Task Rejects_NonHelloFirstMessage()
    {
        await using var connection = new PythonHostConnection();
        using var cts = new CancellationTokenSource(RejectionDeadline);

        var acceptTask = connection.AcceptAndAuthenticateAsync(cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, connection.Port);
        await SendLineAsync(client.GetStream(), new JsonObject { ["type"] = "shutdown" });

        await Assert.ThrowsExceptionAsync<PythonHostAuthenticationException>(async () => await acceptTask);
    }

    [TestMethod]
    public async Task Rejects_UnreadableHandshake()
    {
        await using var connection = new PythonHostConnection();
        using var cts = new CancellationTokenSource(RejectionDeadline);

        var acceptTask = connection.AcceptAndAuthenticateAsync(cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, connection.Port);

        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes("this is not json\n"));
        await stream.FlushAsync();

        // Garbage must be turned away like any other stranger, rather than faulting the listener
        // and taking the session with it.
        await Assert.ThrowsExceptionAsync<PythonHostAuthenticationException>(async () => await acceptTask);
    }

    [TestMethod]
    public async Task KeepsListening_AfterAConnectionThatSaysNothing()
    {
        await using var connection = new PythonHostConnection();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var acceptTask = connection.AcceptAndAuthenticateAsync(cts.Token);

        // Holds the socket open and never speaks. Without a window of its own this would keep the
        // subprocess queued behind it for the whole startup budget.
        using var silent = new TcpClient();
        await silent.ConnectAsync(IPAddress.Loopback, connection.Port);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, connection.Port);
        await SendLineAsync(client.GetStream(), Hello(connection.Token));

        var handshake = await acceptTask;
        Assert.AreEqual("3.13.3", handshake.PythonVersion);
    }

    [TestMethod]
    public async Task KeepsListening_AfterAStrangerIsTurnedAway()
    {
        await using var connection = new PythonHostConnection();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var acceptTask = connection.AcceptAndAuthenticateAsync(cts.Token);

        // Something else on the machine reaches the port first and is refused.
        using (var stranger = new TcpClient())
        {
            await stranger.ConnectAsync(IPAddress.Loopback, connection.Port);
            await SendLineAsync(stranger.GetStream(), Hello("not-the-right-token"));
        }

        // The subprocess we actually started then arrives and is accepted.
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, connection.Port);
        await SendLineAsync(client.GetStream(), Hello(connection.Token));

        var handshake = await acceptTask;
        Assert.AreEqual(1, handshake.Protocol);
        Assert.AreEqual("3.13.3", handshake.PythonVersion);
    }

    private static async Task SendLineAsync(NetworkStream stream, JsonObject message)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await stream.WriteAsync(bytes);
        await stream.WriteAsync(new byte[] { (byte)'\n' });
        await stream.FlushAsync();
    }

    private static JsonObject Hello(string? token)
    {
        var hello = new JsonObject
        {
            ["type"] = "hello",
            ["protocol"] = 1,
            ["python"] = new JsonObject
            {
                ["version"] = "3.13.3",
                ["executable"] = "/usr/bin/python3",
                ["implementation"] = "CPython",
            },
            ["capabilities"] = new JsonArray(),
        };
        if (token is not null)
            hello["token"] = token;
        return hello;
    }
}
