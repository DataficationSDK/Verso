using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Verso.Host;
using Verso.Host.Protocol;
using Verso.Localization;

// Force UTF-8 for stdin/stdout — Windows defaults to the OEM code page (e.g. CP437)
// which corrupts non-ASCII characters in JSON-RPC messages.
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

// Before any kernel loads, so a message produced while an extension is being discovered is
// already in the caller's language. Only the interface language moves: this process runs the
// kernels, and a cell's own results are data rather than chrome.
VersoCultures.ApplyUiCulture(VersoCultures.Resolve(VersoCultures.FromArguments(args)));

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
// The requests a running cell is itself waiting on (an answer to a prompt, a cancel, anything
// that drives a stopped cell) are answered here, so they cannot queue behind the run they belong
// to. HostSession.IsAnsweredWhileRunning names them.
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

                if (HostSession.IsAnsweredWhileRunning(method))
                {
                    // Handled inline: each of these completes something already pending or
                    // signals the run, with no heavy work of its own.
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

        // Standard input has closed, so nobody is left to answer a prompt or move a stopped
        // cell on. Without this the main loop would stay inside that cell's run for good.
        session.AbandonRunningCells();
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
