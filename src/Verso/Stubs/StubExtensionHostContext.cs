using Verso.Abstractions;

namespace Verso.Stubs;

/// <summary>
/// Stub <see cref="IExtensionHostContext"/> that reflects registered kernels via a delegate
/// and returns empty lists for all other extension categories.
/// </summary>
public sealed class StubExtensionHostContext : IExtensionHostContext
{
    private readonly Func<IReadOnlyList<ILanguageKernel>> _getKernels;
    private readonly Func<IReadOnlyList<ICellRenderer>> _getRenderers;

    public StubExtensionHostContext(
        Func<IReadOnlyList<ILanguageKernel>> getKernels,
        Func<IReadOnlyList<ICellRenderer>>? getRenderers = null)
    {
        _getKernels = getKernels ?? throw new ArgumentNullException(nameof(getKernels));
        _getRenderers = getRenderers ?? (() => Array.Empty<ICellRenderer>());
    }

    /// <inheritdoc />
    public IReadOnlyList<IExtension> GetLoadedExtensions() => _getKernels();

    /// <inheritdoc />
    public IReadOnlyList<ILanguageKernel> GetKernels() => _getKernels();

    /// <inheritdoc />
    public IReadOnlyList<ICellRenderer> GetRenderers() => _getRenderers();

    /// <inheritdoc />
    public IReadOnlyList<IDataFormatter> GetFormatters() => Array.Empty<IDataFormatter>();

    /// <inheritdoc />
    public IReadOnlyList<ICellType> GetCellTypes() => Array.Empty<ICellType>();

    /// <inheritdoc />
    public IReadOnlyList<INotebookSerializer> GetSerializers() => Array.Empty<INotebookSerializer>();

    /// <inheritdoc />
    public IReadOnlyList<ILayoutEngine> GetLayouts() => Array.Empty<ILayoutEngine>();

    /// <inheritdoc />
    public IReadOnlyList<ITheme> GetThemes() => Array.Empty<ITheme>();

    /// <inheritdoc />
    public IReadOnlyList<INotebookPostProcessor> GetPostProcessors() => Array.Empty<INotebookPostProcessor>();

    /// <inheritdoc />
    public IReadOnlyList<ExtensionInfo> GetExtensionInfos() => Array.Empty<ExtensionInfo>();

    /// <inheritdoc />
    public Task EnableExtensionAsync(string extensionId) => Task.CompletedTask;

    /// <inheritdoc />
    public Task DisableExtensionAsync(string extensionId) => Task.CompletedTask;
}
