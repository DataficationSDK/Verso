using System.Text.Json;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class ToolbarHandler
{
    public static async Task<ToolbarGetEnabledStatesResult> HandleGetEnabledStatesAsync(
        HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<ToolbarGetEnabledStatesParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for toolbar/getEnabledStates");

        var scaffold = session.Scaffold!;
        var extensionHost = session.ExtensionHost!;

        var selectedIds = p.SelectedCellIds
            .Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();

        if (!Enum.TryParse<ToolbarPlacement>(p.Placement, ignoreCase: true, out var placement))
            throw new JsonException($"Invalid placement: {p.Placement}");

        var actions = extensionHost.GetToolbarActions()
            .Where(a => a.Placement == placement)
            .ToList();

        var context = new HostToolbarActionContext(scaffold, selectedIds, session);
        var states = new Dictionary<string, bool>();

        foreach (var action in actions)
        {
            try
            {
                states[action.ActionId] = await action.IsEnabledAsync(context);
            }
            catch
            {
                states[action.ActionId] = false;
            }
        }

        return new ToolbarGetEnabledStatesResult { States = states };
    }

    public static async Task<object?> HandleExecuteAsync(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<ToolbarExecuteParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for toolbar/execute");

        var scaffold = session.Scaffold!;
        var extensionHost = session.ExtensionHost!;

        var action = extensionHost.GetToolbarActions()
            .FirstOrDefault(a => a.ActionId == p.ActionId)
            ?? throw new InvalidOperationException($"Unknown action: {p.ActionId}");

        var selectedIds = p.SelectedCellIds
            .Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();

        var context = new HostToolbarActionContext(scaffold, selectedIds, session);
        await action.ExecuteAsync(context);

        return null;
    }

    /// <summary>
    /// IToolbarActionContext implementation for the host process.
    /// Similar to BlazorToolbarActionContext but without JS interop.
    /// File downloads are routed through the host session as JSON-RPC notifications.
    /// </summary>
    private sealed class HostToolbarActionContext : IToolbarActionContext
    {
        private readonly Scaffold _scaffold;
        private readonly HostSession _session;

        public HostToolbarActionContext(Scaffold scaffold, IReadOnlyList<Guid> selectedCellIds, HostSession session)
        {
            _scaffold = scaffold;
            _session = session;
            SelectedCellIds = selectedCellIds;
        }

        public IReadOnlyList<Guid> SelectedCellIds { get; }
        public IReadOnlyList<CellModel> NotebookCells => _scaffold.Cells;
        public string? ActiveKernelId => _scaffold.DefaultKernelId;
        public IVariableStore Variables => _scaffold.Variables;
        public CancellationToken CancellationToken => CancellationToken.None;
        public IThemeContext Theme => _scaffold.ThemeContext;
        public LayoutCapabilities LayoutCapabilities => _scaffold.LayoutCapabilities;
        public IExtensionHostContext ExtensionHost => _scaffold.ExtensionHostContext;
        public INotebookMetadata NotebookMetadata => new HostNotebookMetadata(_scaffold);
        public INotebookOperations Notebook => _scaffold.NotebookOps;

        public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;

        public Task RequestFileDownloadAsync(string fileName, string contentType, byte[] data)
        {
            _session.SendNotification(MethodNames.FileDownload, new
            {
                fileName,
                contentType,
                data = Convert.ToBase64String(data)
            });
            return Task.CompletedTask;
        }

        private sealed class HostNotebookMetadata : INotebookMetadata
        {
            private readonly Scaffold _scaffold;
            public HostNotebookMetadata(Scaffold scaffold) => _scaffold = scaffold;
            public string? Title => _scaffold.Title;
            public string? DefaultKernelId => _scaffold.DefaultKernelId;
            public string? FilePath => null;
        }
    }
}
