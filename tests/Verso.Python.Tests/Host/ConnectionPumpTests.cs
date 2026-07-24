using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Verso.Python.Host;

namespace Verso.Python.Tests.Host;

/// <summary>
/// Covers the read pump on <see cref="PythonHostConnection"/> with a stand-in peer rather than a
/// Python subprocess: reply correlation, event dispatch, and what happens to work in flight when
/// the connection goes away.
/// </summary>
[TestClass]
public sealed class ConnectionPumpTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task SendRequest_CompletesOnTheMatchingReply()
    {
        await using var peer = await FakePeer.ConnectAsync();

        var request = new JsonObject { ["type"] = "execute", ["code"] = "1 + 1" };
        var pending = peer.Connection.SendRequestAsync(request, CancellationToken.None);

        var received = await peer.ReadAsync();
        Assert.AreEqual("execute", received["type"]!.GetValue<string>());

        var id = received["id"]!.GetValue<int>();
        await peer.SendAsync(new JsonObject
        {
            ["type"] = "execute_result",
            ["req_id"] = id,
            ["status"] = "ok",
        });

        var reply = await pending.WaitAsync(Timeout);
        Assert.AreEqual("ok", reply["status"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task SendRequest_AssignsDistinctIds()
    {
        await using var peer = await FakePeer.ConnectAsync();

        _ = peer.Connection.SendRequestAsync(new JsonObject { ["type"] = "execute" }, CancellationToken.None);
        _ = peer.Connection.SendRequestAsync(new JsonObject { ["type"] = "execute" }, CancellationToken.None);

        var first = (await peer.ReadAsync())["id"]!.GetValue<int>();
        var second = (await peer.ReadAsync())["id"]!.GetValue<int>();

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public async Task Events_AreRaisedInsteadOfCompletingTheRequest()
    {
        await using var peer = await FakePeer.ConnectAsync();

        var events = new List<JsonObject>();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.Connection.EventReceived += message =>
        {
            events.Add(message);
            if (events.Count == 2) arrived.TrySetResult();
        };

        var pending = peer.Connection.SendRequestAsync(
            new JsonObject { ["type"] = "execute" }, CancellationToken.None);
        var id = (await peer.ReadAsync())["id"]!.GetValue<int>();

        // Stream events carry the request id too, so only the terminal reply may complete it.
        await peer.SendAsync(new JsonObject
        {
            ["type"] = "stream", ["req_id"] = id, ["name"] = "stdout", ["text"] = "hello",
        });
        await peer.SendAsync(new JsonObject
        {
            ["type"] = "input_request", ["req_id"] = id, ["prompt"] = "name", ["password"] = false,
        });

        await arrived.Task.WaitAsync(Timeout);
        Assert.IsFalse(pending.IsCompleted, "events must not complete the pending request");

        Assert.AreEqual("stream", events[0]["type"]!.GetValue<string>());
        Assert.AreEqual("input_request", events[1]["type"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Disconnect_FailsRequestsStillInFlight()
    {
        await using var peer = await FakePeer.ConnectAsync();

        var pending = peer.Connection.SendRequestAsync(
            new JsonObject { ["type"] = "execute" }, CancellationToken.None);
        await peer.ReadAsync();

        peer.DropConnection();

        // Without this the caller would wait forever for a reply that can never arrive.
        await Assert.ThrowsExceptionAsync<PythonHostException>(async () => await pending.WaitAsync(Timeout));
    }

    [TestMethod]
    public async Task Disconnect_RaisesClosedOnce()
    {
        await using var peer = await FakePeer.ConnectAsync();

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        peer.Connection.Closed += _ =>
        {
            Interlocked.Increment(ref count);
            closed.TrySetResult();
        };

        peer.DropConnection();

        await closed.Task.WaitAsync(Timeout);
        Assert.AreEqual(1, count);
    }

    /// <summary>A socket peer that speaks the wire protocol in place of the Python subprocess.</summary>
    private sealed class FakePeer : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly byte[] _buffer = new byte[16384];
        private readonly List<byte> _pending = new();

        private FakePeer(PythonHostConnection connection, TcpClient client)
        {
            Connection = connection;
            _client = client;
            _stream = client.GetStream();
        }

        public PythonHostConnection Connection { get; }

        public static async Task<FakePeer> ConnectAsync()
        {
            var connection = new PythonHostConnection();
            using var cancellation = new CancellationTokenSource(Timeout);

            var accept = connection.AcceptAndAuthenticateAsync(cancellation.Token);

            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, connection.Port);

            var hello = new JsonObject
            {
                ["type"] = "hello",
                ["token"] = connection.Token,
                ["protocol"] = 1,
                ["python"] = new JsonObject
                {
                    ["version"] = "3.13.3",
                    ["executable"] = "/usr/bin/python3",
                    ["implementation"] = "CPython",
                },
            };

            var bytes = Encoding.UTF8.GetBytes(hello.ToJsonString() + "\n");
            await client.GetStream().WriteAsync(bytes);
            await client.GetStream().FlushAsync();

            await accept;
            connection.StartPump();

            return new FakePeer(connection, client);
        }

        public async Task SendAsync(JsonObject message)
        {
            var bytes = Encoding.UTF8.GetBytes(message.ToJsonString() + "\n");
            await _stream.WriteAsync(bytes);
            await _stream.FlushAsync();
        }

        public async Task<JsonObject> ReadAsync()
        {
            while (true)
            {
                var newline = _pending.IndexOf((byte)'\n');
                if (newline >= 0)
                {
                    var line = Encoding.UTF8.GetString(_pending.GetRange(0, newline).ToArray());
                    _pending.RemoveRange(0, newline + 1);
                    return (JsonObject)JsonNode.Parse(line)!;
                }

                using var cancellation = new CancellationTokenSource(Timeout);
                var read = await _stream.ReadAsync(_buffer, cancellation.Token);
                if (read == 0) throw new IOException("the connection closed while reading");
                _pending.AddRange(_buffer.Take(read));
            }
        }

        public void DropConnection()
        {
            _stream.Dispose();
            _client.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            try { _stream.Dispose(); } catch { /* already dropped */ }
            try { _client.Dispose(); } catch { /* already dropped */ }
            await Connection.DisposeAsync();
        }
    }
}
