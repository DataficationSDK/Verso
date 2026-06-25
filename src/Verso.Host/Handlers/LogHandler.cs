using System.Text.Json;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

/// <summary>
/// Receives diagnostic log entries forwarded from in-frame extension code
/// (the iframe bridge's <c>verso.log</c>). Writes them to host stdout so the
/// host process owns the diagnostic surface.
/// </summary>
public static class LogHandler
{
    public static object? HandleExtensionLog(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LogExtensionParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for log/extension");

        if (string.IsNullOrWhiteSpace(p.FrameInstanceId))
            throw new JsonException("log/extension requires 'frameInstanceId'.");
        if (string.IsNullOrWhiteSpace(p.Level))
            throw new JsonException("log/extension requires 'level'.");
        if (p.Message is null)
            throw new JsonException("log/extension requires 'message'.");

        Console.WriteLine(
            $"[frame:{p.FrameInstanceId}] [{p.Level}] [{p.ExtensionId}/{p.LayoutId}] {p.Message}");

        return null;
    }
}
