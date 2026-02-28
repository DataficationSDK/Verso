namespace Verso.Abstractions;

/// <summary>
/// Supplemental interface for extensions that handle bidirectional interactions
/// from rendered cell content. Implement alongside a primary capability interface
/// (e.g. <c>IDataFormatter + ICellInteractionHandler</c>). Implementing
/// <see cref="ICellInteractionHandler"/> alone is not a valid extension capability.
/// </summary>
public interface ICellInteractionHandler
{
    /// <summary>
    /// Called when the host receives an interaction event from rendered cell content.
    /// </summary>
    /// <param name="context">Describes the interaction that occurred.</param>
    /// <returns>
    /// An optional response string to send back to the client, or <c>null</c> if no response is needed.
    /// </returns>
    Task<string?> OnCellInteractionAsync(CellInteractionContext context);
}
