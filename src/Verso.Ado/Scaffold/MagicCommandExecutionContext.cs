using Verso.Abstractions;

namespace Verso.Ado.Scaffold;

/// <summary>
/// Lightweight adapter wrapping <see cref="IMagicCommandContext"/> to satisfy <see cref="IExecutionContext"/>,
/// needed to call <see cref="ILanguageKernel.ExecuteAsync"/>.
/// </summary>
internal sealed class MagicCommandExecutionContext : IExecutionContext
{
    private readonly IMagicCommandContext _inner;

    public MagicCommandExecutionContext(IMagicCommandContext inner)
    {
        _inner = inner;
    }

    public IVariableStore Variables => _inner.Variables;
    public CancellationToken CancellationToken => _inner.CancellationToken;
    public IThemeContext Theme => _inner.Theme;
    public LayoutCapabilities LayoutCapabilities => _inner.LayoutCapabilities;
    public IExtensionHostContext ExtensionHost => _inner.ExtensionHost;
    public INotebookMetadata NotebookMetadata => _inner.NotebookMetadata;
    public INotebookOperations Notebook => _inner.Notebook;
    public Guid CellId => Guid.NewGuid();
    public int ExecutionCount => 0;

    public Task WriteOutputAsync(CellOutput output) => _inner.WriteOutputAsync(output);
    public Task DisplayAsync(CellOutput output) => _inner.WriteOutputAsync(output);
}
