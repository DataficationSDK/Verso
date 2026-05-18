using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Microsoft.JSInterop;

namespace Verso.Blazor.Wasm.Services;

/// <summary>
/// Manages communication between the Blazor WASM app and the VS Code extension host
/// via postMessage ↔ JSON-RPC relay implemented in vscode-bridge.js.
/// </summary>
public sealed class VsCodeBridge : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pendingRequests = new();
    private DotNetObjectReference<VsCodeBridge>? _selfRef;
    private int _nextRequestId;
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
        var resultJson = await RequestRawAsync(method, @params);
        return JsonSerializer.Deserialize<T>(resultJson, s_jsonOptions)!;
    }

    /// <summary>
    /// Sends a JSON-RPC request that returns no meaningful result.
    /// </summary>
    public async Task RequestVoidAsync(string method, object? @params = null)
    {
        await RequestRawAsync(method, @params);
    }

    private async Task<string> RequestRawAsync(string method, object? @params)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(id, tcs))
            throw new InvalidOperationException($"Duplicate VS Code bridge request id '{id}'.");

        var paramsJson = @params is not null ? JsonSerializer.Serialize(@params, s_jsonOptions) : null;
        try
        {
            await _js.InvokeVoidAsync("vscodeBridge.sendRequestDetached", id, method, paramsJson);
            return await tcs.Task;
        }
        catch
        {
            _pendingRequests.TryRemove(id, out _);
            throw;
        }
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

    /// <summary>
    /// Called from JS when a detached JSON-RPC request receives a response.
    /// </summary>
    [JSInvokable("OnResponse")]
    public Task OnResponseFromJs(int id, string? resultJson, string? errorJson)
    {
        if (!_pendingRequests.TryRemove(id, out var tcs))
            return Task.CompletedTask;

        if (!string.IsNullOrWhiteSpace(errorJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(errorJson);
                var root = doc.RootElement;
                var message = root.TryGetProperty("message", out var messageEl)
                    ? messageEl.GetString()
                    : "JSON-RPC error";
                var code = root.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var c)
                    ? c
                    : 0;
                tcs.TrySetException(new InvalidOperationException($"{message} (code {code})"));
            }
            catch (JsonException)
            {
                tcs.TrySetException(new InvalidOperationException(errorJson));
            }
        }
        else
        {
            tcs.TrySetResult(resultJson ?? "null");
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _selfRef?.Dispose();
        foreach (var pending in _pendingRequests.Values)
            pending.TrySetCanceled();
        _pendingRequests.Clear();
        return ValueTask.CompletedTask;
    }
}
