namespace Verso.Abstractions;

/// <summary>
/// Context provided to <see cref="ICellInteractionHandler.OnCellInteractionAsync"/> describing
/// the interaction that occurred in a rendered cell.
/// </summary>
public sealed class CellInteractionContext
{
    /// <summary>
    /// The cell region that originated the interaction.
    /// </summary>
    public CellRegion Region { get; init; }

    /// <summary>
    /// Application-defined interaction type (e.g. "click", "paginate", "submit").
    /// </summary>
    public string InteractionType { get; init; } = "";

    /// <summary>
    /// Free-form payload from the client (e.g. JSON, form data, page number).
    /// </summary>
    public string Payload { get; init; } = "";

    /// <summary>
    /// Optional identifier of the output block to update in place.
    /// </summary>
    public string? OutputBlockId { get; init; }

    /// <summary>
    /// The unique identifier of the cell where the interaction occurred.
    /// </summary>
    public Guid CellId { get; init; }

    /// <summary>
    /// The extension identifier of the handler that should process this interaction.
    /// </summary>
    public string ExtensionId { get; init; } = "";

    /// <summary>
    /// Cancellation token for the interaction operation.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}
