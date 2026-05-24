using System.Text;
using Verso.Abstractions;

namespace Verso.Testing.Fakes;

/// <summary>
/// Test double that implements <see cref="ILayoutEngine"/> with
/// <see cref="LayoutRendererIsolation.Isolated"/> and returns a minimal renderer package
/// containing a single <c>main.js</c> entry and a CSP hint.
/// </summary>
public sealed class FakeIsolatedLayoutEngine : IExtension, ILayoutEngine
{
    public const string DefaultEntryPoint = "main.js";
    public const string DefaultMainJs = "console.log('hello');";
    public const string DefaultContentSecurityPolicy = "script-src 'self';";

    public FakeIsolatedLayoutEngine(
        string extensionId = "com.test.layout.isolated",
        string layoutId = "isolated-fake",
        string name = "Fake Isolated Layout Engine",
        string version = "1.0.0",
        LayoutRendererPackage? package = null)
    {
        ExtensionId = extensionId;
        LayoutId = layoutId;
        Name = name;
        Version = version;
        Package = package ?? new LayoutRendererPackage(
            EntryPoint: DefaultEntryPoint,
            Files: new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [DefaultEntryPoint] = Encoding.UTF8.GetBytes(DefaultMainJs)
            },
            ContentSecurityPolicy: DefaultContentSecurityPolicy);
    }

    public string ExtensionId { get; }
    public string Name { get; }
    public string Version { get; }
    public string? Author => null;
    public string? Description => null;

    /// <summary>
    /// The package returned by <see cref="GetRendererPackageAsync"/>. Set to <c>null</c>
    /// to simulate an isolated layout that has no package available.
    /// </summary>
    public LayoutRendererPackage? Package { get; set; }

    // --- ILayoutEngine ---

    public string LayoutId { get; }
    public string DisplayName => Name;
    public string? Icon => null;
    public LayoutCapabilities Capabilities => LayoutCapabilities.None;
    public bool RequiresCustomRenderer => true;
    public LayoutRendererIsolation RendererIsolation => LayoutRendererIsolation.Isolated;

    public Task<LayoutRendererPackage?> GetRendererPackageAsync(IVersoContext context)
        => Task.FromResult(Package);

    public Task<RenderResult> RenderLayoutAsync(IReadOnlyList<CellModel> cells, IVersoContext context)
        => Task.FromResult(new RenderResult("text/html", ""));

    public Task<CellContainerInfo> GetCellContainerAsync(Guid cellId, IVersoContext context)
        => Task.FromResult(new CellContainerInfo(cellId, 0, 0, 0, 0));

    public Task OnCellAddedAsync(Guid cellId, int index, IVersoContext context) => Task.CompletedTask;
    public Task OnCellRemovedAsync(Guid cellId, IVersoContext context) => Task.CompletedTask;
    public Task OnCellMovedAsync(Guid cellId, int newIndex, IVersoContext context) => Task.CompletedTask;
    public Dictionary<string, object> GetLayoutMetadata() => new();
    public Task ApplyLayoutMetadata(Dictionary<string, object> metadata, IVersoContext context) => Task.CompletedTask;

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
}
