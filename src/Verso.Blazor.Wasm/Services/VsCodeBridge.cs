using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Verso.Blazor.Wasm.Services;

/// <summary>
/// Manages communication between the Blazor WASM app and the VS Code extension host
/// via postMessage ↔ JSON-RPC relay implemented in vscode-bridge.js.
/// </summary>
public sealed class VsCodeBridge : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<VsCodeBridge>? _selfRef;
    private bool _initialized;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Fired when the host sends a JSON-RPC notification.
    /// Parameters: (method, paramsJson).
    /// </summary>
    public event Action<string, string?>? OnNotification;

    public VsCodeBridge(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>
    /// Registers the .NET notification handler with the JS bridge.
    /// Must be called once after the WASM app has rendered.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _selfRef = DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("vscodeBridge.registerNotificationHandler", _selfRef);
        _initialized = true;
    }

    /// <summary>
    /// Returns true when running inside a VS Code webview.
    /// </summary>
    public async Task<bool> IsVsCodeWebviewAsync()
    {
        return await _js.InvokeAsync<bool>("vscodeBridge.isVsCodeWebview");
    }

    /// <summary>
    /// Sends a JSON-RPC request to the host and deserializes the response.
    /// </summary>
    public async Task<T> RequestAsync<T>(string method, object? @params = null)
    {
        var paramsJson = @params is not null ? JsonSerializer.Serialize(@params, s_jsonOptions) : null;
        var resultJson = await _js.InvokeAsync<string>("vscodeBridge.sendRequest", method, paramsJson);
        return JsonSerializer.Deserialize<T>(resultJson, s_jsonOptions)!;
    }

    /// <summary>
    /// Sends a JSON-RPC request that returns no meaningful result.
    /// </summary>
    public async Task RequestVoidAsync(string method, object? @params = null)
    {
        var paramsJson = @params is not null ? JsonSerializer.Serialize(@params, s_jsonOptions) : null;
        await _js.InvokeAsync<string>("vscodeBridge.sendRequest", method, paramsJson);
    }

    /// <summary>
    /// Called from JS when the host sends a notification.
    /// </summary>
    [JSInvokable("OnNotification")]
    public Task OnNotificationFromJs(string method, string? paramsJson)
    {
        OnNotification?.Invoke(method, paramsJson);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _selfRef?.Dispose();
        return ValueTask.CompletedTask;
    }
}
