using System.Text.Json;
using Verso.Extensions;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;

namespace Verso.Host;

public sealed class HostSession : IAsyncDisposable
{
    private Scaffold? _scaffold;
    private ExtensionHost? _extensionHost;
    private CancellationTokenSource? _executionCts;
    private readonly Action<string> _sendNotification;

    public Scaffold? Scaffold => _scaffold;
    public ExtensionHost? ExtensionHost => _extensionHost;

    public HostSession(Action<string> sendNotification)
    {
        _sendNotification = sendNotification;
    }

    public void SetSession(Scaffold scaffold, ExtensionHost extensionHost)
    {
        _scaffold = scaffold;
        _extensionHost = extensionHost;
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
        var json = JsonRpcMessage.Notification(method, @params);
        _sendNotification(json);
    }

    public async Task<string> DispatchAsync(object id, string method, JsonElement? @params)
    {
        try
        {
            object? result = method switch
            {
                MethodNames.HostShutdown => HandleShutdown(),
                MethodNames.NotebookOpen => await NotebookHandler.HandleOpenAsync(this, @params),
                MethodNames.NotebookSave => await NotebookHandler.HandleSaveAsync(this),
                MethodNames.NotebookGetLanguages => NotebookHandler.HandleGetLanguages(this),
                MethodNames.NotebookGetToolbarActions => NotebookHandler.HandleGetToolbarActions(this),
                MethodNames.NotebookGetTheme => ThemeHandler.HandleGetTheme(this),
                MethodNames.CellAdd => CellHandler.HandleAdd(this, @params),
                MethodNames.CellInsert => CellHandler.HandleInsert(this, @params),
                MethodNames.CellRemove => CellHandler.HandleRemove(this, @params),
                MethodNames.CellMove => CellHandler.HandleMove(this, @params),
                MethodNames.CellUpdateSource => CellHandler.HandleUpdateSource(this, @params),
                MethodNames.CellGet => CellHandler.HandleGet(this, @params),
                MethodNames.CellList => CellHandler.HandleList(this),
                MethodNames.ExecutionRun => await ExecutionHandler.HandleRunAsync(this, @params),
                MethodNames.ExecutionRunAll => await ExecutionHandler.HandleRunAllAsync(this),
                MethodNames.ExecutionCancel => ExecutionHandler.HandleCancel(this),
                MethodNames.KernelRestart => await KernelHandler.HandleRestartAsync(this, @params),
                MethodNames.KernelGetCompletions => await KernelHandler.HandleGetCompletionsAsync(this, @params),
                MethodNames.KernelGetDiagnostics => await KernelHandler.HandleGetDiagnosticsAsync(this, @params),
                MethodNames.KernelGetHoverInfo => await KernelHandler.HandleGetHoverInfoAsync(this, @params),
                MethodNames.OutputClearAll => OutputHandler.HandleClearAll(this),
                _ => throw new InvalidOperationException($"Unknown method: {method}")
            };
            return JsonRpcMessage.Response(id, result);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Unknown method"))
        {
            return JsonRpcMessage.Error(id, JsonRpcMessage.ErrorCodes.MethodNotFound, ex.Message);
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

    private object? HandleShutdown()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            Environment.Exit(0);
        });
        return null;
    }

    public void EnsureSession()
    {
        if (_scaffold is null)
            throw new InvalidOperationException("No notebook is open. Call notebook/open first.");
    }

    public async ValueTask DisposeAsync()
    {
        _executionCts?.Dispose();
        if (_scaffold is not null)
            await _scaffold.DisposeAsync();
        if (_extensionHost is not null)
            await _extensionHost.DisposeAsync();
    }
}
