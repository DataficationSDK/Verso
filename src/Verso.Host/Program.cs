using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Verso.Host;
using Verso.Host.Protocol;

// Force UTF-8 for stdin/stdout — Windows defaults to the OEM code page (e.g. CP437)
// which corrupts non-ASCII characters in JSON-RPC messages.
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

// Lock ensures atomic line writes when the reader task and main loop
// send responses/notifications concurrently.
var stdoutLock = new object();

var session = new HostSession(json => SendLine(json, stdoutLock));

// Emit ready signal
SendLine(JsonRpcMessage.Notification(MethodNames.HostReady, new { version = "1.0.0" }), stdoutLock);

// Channel for requests that need sequential processing by the main loop.
var requests = Channel.CreateUnbounded<(object id, string method, JsonElement? @params)>();

// Background task: continuously read stdin.
// Consent responses are resolved immediately (they just complete a TCS) to prevent
// deadlocks when a handler is blocked waiting for consent approval.  All other
// requests are forwarded to the main loop via the channel.
_ = Task.Run(async () =>
{
    try
    {
        await foreach (var line in ReadLinesAsync(Console.In))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var (id, method, @params) = JsonRpcMessage.Parse(line);

                if (id is null || method is null)
                    continue;

                if (method == MethodNames.ExtensionConsentResponse)
                {
                    // Handle inline — resolves a TCS, no heavy work.
                    var response = await session.DispatchAsync(id, method, @params);
                    SendLine(response, stdoutLock);
                    continue;
                }

                await requests.Writer.WriteAsync((id, method, @params));
            }
            catch (JsonException)
            {
                SendLine(JsonRpcMessage.Error(0, JsonRpcMessage.ErrorCodes.ParseError, "Invalid JSON"), stdoutLock);
            }
        }
    }
    finally
    {
        requests.Writer.Complete();
    }
});

// Main loop: process all other requests sequentially.
await foreach (var (id, method, @params) in requests.Reader.ReadAllAsync())
{
    string response;
    try
    {
        response = await session.DispatchAsync(id, method, @params);
    }
    catch (JsonException)
    {
        response = JsonRpcMessage.Error(id, JsonRpcMessage.ErrorCodes.ParseError, "Invalid JSON");
    }
    catch (Exception ex)
    {
        response = JsonRpcMessage.Error(id, JsonRpcMessage.ErrorCodes.InternalError, ex.Message);
    }

    SendLine(response, stdoutLock);
}

await session.DisposeAsync();

static void SendLine(string json, object @lock)
{
    lock (@lock)
    {
        Console.Out.WriteLine(json);
        Console.Out.Flush();
    }
}

static async IAsyncEnumerable<string> ReadLinesAsync(TextReader reader)
{
    while (true)
    {
        var line = await reader.ReadLineAsync();
        if (line is null)
            yield break;
        yield return line;
    }
}
