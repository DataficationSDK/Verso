namespace Verso.Abstractions;

/// <summary>
/// Contributes a section of editable properties to the cell properties panel.
/// Implement this interface to surface per-cell configuration in the UI.
/// Property values are persisted in <see cref="CellModel.Metadata"/>.
/// </summary>
public interface ICellPropertyProvider : IExtension
{
    /// <summary>
    /// The display order of this provider's section within the panel.
    /// Lower values appear first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Determines whether this provider contributes properties for the given cell.
    /// </summary>
    bool AppliesTo(CellModel cell, ICellRenderContext context);

    /// <summary>
    /// Returns the property section definition for the given cell.
    /// </summary>
    Task<PropertySection> GetPropertiesSectionAsync(CellModel cell, ICellRenderContext context);

    /// <summary>
    /// Called when the user changes a property value in the panel.
    /// The provider validates the change and writes it to cell metadata.
    /// </summary>
    Task OnPropertyChangedAsync(CellModel cell, string propertyName, object? value, ICellRenderContext context);
}
