using Verso.Abstractions;

namespace Verso.Stubs;

/// <summary>
/// Stub <see cref="IExtensionHostContext"/> that reflects registered kernels via a delegate
/// and returns empty lists for all other extension categories.
/// </summary>
public sealed class StubExtensionHostContext : IExtensionHostContext
{
    private readonly Func<IReadOnlyList<ILanguageKernel>> _getKernels;

    public StubExtensionHostContext(Func<IReadOnlyList<ILanguageKernel>> getKernels)
    {
        _getKernels = getKernels ?? throw new ArgumentNullException(nameof(getKernels));
    }

    /// <inheritdoc />
    public IReadOnlyList<IExtension> GetLoadedExtensions() => _getKernels();

    /// <inheritdoc />
    public IReadOnlyList<ILanguageKernel> GetKernels() => _getKernels();

    /// <inheritdoc />
    public IReadOnlyList<ICellRenderer> GetRenderers() => Array.Empty<ICellRenderer>();

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
}
