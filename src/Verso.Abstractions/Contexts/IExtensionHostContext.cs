namespace Verso.Abstractions;

/// <summary>
/// Provides access to the loaded extensions in the Verso host, grouped by category.
/// </summary>
public interface IExtensionHostContext
{
    /// <summary>
    /// Returns all extensions currently loaded in the host.
    /// </summary>
    /// <returns>A read-only list of loaded <see cref="IExtension"/> instances.</returns>
    IReadOnlyList<IExtension> GetLoadedExtensions();

    /// <summary>
    /// Returns all registered language kernels.
    /// </summary>
    /// <returns>A read-only list of <see cref="ILanguageKernel"/> instances.</returns>
    IReadOnlyList<ILanguageKernel> GetKernels();

    /// <summary>
    /// Returns all registered cell renderers.
    /// </summary>
    /// <returns>A read-only list of <see cref="ICellRenderer"/> instances.</returns>
    IReadOnlyList<ICellRenderer> GetRenderers();

    /// <summary>
    /// Returns all registered data formatters.
    /// </summary>
    /// <returns>A read-only list of <see cref="IDataFormatter"/> instances.</returns>
    IReadOnlyList<IDataFormatter> GetFormatters();

    /// <summary>
    /// Returns all registered cell types.
    /// </summary>
    /// <returns>A read-only list of <see cref="ICellType"/> instances.</returns>
    IReadOnlyList<ICellType> GetCellTypes();

    /// <summary>
    /// Returns all registered notebook serializers.
    /// </summary>
    /// <returns>A read-only list of <see cref="INotebookSerializer"/> instances.</returns>
    IReadOnlyList<INotebookSerializer> GetSerializers();

    /// <summary>
    /// Returns all registered layout engines.
    /// </summary>
    /// <returns>A read-only list of <see cref="ILayoutEngine"/> instances.</returns>
    IReadOnlyList<ILayoutEngine> GetLayouts();

    /// <summary>
    /// Returns all registered themes.
    /// </summary>
    /// <returns>A read-only list of <see cref="ITheme"/> instances.</returns>
    IReadOnlyList<ITheme> GetThemes();
}
