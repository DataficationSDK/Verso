using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Verso.Host;
using Verso.Host.Protocol;

// Force UTF-8 for stdin/stdout — Windows defaults to the OEM code page (e.g. CP437)
// which corrupts non-ASCII characters in JSON-RPC messages.
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

// Bind the protocol writer to the real underlying stdout stream, not Console.Out.
// Kernels (CSharpKernel, FsiSessionManager) call Console.SetOut to a StringWriter
// during cell evaluation so they can capture user stdout. If notifications routed
// through Console.Out, an output/update emitted mid-cell would land in that capture
// buffer and be re-emitted as a text/plain cell output.
var stdoutWriter = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
{
    AutoFlush = false
};

// Lock ensures atomic line writes when the reader task and main loop
// send responses/notifications concurrently.
var stdoutLock = new object();

var session = new HostSession(json => SendLine(json, stdoutWriter, stdoutLock));

// Emit ready signal
SendLine(JsonRpcMessage.Notification(MethodNames.HostReady, new { version = "1.0.0" }), stdoutWriter, stdoutLock);

// Channel for requests that need sequential processing by the main loop.
var requests = Channel.CreateUnbounded<(object id, string method, JsonElement? @params)>();

// Background task: continuously read stdin.
// Consent and input responses are resolved immediately (they just complete a TCS)
// to prevent deadlocks when a handler is blocked waiting for user approval/input.
// All other requests are forwarded to the main loop via the channel.
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

                if (method == MethodNames.ExtensionConsentResponse ||
                    method == MethodNames.InputResponse)
                {
                    // Handle inline — resolves a TCS, no heavy work.
                    var response = await session.DispatchAsync(id, method, @params);
                    SendLine(response, stdoutWriter, stdoutLock);
                    continue;
                }

                if (method == MethodNames.ExecutionCancel)
                {
                    // Handle inline so cancel does not queue behind the running execution
                    // it is trying to interrupt. The dispatch just signals the session CTS,
                    // which the kernel observes through IExecutionContext.CancellationToken.
                    var response = await session.DispatchAsync(id, method, @params);
                    SendLine(response, stdoutWriter, stdoutLock);
                    continue;
                }

                await requests.Writer.WriteAsync((id, method, @params));
            }
            catch (JsonException)
            {
                SendLine(JsonRpcMessage.Error(0, JsonRpcMessage.ErrorCodes.ParseError, "Invalid JSON"), stdoutWriter, stdoutLock);
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

    SendLine(response, stdoutWriter, stdoutLock);
}

await session.DisposeAsync();

static void SendLine(string json, TextWriter writer, object @lock)
{
    lock (@lock)
    {
        writer.WriteLine(json);
        writer.Flush();
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
