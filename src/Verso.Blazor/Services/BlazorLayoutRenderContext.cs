using Verso.Abstractions;

namespace Verso.Blazor.Services;

/// <summary>
/// Minimal <see cref="IVersoContext"/> passed to <see cref="ILayoutEngine.RenderLayoutAsync"/>
/// from the Blazor Server hosting mode. Mirrors the host's layout-render context: write
/// operations on cell output are no-ops because the layout renderer does not stream output.
/// </summary>
internal sealed class BlazorLayoutRenderContext : IVersoContext
{
    private readonly Scaffold _scaffold;

    public BlazorLayoutRenderContext(Scaffold scaffold)
    {
        _scaffold = scaffold ?? throw new ArgumentNullException(nameof(scaffold));
    }

    public IVariableStore Variables => _scaffold.Variables;
    public CancellationToken CancellationToken => CancellationToken.None;
    public IThemeContext Theme => _scaffold.ThemeContext;
    public LayoutCapabilities LayoutCapabilities => _scaffold.LayoutCapabilities;
    public IExtensionHostContext ExtensionHost => _scaffold.ExtensionHostContext;
    public INotebookMetadata NotebookMetadata => new BlazorNotebookMetadata(_scaffold);
    public INotebookOperations Notebook => _scaffold.NotebookOps;
    public string? ActiveLayoutId => _scaffold.LayoutManager?.ActiveLayout?.LayoutId;

    public Task WriteOutputAsync(CellOutput output) => Task.CompletedTask;

    private sealed class BlazorNotebookMetadata : INotebookMetadata
    {
        private readonly Scaffold _s;
        public BlazorNotebookMetadata(Scaffold s) => _s = s;
        public string? Title => _s.Title;
        public string? DefaultKernelId => _s.DefaultKernelId;
        public string? FilePath => null;
        public Dictionary<string, NotebookParameterDefinition>? Parameters => _s.Notebook.Parameters;
    }
}
