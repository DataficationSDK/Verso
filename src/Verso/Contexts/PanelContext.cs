using Verso.Abstractions;

namespace Verso.Contexts;

/// <summary>
/// <see cref="IPanelContext"/> implementation backed by a <see cref="Scaffold"/>.
/// Hosts build one of these each time they ask a panel whether it is available or
/// ask it to produce content.
/// </summary>
/// <remarks>
/// Panels do not execute cells, so <see cref="WriteOutputAsync"/> and
/// <see cref="UpdateOutputAsync"/> have no cell to write to. A panel that wants to
/// affect the notebook goes through <see cref="IVersoContext.Notebook"/>, which is
/// the same route a toolbar action takes.
/// </remarks>
public sealed class PanelContext : IPanelContext
{
    private readonly Scaffold _scaffold;

    public PanelContext(Scaffold scaffold, Guid? selectedCellId, CancellationToken cancellationToken = default)
    {
        _scaffold = scaffold ?? throw new ArgumentNullException(nameof(scaffold));
        SelectedCellId = selectedCellId;
        CancellationToken = cancellationToken;
    }

    /// <inheritdoc />
    public Guid? SelectedCellId { get; }

    /// <inheritdoc />
    public IReadOnlyList<CellModel> NotebookCells => _scaffold.Cells;

    /// <inheritdoc />
    public IVariableStore Variables => _scaffold.Variables;

    /// <inheritdoc />
    public CancellationToken CancellationToken { get; }

    /// <inheritdoc />
    public IThemeContext Theme => _scaffold.ThemeContext;

    /// <inheritdoc />
    public LayoutCapabilities LayoutCapabilities => _scaffold.LayoutCapabilities;

    /// <inheritdoc />
    public IExtensionHostContext ExtensionHost => _scaffold.ExtensionHostContext;

    /// <inheritdoc />
    public INotebookMetadata NotebookMetadata => _scaffold.Metadata;

    /// <inheritdoc />
    public INotebookOperations Notebook => _scaffold.NotebookOps;

    /// <inheritdoc />
    public Task WriteOutputAsync(CellOutput output)
        => throw new NotSupportedException("A panel has no cell to write output to.");

    /// <inheritdoc />
    public Task UpdateOutputAsync(string outputBlockId, CellOutput output)
        => throw new NotSupportedException("A panel has no cell output to update.");
}
