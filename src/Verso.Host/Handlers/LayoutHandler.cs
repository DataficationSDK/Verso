using System.Text.Json;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Layouts;
using Verso.Host.Protocol;
using Verso.Host.Resources;

namespace Verso.Host.Handlers;

/// <summary>
/// Answers the layout half of the protocol: which layouts exist, which one is active, and what an
/// isolated one needs in order to draw.
/// </summary>
/// <remarks>
/// The messages naming a missing field or an unknown identifier stay in English on purpose. They
/// can only appear when a caller sends a request this cannot read, which is a fault in the code
/// rather than anything the reader did, and a log from one machine has to match a search made on
/// another. The same goes for the messages an extension gets back when it uses a layout the wrong
/// way: the person who can act on one is writing code, not reading a notebook.
/// </remarks>
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

                return MapLayout(l, isActive);
            }).ToList()
        };
    }

    /// <summary>
    /// Maps the session's resolved active layout to its wire shape, or null when no
    /// layout manager exists or the notebook's referenced layout is not registered.
    /// Used by the notebook/open response so clients can render the correct layout
    /// on first paint without waiting for a layout/getLayouts round trip.
    /// </summary>
    internal static LayoutDto? GetActiveLayout(NotebookSession ns)
        => ns.Scaffold.LayoutManager?.ActiveLayout is { } active
            ? MapLayout(active, isActive: true)
            : null;

    internal static LayoutDto MapLayout(ILayoutEngine layout, bool isActive)
    {
        return new LayoutDto
        {
            Id = layout.LayoutId,
            ExtensionId = (layout as IExtension)?.ExtensionId ?? string.Empty,
            DisplayName = layout.DisplayName,
            Icon = layout.Icon,
            RequiresCustomRenderer = layout.RequiresCustomRenderer,
            RendererIsolation = layout.RendererIsolation.ToWireString(),
            IsActive = isActive,
            Capabilities = (int)layout.Capabilities,
            SupportsPropertiesPanel = layout.SupportsPropertiesPanel
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

    public static async Task<LayoutRenderResult> HandleRenderAsync(NotebookSession ns, JsonElement? @params)
    {
        var manager = ns.Scaffold.LayoutManager
            ?? throw new InvalidOperationException("No layout manager initialized.");

        var layout = manager.ActiveLayout
            ?? throw new InvalidOperationException("No active layout.");

        var cells = ns.Scaffold.Cells;
        var context = new HostVersoContext(ns.Scaffold, ParseCollapsedSections(@params));
        var result = await layout.RenderLayoutAsync(cells, context).ConfigureAwait(false);

        return new LayoutRenderResult { Html = result.Content };
    }

    private static IReadOnlySet<Guid> ParseCollapsedSections(JsonElement? @params)
    {
        var set = new HashSet<Guid>();
        var p = @params?.Deserialize<LayoutRenderParams>(JsonRpcMessage.SerializerOptions);
        if (p?.CollapsedSections is { } ids)
        {
            foreach (var id in ids)
            {
                if (Guid.TryParse(id, out var cellId))
                    set.Add(cellId);
            }
        }
        return set;
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

    public static async Task<object?> HandleUpdateCell(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LayoutUpdateCellParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/updateCell");

        if (!Guid.TryParse(p.CellId, out _))
            throw new JsonException($"Invalid cell ID: {p.CellId}");

        Console.Error.WriteLine(
            "[deprecation] layout/updateCell is forwarded to layout/interact and will be removed in v2.0. " +
            "Migrate clients to layout/interact with interactionType 'updateCellPosition'.");

        var payload = JsonSerializer.Serialize(
            new { cellId = p.CellId, row = p.Row, col = p.Col, width = p.Width, height = p.Height },
            JsonRpcMessage.SerializerOptions);

        return await HandleInteractAsync(ns, BuildDashboardInteractParams("updateCellPosition", payload))
            .ConfigureAwait(false);
    }

    private static JsonElement BuildDashboardInteractParams(string interactionType, string payload)
    {
        var interactParams = new LayoutInteractParams
        {
            ExtensionId = "verso.layout.dashboard",
            LayoutId = "dashboard",
            FrameInstanceId = string.Empty,
            InteractionType = interactionType,
            Payload = payload
        };
        return JsonSerializer.SerializeToElement(interactParams, JsonRpcMessage.SerializerOptions);
    }

    public static async Task<LayoutGetRendererPackageResult?> HandleGetRendererPackageAsync(
        NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LayoutGetRendererPackageParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/getRendererPackage");

        if (string.IsNullOrWhiteSpace(p.ExtensionId))
            throw new JsonException("layout/getRendererPackage requires 'extensionId'.");
        if (string.IsNullOrWhiteSpace(p.LayoutId))
            throw new JsonException("layout/getRendererPackage requires 'layoutId'.");

        if (!ns.ExtensionHost.TryGetLayoutEngine(p.ExtensionId, p.LayoutId, out var engine))
        {
            // The "not found" substring routes this to JsonRpcMessage.ErrorCodes.InvalidParams
            // via HostSession.DispatchAsync. The symbolic code is included in the message
            // so clients and tests can key off it.
            throw new InvalidOperationException(
                $"LAYOUT_ENGINE_NOT_FOUND: no layout engine is registered " +
                $"for (extensionId='{p.ExtensionId}', layoutId='{p.LayoutId}').");
        }

        if (engine.RendererIsolation != LayoutRendererIsolation.Isolated)
            return null;

        var context = new HostVersoContext(ns.Scaffold);
        var package = await engine.GetRendererPackageAsync(context).ConfigureAwait(false);
        if (package is null) return null;

        var encodedFiles = new Dictionary<string, string>(package.Files.Count);
        foreach (var (path, bytes) in package.Files)
            encodedFiles[path] = Convert.ToBase64String(bytes);

        return new LayoutGetRendererPackageResult
        {
            EntryPoint = package.EntryPoint,
            Files = encodedFiles,
            ContentSecurityPolicy = package.ContentSecurityPolicy,
            RendererProtocolVersion = package.RendererProtocolVersion
        };
    }

    public static async Task<LayoutGetStaticAssetsResult> HandleGetStaticAssetsAsync(
        NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LayoutGetStaticAssetsParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/getStaticAssets");

        if (string.IsNullOrWhiteSpace(p.ExtensionId))
            throw new JsonException("layout/getStaticAssets requires 'extensionId'.");
        if (string.IsNullOrWhiteSpace(p.LayoutId))
            throw new JsonException("layout/getStaticAssets requires 'layoutId'.");

        if (!ns.ExtensionHost.TryGetLayoutEngine(p.ExtensionId, p.LayoutId, out var engine))
        {
            // The "not found" substring routes this to JsonRpcMessage.ErrorCodes.InvalidParams
            // via HostSession.DispatchAsync. The symbolic code is included in the message
            // so clients and tests can key off it.
            throw new InvalidOperationException(
                $"LAYOUT_ENGINE_NOT_FOUND: no layout engine is registered " +
                $"for (extensionId='{p.ExtensionId}', layoutId='{p.LayoutId}').");
        }

        var capabilities = new LayoutHostCapabilities(
            new HashSet<string>(p.SupportedAssetContentTypes ?? new(), StringComparer.Ordinal),
            new HashSet<string>(p.SupportedRenderFormats ?? new(), StringComparer.Ordinal));

        var context = new HostVersoContext(ns.Scaffold);
        var assets = await engine.GetStaticAssetsAsync(context, capabilities).ConfigureAwait(false);

        var result = new LayoutGetStaticAssetsResult();
        if (assets is null) return result;

        foreach (var asset in assets)
        {
            // Defensive filter: drop anything the host did not advertise. The engine is
            // expected to honor the capability set, but a stale or misbehaving engine
            // should not poison the host pipeline.
            if (!capabilities.SupportedAssetContentTypes.Contains(asset.ContentType))
                continue;

            result.Assets.Add(new LayoutStaticAssetDto
            {
                AssetId = asset.AssetId,
                ContentType = asset.ContentType,
                Content = Convert.ToBase64String(asset.Content.Span),
                LoadHints = SerializeLoadHints(asset.LoadHints),
                ContentSecurityPolicy = asset.ContentSecurityPolicy,
            });
        }

        return result;
    }

    private static LayoutStaticAssetLoadHintsDto? SerializeLoadHints(LayoutStaticAssetLoadHints? hints)
    {
        if (hints is null) return null;
        return new LayoutStaticAssetLoadHintsDto
        {
            ModuleKind = hints.ModuleKind switch
            {
                LayoutScriptModuleKind.Module => "module",
                _ => "classic",
            },
            LoadMode = hints.LoadMode switch
            {
                LayoutScriptLoadMode.Async => "async",
                LayoutScriptLoadMode.Blocking => "blocking",
                _ => "defer",
            },
            Placement = hints.Placement switch
            {
                LayoutScriptPlacement.BeforeLayoutHtml => "beforeLayoutHtml",
                _ => "afterLayoutHtml",
            },
        };
    }

    public static async Task<object?> HandleInteractAsync(NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LayoutInteractParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/interact");

        if (string.IsNullOrWhiteSpace(p.ExtensionId))
            throw new JsonException("layout/interact requires 'extensionId'.");
        if (string.IsNullOrWhiteSpace(p.LayoutId))
            throw new JsonException("layout/interact requires 'layoutId'.");

        // Reserved interaction types in the "verso/" namespace are handled by the host
        // before any registered ILayoutInteractionHandler is consulted. These shortcuts
        // let layout-author scripts trigger host actions (e.g. re-render) without forcing
        // every layout to implement a handler just to forward the action.
        if (IsReservedInteractionType(p.InteractionType))
        {
            await DispatchReservedInteractionAsync(ns, p).ConfigureAwait(false);
            return null;
        }

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

        // Render requests the handler makes are deferred, not sent inline, so they can be flushed
        // AFTER a cell-collection change is announced (below). The client refreshes its cell cache
        // on notebook/cellsChanged and re-renders the layout HTML on layout/updated; if the render
        // arrives first the new slot paints before the cell cache catches up, so the cell only
        // portals in a beat later (a visible two-step on insert/delete). Announcing the cells first
        // lets the re-render mount the new cell in the same frame its slot appears.
        var deferredRenders = new List<Action>();
        var context = new LayoutInteractionContext
        {
            ExtensionId = extensionId,
            LayoutId = layoutId,
            FrameInstanceId = frameInstanceId,
            InteractionType = p.InteractionType ?? string.Empty,
            Payload = p.Payload ?? string.Empty,
            TargetId = p.TargetId,
            Verso = new HostVersoContext(ns.Scaffold, ns: ns),
            CancellationToken = CancellationToken.None,
            RequestRender = () => deferredRenders.Add(
                () => ns.SendLayoutUpdated(extensionId, layoutId, frameInstanceId, "full")),
            RequestCellRefresh = cellId => deferredRenders.Add(
                () => ns.SendLayoutUpdated(extensionId, layoutId, frameInstanceId, "cell", cellId))
        };

        // Snapshot the cell identity sequence so we can tell whether the interaction mutated the
        // cell collection (insert, remove, or reorder). Joined ids capture order changes too.
        var cellsBefore = string.Join(",", ns.Scaffold.Cells.Select(c => c.Id));

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

        // A layout interaction handler may mutate kernel variables, and used to say so here on
        // every interaction whether it had or not. The session now watches the store itself and
        // reports what actually changed, which covers this and covers a change made with nothing
        // running. Saying it again from here would be worse than redundant: a layout that reacts
        // to variables by interacting would be answered with another announcement, and the two
        // would chase each other for as long as the notebook stayed open.

        // If the interaction changed the cell collection (e.g. a custom layout's own insert/move/
        // delete affordance, which mutates the host's cells rather than the client's), tell the
        // client to refresh its cell cache. Without this its pool stays stale and a newly added
        // cell's slot renders empty. The in-process server host shares one cell collection and so
        // does not need this.
        var cellsAfter = string.Join(",", ns.Scaffold.Cells.Select(c => c.Id));
        if (!string.Equals(cellsBefore, cellsAfter, StringComparison.Ordinal))
            ns.SendNotification(MethodNames.NotebookCellsChanged);

        // Flush the handler's render requests now, after any cell-collection change has been
        // announced, so the client re-renders the layout with a cell cache that already reflects
        // the change and the new cell portals into its slot in one frame.
        //
        // ORDERING CONTRACT (read before reordering these sends): cellsChanged MUST precede the
        // render flush. The out-of-process client services them as two independent round-trips
        // (notebook/list to refresh the cell cache, vs renderActiveLayout for the HTML). Sending
        // the render first lets it read a stale cache and paint the new slot before the cell
        // exists client-side, which shows as the new cell briefly merged into its neighbour. A
        // residual race remains even in this order: nothing guarantees the cache-refresh reply
        // lands before the render reply (a large notebook or a busy connection can let the cheaper
        // render reply overtake it). That race is BENIGN by construction, not luck: an unfilled
        // slot is hidden by CSS, and CustomLayoutHtml's portalPending pass (driven by the same
        // cellsChanged) always fills the slot once the cache catches up, so the final state is
        // always correct. The only deterministic close, if a transient ever becomes objectionable,
        // is to make the client's "full" render await any in-flight cell refresh before portaling.
        foreach (var render in deferredRenders)
            render();

        return null;
    }

    public static LayoutAllocateFrameInstanceResult HandleAllocateFrameInstance(
        NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LayoutAllocateFrameInstanceParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/allocateFrameInstance");

        if (string.IsNullOrWhiteSpace(p.ExtensionId))
            throw new JsonException("layout/allocateFrameInstance requires 'extensionId'.");
        if (string.IsNullOrWhiteSpace(p.LayoutId))
            throw new JsonException("layout/allocateFrameInstance requires 'layoutId'.");

        if (!ns.ExtensionHost.TryGetLayoutEngine(p.ExtensionId, p.LayoutId, out var engine))
        {
            throw new InvalidOperationException(
                $"LAYOUT_ENGINE_NOT_FOUND: no layout engine is registered " +
                $"for (extensionId='{p.ExtensionId}', layoutId='{p.LayoutId}').");
        }

        if (engine!.RendererIsolation != LayoutRendererIsolation.Isolated)
        {
            throw new InvalidOperationException(
                $"LAYOUT_NOT_ISOLATED: layout (extensionId='{p.ExtensionId}', " +
                $"layoutId='{p.LayoutId}') does not declare RendererIsolation.Isolated; " +
                $"frame instances are only allocated for isolated renderers.");
        }

        return new LayoutAllocateFrameInstanceResult
        {
            FrameInstanceId = ns.FrameTracker.AllocateFrameInstanceId(p.LayoutId)
        };
    }

    public static async Task<LayoutRendererMountedResult> HandleRendererMountedAsync(
        NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LayoutRendererMountedParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/rendererMounted");

        if (string.IsNullOrWhiteSpace(p.ExtensionId))
            throw new JsonException("layout/rendererMounted requires 'extensionId'.");
        if (string.IsNullOrWhiteSpace(p.LayoutId))
            throw new JsonException("layout/rendererMounted requires 'layoutId'.");
        if (string.IsNullOrWhiteSpace(p.FrameInstanceId))
            throw new JsonException("layout/rendererMounted requires 'frameInstanceId'.");

        var channel = new IframeFrameChannel(p.FrameInstanceId, ns);

        IReadOnlyDictionary<string, object>? extra;
        try
        {
            extra = await ns.FrameTracker.InvokeMountedAsync(
                p.ExtensionId,
                p.LayoutId,
                p.FrameInstanceId,
                LayoutRendererIsolation.Isolated,
                channel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Preserve the originating extension id so dispatch-side errors are
            // diagnosable. The WASM caller falls back to omitting the 'extension'
            // field from verso/init when this surfaces as an InternalError, per
            // the lifecycle failure-mode contract.
            throw new InvalidOperationException($"[{p.ExtensionId}] {ex.Message}", ex);
        }

        return new LayoutRendererMountedResult { Extension = extra };
    }

    public static async Task<object?> HandleRendererUnmountedAsync(
        NotebookSession ns, JsonElement? @params)
    {
        var p = @params?.Deserialize<LayoutRendererUnmountedParams>(JsonRpcMessage.SerializerOptions)
            ?? throw new JsonException("Missing params for layout/rendererUnmounted");

        if (string.IsNullOrWhiteSpace(p.ExtensionId))
            throw new JsonException("layout/rendererUnmounted requires 'extensionId'.");
        if (string.IsNullOrWhiteSpace(p.LayoutId))
            throw new JsonException("layout/rendererUnmounted requires 'layoutId'.");
        if (string.IsNullOrWhiteSpace(p.FrameInstanceId))
            throw new JsonException("layout/rendererUnmounted requires 'frameInstanceId'.");

        try
        {
            await ns.FrameTracker.InvokeUnmountedAsync(
                p.ExtensionId,
                p.LayoutId,
                p.FrameInstanceId,
                LayoutRendererIsolation.Isolated).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[{p.ExtensionId}] {ex.Message}", ex);
        }

        return null;
    }

    /// <summary>
    /// Reserved interaction-type namespace. Types prefixed with <c>verso/</c> are
    /// intercepted by the host and routed to built-in actions rather than dispatched to
    /// a registered <see cref="ILayoutInteractionHandler"/>.
    /// </summary>
    public const string ReservedInteractionPrefix = "verso/";

    /// <summary>Reserved type that asks the host to re-render the layout.</summary>
    public const string ReservedInteractionRequestRender = "verso/requestRender";

    private static bool IsReservedInteractionType(string? interactionType) =>
        !string.IsNullOrEmpty(interactionType)
        && interactionType.StartsWith(ReservedInteractionPrefix, StringComparison.Ordinal);

    private static Task DispatchReservedInteractionAsync(NotebookSession ns, LayoutInteractParams p)
    {
        var extensionId = p.ExtensionId;
        var layoutId = p.LayoutId;
        var frameInstanceId = p.FrameInstanceId ?? string.Empty;

        switch (p.InteractionType)
        {
            case ReservedInteractionRequestRender:
                ns.SendLayoutUpdated(extensionId, layoutId, frameInstanceId, "full");
                return Task.CompletedTask;

            default:
                // Unknown reserved type. Drop silently so future additions on the client
                // do not throw against older hosts that have not yet learned them.
                return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Minimal IVersoContext for rendering layouts on the host side.
    /// </summary>
    internal sealed class HostVersoContext : IVersoContext
    {
        private readonly Scaffold _scaffold;
        private readonly IReadOnlySet<Guid> _collapsedSections;
        private readonly NotebookSession? _ns;

        public HostVersoContext(Scaffold scaffold, IReadOnlySet<Guid>? collapsedSections = null, NotebookSession? ns = null)
        {
            _scaffold = scaffold;
            _collapsedSections = collapsedSections ?? new HashSet<Guid>();
            _ns = ns;
        }

        public IVariableStore Variables => _scaffold.Variables;
        public CancellationToken CancellationToken => CancellationToken.None;
        public IThemeContext Theme => _scaffold.ThemeContext;
        public LayoutCapabilities LayoutCapabilities => _scaffold.LayoutCapabilities;
        public IExtensionHostContext ExtensionHost => _scaffold.ExtensionHostContext;
        public INotebookMetadata NotebookMetadata => new HostNotebookMetadata(_scaffold);
        public INotebookOperations Notebook => _scaffold.NotebookOps;
        public string? ActiveLayoutId => _scaffold.LayoutManager?.ActiveLayout?.LayoutId;
        public IReadOnlySet<Guid> CollapsedSections => _collapsedSections;
        public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;

        public Task RequestFileDownloadAsync(string fileName, string contentType, byte[] data)
        {
            // Reuse the same file/download path the toolbar export actions use, so a layout
            // interaction (e.g. export PNG) gets a native save dialog in the VS Code host.
            if (_ns is null)
                throw new NotSupportedException(Strings.Download_Unsupported);

            _ns.SendNotification(MethodNames.FileDownload, new
            {
                fileName,
                contentType,
                data = Convert.ToBase64String(data)
            });
            return Task.CompletedTask;
        }

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
