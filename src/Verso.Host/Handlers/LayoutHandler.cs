using System.Text.Json;
using Verso.Abstractions;
using Verso.Extensions.Layouts;
using Verso.Host.Dto;
using Verso.Host.Protocol;

namespace Verso.Host.Handlers;

public static class LayoutHandler
{
    public static LayoutsResult HandleGetLayouts(NotebookSession ns)
    {
        var manager = ns.Scaffold.LayoutManager;
        if (manager is null)
            return new LayoutsResult();

        var activeLayout = manager.ActiveLayout;
        var activeExtensionId = (activeLayout as IExtension)?.ExtensionId;
        var activeLayoutId = activeLayout?.LayoutId;

        return new LayoutsResult
        {
            Layouts = ns.ExtensionHost.GetLayouts().Select(l =>
            {
                var owningExtensionId = (l as IExtension)?.ExtensionId ?? string.Empty;
                var isActive = string.Equals(l.LayoutId, activeLayoutId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(owningExtensionId, activeExtensionId, StringComparison.OrdinalIgnoreCase);

                return new LayoutDto
                {
                    Id = l.LayoutId,
                    ExtensionId = owningExtensionId,
                    DisplayName = l.DisplayName,
                    Icon = l.Icon,
                    RequiresCustomRenderer = l.RequiresCustomRenderer,
                    RendererIsolation = l.RendererIsolation,
                    IsActive = isActive,
                    Capabilities = (int)l.Capabilities,
                    SupportsPropertiesPanel = l.SupportsPropertiesPanel
                };
            }).ToList()
        };
    }

    public static object? HandleSwitch(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LayoutSwitchParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/switch");

        var manager = ns.Scaffold.LayoutManager
            ?? throw new InvalidOperationException("No layout manager initialized.");

        LayoutReference reference;
        if (!string.IsNullOrEmpty(p.ExtensionId))
        {
            reference = new LayoutReference(p.ExtensionId, p.LayoutId);
        }
        else
        {
            // Legacy bare-string form. Deprecated in v1.0, removed in v2.0.
            // Log a deprecation warning so we can measure when external clients have migrated.
            Console.Error.WriteLine(
                $"[deprecation] layout/switch invoked with bare layoutId '{p.LayoutId}' " +
                $"and no extensionId. Bare-form support is removed in v2.0.");
            reference = new LayoutReference(string.Empty, p.LayoutId);
        }

        manager.SetActiveLayout(reference);

        // Capture the resolved qualified pair from the manager so the saved notebook
        // carries the fully-qualified form even when the call arrived in legacy shape.
        if (manager.ActiveLayout is { } resolved && resolved is IExtension resolvedExt)
        {
            ns.Scaffold.Notebook.ActiveLayout =
                new LayoutReference(resolvedExt.ExtensionId, resolved.LayoutId);
            ns.Scaffold.Notebook.RequiresLegacyLayoutResolution = false;
        }
        else
        {
            ns.Scaffold.Notebook.ActiveLayout = reference;
        }
        return null;
    }

    public static async Task<LayoutRenderResult> HandleRenderAsync(NotebookSession ns)
    {
        var manager = ns.Scaffold.LayoutManager
            ?? throw new InvalidOperationException("No layout manager initialized.");

        var layout = manager.ActiveLayout
            ?? throw new InvalidOperationException("No active layout.");

        var cells = ns.Scaffold.Cells;
        var context = new HostVersoContext(ns.Scaffold);
        var result = await layout.RenderLayoutAsync(cells, context).ConfigureAwait(false);

        return new LayoutRenderResult { Html = result.Content };
    }

    public static async Task<LayoutGetCellContainerResult> HandleGetCellContainerAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LayoutGetCellContainerParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/getCellContainer");

        if (!Guid.TryParse(p.CellId, out var cellId))
            throw new JsonException($"Invalid cell ID: {p.CellId}");

        var layout = ns.Scaffold.LayoutManager?.ActiveLayout
            ?? throw new InvalidOperationException("No active layout.");

        var context = new HostVersoContext(ns.Scaffold);
        var info = await layout.GetCellContainerAsync(cellId, context).ConfigureAwait(false);

        return new LayoutGetCellContainerResult
        {
            Row = (int)info.Y,
            Col = (int)info.X,
            Width = (int)info.Width,
            Height = (int)info.Height
        };
    }

    public static object? HandleUpdateCell(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LayoutUpdateCellParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/updateCell");

        var manager = ns.Scaffold.LayoutManager
            ?? throw new InvalidOperationException("No layout manager initialized.");

        if (manager.ActiveLayout is DashboardLayout dashboard)
        {
            if (!Guid.TryParse(p.CellId, out var cellId))
                throw new JsonException($"Invalid cell ID: {p.CellId}");

            dashboard.UpdateCellPosition(cellId, p.Row, p.Col, p.Width, p.Height);
        }

        return null;
    }

    public static object? HandleSetEditMode(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LayoutSetEditModeParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/setEditMode");

        var manager = ns.Scaffold.LayoutManager
            ?? throw new InvalidOperationException("No layout manager initialized.");

        if (manager.ActiveLayout is DashboardLayout dashboard)
        {
            dashboard.IsEditMode = p.EditMode;
        }

        return null;
    }

    public static async Task<object?> HandleInteractAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LayoutInteractParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/interact");

        if (string.IsNullOrWhiteSpace(p.ExtensionId))
            throw new JsonException("layout/interact requires 'extensionId'.");
        if (string.IsNullOrWhiteSpace(p.LayoutId))
            throw new JsonException("layout/interact requires 'layoutId'.");

        if (!ns.ExtensionHost.TryGetLayoutInteractionHandler(p.ExtensionId, p.LayoutId, out var handler))
        {
            // The "not found" substring routes this to JsonRpcMessage.ErrorCodes.InvalidParams
            // via HostSession.DispatchAsync. The symbolic code is included in the message
            // so clients and tests can key off it.
            throw new InvalidOperationException(
                $"LAYOUT_INTERACTION_HANDLER_NOT_FOUND: no layout interaction handler is registered " +
                $"for (extensionId='{p.ExtensionId}', layoutId='{p.LayoutId}').");
        }

        var extensionId = p.ExtensionId;
        var layoutId = p.LayoutId;
        var frameInstanceId = p.FrameInstanceId ?? string.Empty;

        var context = new LayoutInteractionContext
        {
            ExtensionId = extensionId,
            LayoutId = layoutId,
            FrameInstanceId = frameInstanceId,
            InteractionType = p.InteractionType ?? string.Empty,
            Payload = p.Payload ?? string.Empty,
            TargetId = p.TargetId,
            Verso = new HostVersoContext(ns.Scaffold),
            CancellationToken = CancellationToken.None,
            RequestRender = () => ns.SendLayoutUpdated(extensionId, layoutId, frameInstanceId, "full"),
            RequestCellRefresh = cellId => ns.SendLayoutUpdated(extensionId, layoutId, frameInstanceId, "cell", cellId)
        };

        try
        {
            await handler.OnLayoutInteractionAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Preserve the originating extension id so dispatch-side errors are
            // diagnosable. HostSession.DispatchAsync surfaces this as a JSON-RPC
            // InternalError with the rewrapped message.
            throw new InvalidOperationException($"[{p.ExtensionId}] {ex.Message}", ex);
        }

        return null;
    }

    /// <summary>
    /// Minimal IVersoContext for rendering layouts on the host side.
    /// </summary>
    internal sealed class HostVersoContext : IVersoContext
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
        public string? ActiveLayoutId => _scaffold.LayoutManager?.ActiveLayout?.LayoutId;
        public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;

        private sealed class HostNotebookMetadata : INotebookMetadata
        {
            private readonly Scaffold _s;
            public HostNotebookMetadata(Scaffold s) => _s = s;
            public string? Title => _s.Title;
            public string? DefaultKernelId => _s.DefaultKernelId;
            public string? FilePath => null;
            public Dictionary<string, NotebookParameterDefinition>? Parameters => _s.Notebook.Parameters;
        }
    }
}
