using System.Collections.Concurrent;
using System.Text.Json;
using Verso.Extensions;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;

namespace Verso.Host;

public sealed class NotebookSession : IAsyncDisposable
{
    public Scaffold Scaffold { get; }
    public ExtensionHost ExtensionHost { get; }
    public string NotebookId { get; }

    private CancellationTokenSource? _executionCts;
    private readonly Action<string, object?> _sendNotification;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingConsents = new();
    private int _consentCounter;

    public NotebookSession(
        string notebookId,
        Scaffold scaffold,
        ExtensionHost extensionHost,
        Action<string, object?> sendNotification)
    {
        NotebookId = notebookId;
        Scaffold = scaffold;
        ExtensionHost = extensionHost;
        _sendNotification = sendNotification;

        // Notify the client when extensions are dynamically loaded (e.g. via #!extension)
        // so the WASM UI can refresh its cached extension/layout/theme lists.
        ExtensionHost.OnExtensionLoaded += HandleExtensionLoaded;
    }

    private void HandleExtensionLoaded(Abstractions.IExtension extension)
    {
        SendNotification(Protocol.MethodNames.ExtensionChanged, null);
    }

    public CancellationToken GetExecutionToken()
    {
        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();
        return _executionCts.Token;
    }

    public void CancelExecution()
    {
        _executionCts?.Cancel();
    }

    public void SendNotification(string method, object? @params = null)
    {
        _sendNotification(method, @params);
    }

    /// <summary>
    /// Sends an extension consent request notification to the client and awaits the response.
    /// </summary>
    public async Task<bool> RequestConsentAsync(
        IReadOnlyList<Abstractions.ExtensionConsentInfo> extensions,
        CancellationToken cancellationToken)
    {
        var requestId = $"consent-{Interlocked.Increment(ref _consentCounter)}";
        var tcs = new TaskCompletionSource<bool>();
        _pendingConsents[requestId] = tcs;

        using var registration = cancellationToken.Register(
            () => { tcs.TrySetResult(false); _pendingConsents.TryRemove(requestId, out _); });

        SendNotification(Protocol.MethodNames.ExtensionConsentRequest, new
        {
            requestId,
            extensions = extensions.Select(e => new
            {
                packageId = e.PackageId,
                version = e.Version,
                source = e.Source
            }).ToArray()
        });

        return await tcs.Task;
    }

    /// <summary>
    /// Resolves a pending consent request from the client response.
    /// </summary>
    public void ResolveConsent(string requestId, bool approved)
    {
        if (_pendingConsents.TryRemove(requestId, out var tcs))
            tcs.TrySetResult(approved);
    }

    public async ValueTask DisposeAsync()
    {
        ExtensionHost.OnExtensionLoaded -= HandleExtensionLoaded;

        // Cancel any pending consent requests
        foreach (var tcs in _pendingConsents.Values)
            tcs.TrySetResult(false);
        _pendingConsents.Clear();

        _executionCts?.Dispose();
        await Scaffold.DisposeAsync();
        await ExtensionHost.DisposeAsync();
    }
}

public sealed class HostSession : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, NotebookSession> _sessions = new();
    private int _sessionCounter;
    private readonly Action<string> _sendNotification;

    public HostSession(Action<string> sendNotification)
    {
        _sendNotification = sendNotification;
    }

    public string AddSession(Scaffold scaffold, ExtensionHost extensionHost)
    {
        var id = $"nb-{Interlocked.Increment(ref _sessionCounter)}";
        var ns = new NotebookSession(id, scaffold, extensionHost, (method, @params) =>
        {
            // Embed notebookId in notification params
            var wrapped = new Dictionary<string, object?>
            {
                ["notebookId"] = id
            };
            if (@params is not null)
            {
                var json = JsonSerializer.Serialize(@params, JsonRpcMessage.SerializerOptions);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    wrapped[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                }
            }
            var notification = JsonRpcMessage.Notification(method, wrapped);
            _sendNotification(notification);
        });
        _sessions[id] = ns;
        return id;
    }

    public NotebookSession GetSession(string notebookId)
    {
        if (_sessions.TryGetValue(notebookId, out var ns))
            return ns;
        throw new InvalidOperationException($"Notebook session '{notebookId}' not found.");
    }

    public async Task RemoveSessionAsync(string notebookId)
    {
        if (_sessions.TryRemove(notebookId, out var ns))
            await ns.DisposeAsync();
    }

    public async Task<string> DispatchAsync(object id, string method, JsonElement? @params)
    {
        try
        {
            object? result = method switch
            {
                MethodNames.HostShutdown => HandleShutdown(),
                MethodNames.NotebookOpen => await NotebookHandler.HandleOpenAsync(this, @params),
                MethodNames.NotebookClose => await NotebookHandler.HandleCloseAsync(this, @params),
                _ => await DispatchToSessionAsync(method, @params)
            };
            return JsonRpcMessage.Response(id, result);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Unknown method"))
        {
            return JsonRpcMessage.Error(id, JsonRpcMessage.ErrorCodes.MethodNotFound, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found") || ex.Message.Contains("Missing notebookId"))
        {
            return JsonRpcMessage.Error(id, JsonRpcMessage.ErrorCodes.InvalidParams, ex.Message);
        }
        catch (JsonException ex)
        {
            return JsonRpcMessage.Error(id, JsonRpcMessage.ErrorCodes.InvalidParams, ex.Message);
        }
        catch (Exception ex)
        {
            return JsonRpcMessage.Error(id, JsonRpcMessage.ErrorCodes.InternalError, ex.Message);
        }
    }

    private async Task<object?> DispatchToSessionAsync(string method, JsonElement? @params)
    {
        // Extract notebookId from params
        string? notebookId = null;
        if (@params?.TryGetProperty("notebookId", out var nbIdEl) == true)
            notebookId = nbIdEl.GetString();

        if (string.IsNullOrEmpty(notebookId))
            throw new InvalidOperationException("Missing notebookId in request params. Include notebookId as a sibling field in params.");

        var ns = GetSession(notebookId);

        return method switch
        {
            MethodNames.NotebookSetFilePath => NotebookHandler.HandleSetFilePath(ns, @params),
            MethodNames.NotebookSetDefaultKernel => NotebookHandler.HandleSetDefaultKernel(ns, @params),
            MethodNames.NotebookSave => await NotebookHandler.HandleSaveAsync(ns),
            MethodNames.NotebookGetLanguages => NotebookHandler.HandleGetLanguages(ns),
            MethodNames.NotebookGetToolbarActions => NotebookHandler.HandleGetToolbarActions(ns),
            MethodNames.NotebookGetTheme => ThemeHandler.HandleGetTheme(ns),
            MethodNames.CellAdd => CellHandler.HandleAdd(ns, @params),
            MethodNames.CellInsert => CellHandler.HandleInsert(ns, @params),
            MethodNames.CellRemove => CellHandler.HandleRemove(ns, @params),
            MethodNames.CellMove => CellHandler.HandleMove(ns, @params),
            MethodNames.CellUpdateSource => CellHandler.HandleUpdateSource(ns, @params),
            MethodNames.CellChangeType => CellHandler.HandleChangeType(ns, @params),
            MethodNames.CellChangeLanguage => CellHandler.HandleChangeLanguage(ns, @params),
            MethodNames.CellGet => CellHandler.HandleGet(ns, @params),
            MethodNames.CellList => CellHandler.HandleList(ns),
            MethodNames.ExecutionRun => await ExecutionHandler.HandleRunAsync(ns, @params),
            MethodNames.ExecutionRunAll => await ExecutionHandler.HandleRunAllAsync(ns),
            MethodNames.ExecutionCancel => ExecutionHandler.HandleCancel(ns),
            MethodNames.KernelRestart => await KernelHandler.HandleRestartAsync(ns, @params),
            MethodNames.KernelGetCompletions => await KernelHandler.HandleGetCompletionsAsync(ns, @params),
            MethodNames.KernelGetDiagnostics => await KernelHandler.HandleGetDiagnosticsAsync(ns, @params),
            MethodNames.KernelGetHoverInfo => await KernelHandler.HandleGetHoverInfoAsync(ns, @params),
            MethodNames.OutputClearAll => OutputHandler.HandleClearAll(ns),
            MethodNames.CellInteract => await InteractionHandler.HandleInteractAsync(ns, @params),
            MethodNames.NotebookGetCellTypes => NotebookHandler.HandleGetCellTypes(ns),
            MethodNames.LayoutGetLayouts => LayoutHandler.HandleGetLayouts(ns),
            MethodNames.LayoutSwitch => LayoutHandler.HandleSwitch(ns, @params),
            MethodNames.LayoutRender => await LayoutHandler.HandleRenderAsync(ns),
            MethodNames.LayoutGetCellContainer => await LayoutHandler.HandleGetCellContainerAsync(ns, @params),
            MethodNames.LayoutUpdateCell => LayoutHandler.HandleUpdateCell(ns, @params),
            MethodNames.LayoutSetEditMode => LayoutHandler.HandleSetEditMode(ns, @params),
            MethodNames.ThemeGetThemes => ThemeHandler.HandleGetThemes(ns),
            MethodNames.ThemeSwitch => ThemeHandler.HandleSwitchTheme(ns, @params),
            MethodNames.ExtensionList => ExtensionHandler.HandleList(ns),
            MethodNames.ExtensionEnable => await ExtensionHandler.HandleEnableAsync(ns, @params),
            MethodNames.ExtensionDisable => await ExtensionHandler.HandleDisableAsync(ns, @params),
            MethodNames.SettingsGetDefinitions => SettingsHandler.HandleGetDefinitions(ns),
            MethodNames.SettingsGet => SettingsHandler.HandleGet(ns, @params),
            MethodNames.SettingsUpdate => await SettingsHandler.HandleUpdateAsync(ns, @params),
            MethodNames.SettingsReset => await SettingsHandler.HandleResetAsync(ns, @params),
            MethodNames.ToolbarGetEnabledStates => await ToolbarHandler.HandleGetEnabledStatesAsync(ns, @params),
            MethodNames.ToolbarExecute => await ToolbarHandler.HandleExecuteAsync(ns, @params),
            MethodNames.VariableList => VariableHandler.HandleList(ns),
            MethodNames.VariableInspect => await VariableHandler.HandleInspectAsync(ns, @params),
            MethodNames.ExtensionConsentResponse => HandleConsentResponse(ns, @params),
            _ => throw new InvalidOperationException($"Unknown method: {method}")
        };
    }

    private static object? HandleConsentResponse(NotebookSession ns, JsonElement? @params)
    {
        var requestId = @params?.GetProperty("requestId").GetString();
        var approved = @params?.GetProperty("approved").GetBoolean() ?? false;
        if (requestId is not null)
            ns.ResolveConsent(requestId, approved);
        return null;
    }

    private object? HandleShutdown()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            Environment.Exit(0);
        });
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var ns in _sessions.Values)
            await ns.DisposeAsync();
        _sessions.Clear();
    }
}
