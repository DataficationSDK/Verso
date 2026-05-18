using Verso.Abstractions;

namespace Verso.Testing.Fakes;

/// <summary>
/// Test double that implements both <see cref="ILayoutEngine"/> and
/// <see cref="ILayoutInteractionHandler"/> on a single class, matching the
/// typical layout-extension shape. Records every interaction received and
/// exposes an optional callback for tests that need to drive handler behavior.
/// </summary>
public sealed class FakeLayoutInteractionHandler : IExtension, ILayoutEngine, ILayoutInteractionHandler
{
    public FakeLayoutInteractionHandler(
        string extensionId = "com.test.layout",
        string layoutId = "fake-layout",
        string name = "Fake Layout Interaction Handler",
        string version = "1.0.0")
    {
        ExtensionId = extensionId;
        LayoutId = layoutId;
        Name = name;
        Version = version;
    }

    public string ExtensionId { get; }
    public string Name { get; }
    public string Version { get; }
    public string? Author => null;
    public string? Description => null;

    // --- ILayoutEngine ---

    public string LayoutId { get; }
    public string DisplayName => Name;
    public string? Icon => null;
    public LayoutCapabilities Capabilities => LayoutCapabilities.None;
    public bool RequiresCustomRenderer => false;

    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
        => Task.FromResult(new RenderResult("text/html", ""));

    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
        => Task.FromResult(new CellContainerInfo(cellId, 0, 0, 0, 0));

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;
    public Dictionary<string, object> GetLayoutMetadata() => new();
    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context) => Task.CompletedTask;

    // --- ILayoutInteractionHandler ---

    public List<LayoutInteractionContext> ReceivedInteractions { get; } = new();
    public Func<LayoutInteractionContext, Task>? OnInteraction { get; set; }

    public Task OnLayoutInteractionAsync(LayoutInteractionContext context)
    {
        ReceivedInteractions.Add(context);
        return OnInteraction?.Invoke(context) ?? Task.CompletedTask;
    }

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
}
