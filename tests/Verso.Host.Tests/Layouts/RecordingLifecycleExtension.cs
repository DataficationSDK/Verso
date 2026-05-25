using Verso.Abstractions;

namespace Verso.Host.Tests.Layouts;

/// <summary>
/// In-memory fake that implements the full layout lifecycle surface and records
/// mount/unmount contexts so tests can assert what the handler observed.
/// </summary>
internal sealed class RecordingLifecycleExtension : IExtension, ILayoutEngine, ILayoutLifecycleHandler
{
    private readonly IDictionary<string, object>? _initPayload;

    public RecordingLifecycleExtension(
        string extensionId,
        string layoutId,
        IDictionary<string, object>? initPayload = null)
    {
        ExtensionId = extensionId;
        LayoutId = layoutId;
        _initPayload = initPayload;
    }

    public string ExtensionId { get; }
    public string Name => "Recording Lifecycle";
    public string Version => "1.0.0";
    public string? Author => null;
    public string? Description => null;
    public string LayoutId { get; }
    public string DisplayName => Name;
    public string? Icon => null;
    public LayoutCapabilities Capabilities => LayoutCapabilities.None;
    public bool RequiresCustomRenderer => true;
    public LayoutRendererIsolation RendererIsolation => LayoutRendererIsolation.Isolated;

    public List<LayoutRendererMountContext> MountContexts { get; } = new();
    public List<LayoutRendererUnmountContext> UnmountContexts { get; } = new();
    public bool ThrowOnMount { get; set; }
    public bool ThrowOnUnmount { get; set; }

    public Task<LayoutRendererPackage?> GetRendererPackageAsync(IVersoContext context)
        => Task.FromResult<LayoutRendererPackage?>(null);
    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
        => Task.FromResult(new RenderResult("text/html", ""));
    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
        => Task.FromResult(new CellContainerInfo(cellId, 0, 0, 0, 0));
    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;
    public Dictionary<string, object> GetLayoutMetadata() => new();
    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context) => Task.CompletedTask;

    public Task<IDictionary<string, object>?> OnRendererMountedAsync(LayoutRendererMountContext context)
    {
        MountContexts.Add(context);
        if (ThrowOnMount)
            throw new InvalidOperationException("mount boom");
        return Task.FromResult(_initPayload);
    }

    public Task OnRendererUnmountedAsync(LayoutRendererUnmountContext context)
    {
        UnmountContexts.Add(context);
        if (ThrowOnUnmount)
            throw new InvalidOperationException("unmount boom");
        return Task.CompletedTask;
    }

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
}
