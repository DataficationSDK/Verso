using Verso.Abstractions;

namespace Verso.Tests.Helpers;

/// <summary>
/// <see cref="ICellRenderer"/> test double for auto-registration testing.
/// </summary>
public sealed class FakeCellRenderer : ICellRenderer
{
    public FakeCellRenderer(
        string extensionId = "com.test.renderer",
        string name = "Fake Renderer",
        string version = "1.0.0")
    {
        ExtensionId = extensionId;
        Name = name;
        Version = version;
    }

    public string ExtensionId { get; }
    public string Name { get; }
    public string Version { get; }
    public string? Author => null;
    public string? Description => null;

    public string CellTypeId => "fake";
    public string DisplayName => Name;
    public bool CollapsesInputOnExecute => false;

    public int OnLoadedCallCount { get; private set; }
    public int OnUnloadedCallCount { get; private set; }

    public Task OnLoadedAsync(IExtensionHostContext context)
    {
        OnLoadedCallCount++;
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync()
    {
        OnUnloadedCallCount++;
        return Task.CompletedTask;
    }

    public Task<RenderResult> RenderInputAsync(string source, ICellRenderContext context)
        => Task.FromResult(new RenderResult("text/plain", $"input:{source}"));

    public Task<RenderResult> RenderOutputAsync(CellOutput output, ICellRenderContext context)
        => Task.FromResult(new RenderResult("text/plain", $"output:{output.Content}"));

    public string? GetEditorLanguage() => null;
}
