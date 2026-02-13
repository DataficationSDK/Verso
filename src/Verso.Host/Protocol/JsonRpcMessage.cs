using System.Text.Json;
using System.Text.Json.Serialization;

namespace Verso.Host.Protocol;

public static class JsonRpcMessage
{
    private const string Version = "2.0";

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static string Response(object id, object? result)
    {
        var msg = new Dictionary<string, object?>
        {
            ["jsonrpc"] = Version,
            ["id"] = id,
            ["result"] = result
        };
        return JsonSerializer.Serialize(msg, SerializerOptions);
    }

    public static string Error(object id, int code, string message, object? data = null)
    {
        var error = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data is not null)
            error["data"] = data;

        var msg = new Dictionary<string, object?>
        {
            ["jsonrpc"] = Version,
            ["id"] = id,
            ["error"] = error
        };
        return JsonSerializer.Serialize(msg, SerializerOptions);
    }

    public static string Notification(string method, object? @params = null)
    {
        var msg = new Dictionary<string, object?>
        {
            ["jsonrpc"] = Version,
            ["method"] = method,
            ["params"] = @params
        };
        return JsonSerializer.Serialize(msg, SerializerOptions);
    }

    public static (object? Id, string? Method, JsonElement? Params) Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        object? id = null;
        if (root.TryGetProperty("id", out var idEl))
        {
            id = idEl.ValueKind switch
            {
                JsonValueKind.Number => idEl.GetInt64(),
                JsonValueKind.String => idEl.GetString(),
                _ => null
            };
        }

        string? method = null;
        if (root.TryGetProperty("method", out var methodEl))
            method = methodEl.GetString();

        JsonElement? @params = null;
        if (root.TryGetProperty("params", out var paramsEl))
            @params = paramsEl.Clone();

        return (id, method, @params);
    }

    // Standard JSON-RPC error codes
    public static class ErrorCodes
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
    }
}
