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

    [TestMethod]
    public async Task Rejects_WrongToken()
    {
        await using var connection = new PythonHostConnection();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var acceptTask = connection.AcceptAndAuthenticateAsync(cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, connection.Port);
        await SendLineAsync(client.GetStream(), new JsonObject { ["type"] = "shutdown" });

        await Assert.ThrowsExceptionAsync<PythonHostAuthenticationException>(async () => await acceptTask);
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
