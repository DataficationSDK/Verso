using System.Text;
using System.Text.Json;
using Verso.Host;
using Verso.Host.Protocol;

// Force UTF-8 for stdin/stdout — Windows defaults to the OEM code page (e.g. CP437)
// which corrupts non-ASCII characters in JSON-RPC messages.
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

var session = new HostSession(SendLine);

// Emit ready signal
SendLine(JsonRpcMessage.Notification(MethodNames.HostReady, new { version = "1.0.0" }));

await foreach (var line in ReadLinesAsync(Console.In))
{
    if (string.IsNullOrWhiteSpace(line))
        continue;

    string response;
    try
    {
        var (id, method, @params) = JsonRpcMessage.Parse(line);

        if (id is null || method is null)
        {
            // Notification from extension — currently no inbound notifications handled
            continue;
        }

        response = await session.DispatchAsync(id, method, @params);
    }
    catch (JsonException)
    {
        response = JsonRpcMessage.Error(0, JsonRpcMessage.ErrorCodes.ParseError, "Invalid JSON");
    }
    catch (Exception ex)
    {
        response = JsonRpcMessage.Error(0, JsonRpcMessage.ErrorCodes.InternalError, ex.Message);
    }

    SendLine(response);
}

await session.DisposeAsync();

static void SendLine(string json)
{
    Console.Out.WriteLine(json);
    Console.Out.Flush();
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
