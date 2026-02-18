using System.Text.Json;
using Verso.Abstractions;
using Verso.Extensions.Layouts;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class LayoutHandler
{
    public static LayoutsResult HandleGetLayouts(HostSession session)
    {
        session.EnsureSession();
        var manager = session.Scaffold!.LayoutManager;
        if (manager is null)
            return new LayoutsResult();

        var activeId = manager.ActiveLayout?.LayoutId;
        return new LayoutsResult
        {
            Layouts = manager.AvailableLayouts.Select(l => new LayoutDto
            {
                Id = l.LayoutId,
                DisplayName = l.DisplayName,
                Icon = l.Icon,
                RequiresCustomRenderer = l.RequiresCustomRenderer,
                IsActive = string.Equals(l.LayoutId, activeId, StringComparison.OrdinalIgnoreCase)
            }).ToList()
        };
    }

    public static object? HandleSwitch(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<LayoutSwitchParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/switch");

        var manager = session.Scaffold!.LayoutManager
            ?? throw new InvalidOperationException("No layout manager initialized.");

        manager.SetActiveLayout(p.LayoutId);
        session.Scaffold!.Notebook.ActiveLayoutId = p.LayoutId;
        return null;
    }

    public static async Task<LayoutRenderResult> HandleRenderAsync(HostSession session)
    {
        session.EnsureSession();
        var manager = session.Scaffold!.LayoutManager
            ?? throw new InvalidOperationException("No layout manager initialized.");

        var layout = manager.ActiveLayout
            ?? throw new InvalidOperationException("No active layout.");

        var cells = session.Scaffold!.Cells;
        var context = new HostVersoContext(session.Scaffold!);
        var result = await layout.RenderLayoutAsync(cells, context).ConfigureAwait(false);

        return new LayoutRenderResult { Html = result.Content };
    }

    public static async Task<LayoutGetCellContainerResult> HandleGetCellContainerAsync(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<LayoutGetCellContainerParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/getCellContainer");

        if (!Guid.TryParse(p.CellId, out var cellId))
            throw new JsonException($"Invalid cell ID: {p.CellId}");

        var layout = session.Scaffold!.LayoutManager?.ActiveLayout
            ?? throw new InvalidOperationException("No active layout.");

        var context = new HostVersoContext(session.Scaffold!);
        var info = await layout.GetCellContainerAsync(cellId, context).ConfigureAwait(false);

        return new LayoutGetCellContainerResult
        {
            Row = (int)info.Y,
            Col = (int)info.X,
            Width = (int)info.Width,
            Height = (int)info.Height
        };
    }

    public static object? HandleUpdateCell(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<LayoutUpdateCellParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/updateCell");

        var manager = session.Scaffold!.LayoutManager
            ?? throw new InvalidOperationException("No layout manager initialized.");

        if (manager.ActiveLayout is DashboardLayout dashboard)
        {
            if (!Guid.TryParse(p.CellId, out var cellId))
                throw new JsonException($"Invalid cell ID: {p.CellId}");

            dashboard.UpdateCellPosition(cellId, p.Row, p.Col, p.Width, p.Height);
        }

        return null;
    }

    public static object? HandleSetEditMode(HostSession session, JsonElement? @params)
    {
        session.EnsureSession();
        var p = @params?.Deserialize<LayoutSetEditModeParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/setEditMode");

        var manager = session.Scaffold!.LayoutManager
            ?? throw new InvalidOperationException("No layout manager initialized.");

        if (manager.ActiveLayout is DashboardLayout dashboard)
        {
            dashboard.IsEditMode = p.EditMode;
        }

        return null;
    }

    /// <summary>
    /// Minimal IVersoContext for rendering layouts on the host side.
    /// </summary>
    private sealed class HostVersoContext : IVersoContext
    {
        private readonly Scaffold _scaffold;

        public HostVersoContext(Scaffold scaffold) => _scaffold = scaffold;

        public IVariableStore Variables => _scaffold.Variables;
        public CancellationToken CancellationToken => CancellationToken.None;
        public IThemeContext Theme => _scaffold.ThemeContext;
        public LayoutCapabilities LayoutCapabilities => _scaffold.LayoutCapabilities;
        public IExtensionHostContext ExtensionHost => _scaffold.ExtensionHostContext;
        public INotebookMetadata NotebookMetadata => new HostNotebookMetadata(_scaffold);
        public INotebookOperations Notebook => _scaffold.NotebookOps;
        public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;

        private sealed class HostNotebookMetadata : INotebookMetadata
        {
            private readonly Scaffold _s;
            public HostNotebookMetadata(Scaffold s) => _s = s;
            public string? Title => _s.Title;
            public string? DefaultKernelId => _s.DefaultKernelId;
            public string? FilePath => null;
        }
    }
}
